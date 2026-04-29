using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Nugget.Services.Models;
using System.IO.Compression;
using System.Xml.Linq;
using NuGet.Versioning;
using Minio;
using Minio.DataModel.Args;
using System.Text.Json;
using System.Text;

namespace Nugget.Services;

public class PackageStorageService
{
    private readonly ILogger<PackageStorageService> logger_;
    private readonly IMinioClient minio_;
    private readonly string bucket_;

    public PackageStorageService(
        ILogger<PackageStorageService> logger,
        IMinioClient minio,
        IConfiguration config)
    {
        logger_ = logger;
        minio_ = minio;
        bucket_ = config["Minio:Bucket"] ?? "nugget";
    }

    private async Task EnsureBucketExistsAsync()
    {
        var beArgs = new BucketExistsArgs().WithBucket(bucket_);
        bool found = await minio_.BucketExistsAsync(beArgs);
        if (!found)
        {
            var mbArgs = new MakeBucketArgs().WithBucket(bucket_);
            await minio_.MakeBucketAsync(mbArgs);
        }
    }

    public async Task AddPackageAsync(IFormFile file)
    {
        await EnsureBucketExistsAsync();
        logger_.LogInformation("Attempting to add package to Minio...");

        // 1. Read package metadata from the .nuspec file
        PackageMetadata metadata;
        using (Stream stream = file.OpenReadStream())
        using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Read, true))
        {
            ZipArchiveEntry? nuspec_entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".nuspec"));
            if (nuspec_entry == null)
            {
                throw new InvalidDataException("'.nuspec' file not found in package.");
            }

            using (Stream nuspec_stream = nuspec_entry.Open())
            {
                metadata = ParseNuspec(nuspec_stream);
            }
        }

        string id_lower = metadata.id.ToLower();
        string version_lower = metadata.version.ToLower();

        // 2. Check if this version already exists in Registration
        var registration = await GetRegistrationFromMinioAsync(id_lower);
        if (registration != null)
        {
            bool exists = registration.items.SelectMany(p => p.items ?? new List<RegistrationPageItem>())
                                           .Any(i => i.catalog_entry.version.ToLower() == version_lower);
            if (exists)
            {
                throw new InvalidOperationException($"Package '{id_lower}' version '{version_lower}' already exists.");
            }
        }

        // 3. Upload .nupkg to Minio
        string nupkg_object = $"v3/package/{id_lower}/{version_lower}/{id_lower}.{version_lower}.nupkg";
        using (var uploadStream = file.OpenReadStream())
        {
            var putArgs = new PutObjectArgs()
                .WithBucket(bucket_)
                .WithObject(nupkg_object)
                .WithStreamData(uploadStream)
                .WithObjectSize(file.Length)
                .WithContentType("application/octet-stream");
            await minio_.PutObjectAsync(putArgs);
        }

        // 4. Upload .nuspec to Minio
        string nuspec_object = $"v3/package/{id_lower}/{version_lower}/{id_lower}.nuspec";
        using (Stream stream = file.OpenReadStream())
        using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            ZipArchiveEntry? nuspec_entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".nuspec"));
            if (nuspec_entry != null)
            {
                using (Stream nuspec_stream = nuspec_entry.Open())
                using (MemoryStream ms = new MemoryStream())
                {
                    await nuspec_stream.CopyToAsync(ms);
                    ms.Position = 0;
                    var putArgs = new PutObjectArgs()
                        .WithBucket(bucket_)
                        .WithObject(nuspec_object)
                        .WithStreamData(ms)
                        .WithObjectSize(ms.Length)
                        .WithContentType("text/xml");
                    await minio_.PutObjectAsync(putArgs);
                }
            }
        }

        // 5. Update Registration Index
        await UpdateRegistrationAsync(id_lower, metadata);

        // 6. Update Search Index
        await UpdateSearchIndexAsync(metadata);

        // 7. Update Package Versions Index (Flat Container index.json)
        await UpdatePackageVersionsIndexAsync(id_lower, metadata.version);
    }

    private async Task UpdatePackageVersionsIndexAsync(string id, string version)
    {
        string object_name = $"v3/package/{id}/index.json";
        var response = await GetJsonFromMinioAsync<PackageVersionsResponse>(object_name) ?? new PackageVersionsResponse();
        
        if (!response.versions.Any(v => v.ToLower() == version.ToLower()))
        {
            response.versions.Add(version);
            response.versions = response.versions
                .Select(v => NuGetVersion.Parse(v))
                .OrderBy(v => v)
                .Select(v => v.ToNormalizedString())
                .ToList();
            
            await SaveJsonToMinioAsync(object_name, response);
        }
    }

    private async Task UpdateRegistrationAsync(string id, PackageMetadata metadata)
    {
        var registration = await GetRegistrationFromMinioAsync(id) ?? new RegistrationIndexResponse
        {
            count = 0,
            items = new List<RegistrationPage>()
        };

        var page = registration.items.FirstOrDefault() ?? new RegistrationPage
        {
            items = new List<RegistrationPageItem>()
        };

        if (!registration.items.Contains(page))
        {
            registration.items.Add(page);
        }

        page.items.Add(new RegistrationPageItem
        {
            catalog_entry = new RegistrationCatalogEntry
            {
                id = metadata.id,
                version = metadata.version,
                description = metadata.description,
                authors = metadata.authors,
                package_content = "" // Will be updated on retrieval
            }
        });

        // Re-sort versions
        var sortedItems = page.items
            .OrderBy(i => NuGetVersion.Parse(i.catalog_entry.version))
            .ToList();
        
        page.items = sortedItems;
        page.lower = sortedItems.First().catalog_entry.version;
        page.upper = sortedItems.Last().catalog_entry.version;
        page.count = sortedItems.Count;
        registration.count = 1;

        await SaveJsonToMinioAsync($"v3/registration/{id}/index.json", registration);
    }

    private async Task UpdateSearchIndexAsync(PackageMetadata metadata)
    {
        var searchIndex = await GetSearchIndexFromMinioAsync() ?? new SearchResponse
        {
            data = new List<SearchHit>(),
            total_hits = 0
        };

        var hit = searchIndex.data.FirstOrDefault(h => h.id.ToLower() == metadata.id.ToLower());
        if (hit == null)
        {
            hit = new SearchHit
            {
                id = metadata.id,
                description = metadata.description,
                authors = metadata.authors.Split(','),
                versions = new List<SearchVersion>()
            };
            searchIndex.data.Add(hit);
        }

        if (!hit.versions.Any(v => v.version.ToLower() == metadata.version.ToLower()))
        {
            hit.versions.Add(new SearchVersion { version = metadata.version });
            var latest = hit.versions.OrderByDescending(v => NuGetVersion.Parse(v.version)).First();
            hit.version = latest.version;
        }

        searchIndex.total_hits = searchIndex.data.Count;
        await SaveJsonToMinioAsync("v3/search/index.json", searchIndex);
    }

    private async Task<RegistrationIndexResponse?> GetRegistrationFromMinioAsync(string id)
    {
        return await GetJsonFromMinioAsync<RegistrationIndexResponse>($"v3/registration/{id}/index.json");
    }

    private async Task<SearchResponse?> GetSearchIndexFromMinioAsync()
    {
        return await GetJsonFromMinioAsync<SearchResponse>("v3/search/index.json");
    }

    private async Task<T?> GetJsonFromMinioAsync<T>(string objectName) where T : class
    {
        try
        {
            using (MemoryStream ms = new MemoryStream())
            {
                var getArgs = new GetObjectArgs()
                    .WithBucket(bucket_)
                    .WithObject(objectName)
                    .WithCallbackStream(s => s.CopyTo(ms));
                await minio_.GetObjectAsync(getArgs);
                ms.Position = 0;
                return await JsonSerializer.DeserializeAsync<T>(ms);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task SaveJsonToMinioAsync<T>(string objectName, T data)
    {
        string json = JsonSerializer.Serialize(data);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        using (MemoryStream ms = new MemoryStream(bytes))
        {
            var putArgs = new PutObjectArgs()
                .WithBucket(bucket_)
                .WithObject(objectName)
                .WithStreamData(ms)
                .WithObjectSize(bytes.Length)
                .WithContentType("application/json");
            await minio_.PutObjectAsync(putArgs);
        }
    }

    private PackageMetadata ParseNuspec(Stream stream)
    {
        XDocument xml = XDocument.Load(stream);
        XElement? metadata_node = xml.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata");
        if (metadata_node == null)
        {
            throw new InvalidDataException("Invalid .nuspec: 'metadata' node not found.");
        }

        return new PackageMetadata
        {
            id = metadata_node.Elements().First(e => e.Name.LocalName == "id").Value,
            version = metadata_node.Elements().First(e => e.Name.LocalName == "version").Value,
            description = metadata_node.Elements().FirstOrDefault(e => e.Name.LocalName == "description")?.Value ?? "",
            authors = metadata_node.Elements().FirstOrDefault(e => e.Name.LocalName == "authors")?.Value ?? "N/A",
        };
    }

    public ServiceIndexResponse GetServiceIndex(HttpContext context)
    {
        string base_url = GetBaseUrl(context);
        return new ServiceIndexResponse
        {
            resources = new List<ServiceResource>
            {
                new ServiceResource { type = "PackagePublish/2.0.0", id = $"{base_url}/v3/packages" },
                new ServiceResource { type = "SearchQueryService/3.0.0-beta", id = $"{base_url}/v3/search" },
                new ServiceResource { type = "RegistrationsBaseUrl/3.0.0-beta", id = $"{base_url}/v3/registration" },
                new ServiceResource { type = "PackageBaseAddress/3.0.0", id = $"{base_url}/v3/package" }
            }
        };
    }

    private string GetBaseUrl(HttpContext context)
    {
        return $"{context.Request.Scheme}://{context.Request.Host}";
    }

    public async Task<SearchResponse> SearchPackagesAsync(string query, int skip, int take, bool prerelease, string semVerLevel)
    {
        var index = await GetSearchIndexFromMinioAsync();
        if (index == null) return new SearchResponse { data = new List<SearchHit>(), total_hits = 0 };

        var filtered = index.data
            .Where(h => string.IsNullOrEmpty(query) || h.id.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // 1. Filter by prerelease if needed
        if (!prerelease)
        {
            foreach (var hit in filtered)
            {
                hit.versions = hit.versions.Where(v => !NuGetVersion.Parse(v.version).IsPrerelease).ToList();
            }
            // Remove hits that have no stable versions left
            filtered = filtered.Where(h => h.versions.Any()).ToList();
            // Update 'version' to the latest stable
            foreach (var hit in filtered)
            {
                hit.version = hit.versions.OrderByDescending(v => NuGetVersion.Parse(v.version)).First().version;
            }
        }

        // 2. Filter by semVerLevel (2.0.0)
        bool allowSemVer2 = NuGetVersion.Parse(semVerLevel) >= new NuGetVersion(2, 0, 0);
        if (!allowSemVer2)
        {
            foreach (var hit in filtered)
            {
                hit.versions = hit.versions.Where(v => !NuGetVersion.Parse(v.version).IsSemVer2).ToList();
            }
            filtered = filtered.Where(h => h.versions.Any()).ToList();
            foreach (var hit in filtered)
            {
                hit.version = hit.versions.OrderByDescending(v => NuGetVersion.Parse(v.version)).First().version;
            }
        }

        int totalHits = filtered.Count;

        // 3. Apply Pagination (Skip/Take)
        var paged = filtered
            .Skip(skip)
            .Take(take)
            .ToList();

        return new SearchResponse { data = paged, total_hits = totalHits };
    }

    public async Task<RegistrationIndexResponse?> GetRegistrationAsync(string id, HttpContext context)
    {
        var registration = await GetRegistrationFromMinioAsync(id.ToLower());
        if (registration == null) return null;

        string base_url = GetBaseUrl(context);

        foreach (var page in registration.items)
        {
            if (page.items != null)
            {
                foreach (var item in page.items)
                {
                    if (item.catalog_entry != null)
                    {
                        item.catalog_entry.package_content = $"{base_url}/v3/package/{item.catalog_entry.id}/{item.catalog_entry.version}/{item.catalog_entry.id}.{item.catalog_entry.version}.nupkg";
                    }
                }
            }
        }

        return registration;
    }

    public async Task<string> GetDownloadUrlAsync(string id, string version)
    {
        string object_name = $"v3/package/{id.ToLower()}/{version.ToLower()}/{id.ToLower()}.{version.ToLower()}.nupkg";
        var args = new PresignedGetObjectArgs()
            .WithBucket(bucket_)
            .WithObject(object_name)
            .WithExpiry(3600);
        
        return await minio_.PresignedGetObjectAsync(args);
    }

    public async Task<PackageVersionsResponse?> GetPackageVersionsAsync(string id)
    {
        string object_name = $"v3/package/{id.ToLower()}/index.json";
        return await GetJsonFromMinioAsync<PackageVersionsResponse>(object_name);
    }

    public async Task<string> GetNuspecUrlAsync(string id, string version)
    {
        string object_name = $"v3/package/{id.ToLower()}/{version.ToLower()}/{id.ToLower()}.nuspec";
        var args = new PresignedGetObjectArgs()
            .WithBucket(bucket_)
            .WithObject(object_name)
            .WithExpiry(3600);
        
        return await minio_.PresignedGetObjectAsync(args);
    }
}