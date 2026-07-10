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
        BucketExistsArgs? be_args = new BucketExistsArgs().WithBucket(bucket_);
        bool found = await minio_.BucketExistsAsync(be_args);
        if (!found)
        {
            MakeBucketArgs? mb_args = new MakeBucketArgs().WithBucket(bucket_);
            await minio_.MakeBucketAsync(mb_args);
        }
    }

    public async Task AddPackageAsync(Stream package_stream, long length)
    {
        await EnsureBucketExistsAsync();
        logger_.LogInformation("Attempting to add package to Minio...");

        // Buffer the entire stream so we can read it multiple times
        using MemoryStream buffered = new MemoryStream();
        await package_stream.CopyToAsync(buffered);
        buffered.Position = 0;

        // 1. Read package metadata from the .nuspec file
        PackageMetadata metadata;
        using (ZipArchive zip = new ZipArchive(buffered, ZipArchiveMode.Read, leaveOpen: true))
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
        string version_lower = NuGetVersion.Parse(metadata.version).ToNormalizedString().ToLower();

        // 2. Check if this version already exists in Registration
        RegistrationIndexResponse? registration = await GetRegistrationFromMinioAsync(id_lower);
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
        buffered.Position = 0;
        PutObjectArgs? put_args = new PutObjectArgs()
                                 .WithBucket(bucket_)
                                 .WithObject(nupkg_object)
                                 .WithStreamData(buffered)
                                 .WithObjectSize(buffered.Length)
                                 .WithContentType("application/octet-stream");
        await minio_.PutObjectAsync(put_args);

        // 4. Upload .nuspec to Minio
        string nuspec_object = $"v3/package/{id_lower}/{version_lower}/{id_lower}.nuspec";
        buffered.Position = 0;
        using (ZipArchive zip = new ZipArchive(buffered, ZipArchiveMode.Read, leaveOpen: true))
        {
            ZipArchiveEntry? nuspec_entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".nuspec"));
            if (nuspec_entry != null)
            {
                using (Stream nuspec_stream = nuspec_entry.Open())
                using (MemoryStream ms = new MemoryStream())
                {
                    await nuspec_stream.CopyToAsync(ms);
                    ms.Position = 0;
                    PutObjectArgs? nuspec_put_args = new PutObjectArgs()
                                                   .WithBucket(bucket_)
                                                   .WithObject(nuspec_object)
                                                   .WithStreamData(ms)
                                                   .WithObjectSize(ms.Length)
                                                   .WithContentType("text/xml");
                    await minio_.PutObjectAsync(nuspec_put_args);
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
        PackageVersionsResponse response = await GetJsonFromMinioAsync<PackageVersionsResponse>(object_name) ?? new PackageVersionsResponse();
        
        string normalized_version = NuGetVersion.Parse(version).ToNormalizedString().ToLower();
        if (!response.versions.Any(v => v.ToLower() == normalized_version))
        {
            response.versions.Add(normalized_version);
            response.versions = response.versions
                .Select(v => NuGetVersion.Parse(v))
                .OrderBy(v => v)
                .Select(v => v.ToNormalizedString().ToLower())
                .ToList();
            
            await SaveJsonToMinioAsync(object_name, response);
        }
    }

    private async Task UpdateRegistrationAsync(string id, PackageMetadata metadata)
    {
        RegistrationIndexResponse registration = await GetRegistrationFromMinioAsync(id) ?? new RegistrationIndexResponse
        {
            count = 0,
            items = new List<RegistrationPage>()
        };

        RegistrationPage page = registration.items.FirstOrDefault() ?? new RegistrationPage
        {
            items = new List<RegistrationPageItem>()
        };

        if (!registration.items.Contains(page))
        {
            registration.items.Add(page);
        }

        string normalized_version = NuGetVersion.Parse(metadata.version).ToNormalizedString();
        string id_lower = id.ToLower();

        page.items.Add(new RegistrationPageItem
        {
            catalog_entry = new RegistrationCatalogEntry
            {
                package_id = metadata.id,
                version = normalized_version,
                description = metadata.description,
                authors = metadata.authors,
                package_content = "", // Will be set on retrieval with base URL
                listed = true
            }
        });

        // Re-sort versions
        List<RegistrationPageItem> sorted_items = page.items
                                                     .Select(i => {
                                                         i.catalog_entry.version = NuGetVersion.Parse(i.catalog_entry.version).ToNormalizedString();
                                                         return i;
                                                     })
                                                     .OrderBy(i => NuGetVersion.Parse(i.catalog_entry.version))
                                                     .ToList();
        
        page.items = sorted_items;
        page.lower = sorted_items.First().catalog_entry.version;
        page.upper = sorted_items.Last().catalog_entry.version;
        page.count = sorted_items.Count;
        registration.count = 1;

        await SaveJsonToMinioAsync($"v3/registration/{id}/index.json", registration);
    }

    private async Task UpdateSearchIndexAsync(PackageMetadata metadata)
    {
        SearchResponse search_index = await GetSearchIndexFromMinioAsync() ?? new SearchResponse
        {
            data = new List<SearchHit>(),
            total_hits = 0
        };

        SearchHit? hit = search_index.data.FirstOrDefault(h => h.id.ToLower() == metadata.id.ToLower());
        if (hit == null)
        {
            hit = new SearchHit
            {
                id = metadata.id,
                description = metadata.description,
                authors = metadata.authors.Split(','),
                versions = new List<SearchVersion>()
            };
            search_index.data.Add(hit);
        }

        string normalized_version = NuGetVersion.Parse(metadata.version).ToNormalizedString();
        if (!hit.versions.Any(v => NuGetVersion.Parse(v.version).ToNormalizedString() == normalized_version))
        {
            hit.versions.Add(new SearchVersion { version = normalized_version });
            SearchVersion latest = hit.versions.OrderByDescending(v => NuGetVersion.Parse(v.version)).First();
            hit.version = latest.version;
        }

        search_index.total_hits = search_index.data.Count;
        await SaveJsonToMinioAsync("v3/search/index.json", search_index);
    }

    private async Task<RegistrationIndexResponse?> GetRegistrationFromMinioAsync(string id)
    {
        return await GetJsonFromMinioAsync<RegistrationIndexResponse>($"v3/registration/{id}/index.json");
    }

    private async Task<SearchResponse?> GetSearchIndexFromMinioAsync()
    {
        return await GetJsonFromMinioAsync<SearchResponse>("v3/search/index.json");
    }

    private async Task<T?> GetJsonFromMinioAsync<T>(string object_name) where T : class
    {
        try
        {
            using (MemoryStream ms = new MemoryStream())
            {
                GetObjectArgs? get_args = new GetObjectArgs()
                                         .WithBucket(bucket_)
                                         .WithObject(object_name)
                                         .WithCallbackStream(s => s.CopyTo(ms));
                await minio_.GetObjectAsync(get_args);
                ms.Position = 0;
                return await JsonSerializer.DeserializeAsync<T>(ms);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task SaveJsonToMinioAsync<T>(string object_name, T data)
    {
        string json = JsonSerializer.Serialize(data);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        using (MemoryStream ms = new MemoryStream(bytes))
        {
            PutObjectArgs? put_args = new PutObjectArgs()
                                     .WithBucket(bucket_)
                                     .WithObject(object_name)
                                     .WithStreamData(ms)
                                     .WithObjectSize(bytes.Length)
                                     .WithContentType("application/json");
            await minio_.PutObjectAsync(put_args);
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
                new ServiceResource { type = "SearchQueryService", id = $"{base_url}/v3/search" },
                new ServiceResource { type = "SearchQueryService/3.0.0-beta", id = $"{base_url}/v3/search" },
                new ServiceResource { type = "SearchQueryService/3.0.0-rc", id = $"{base_url}/v3/search" },
                new ServiceResource { type = "SearchQueryService/3.5.0-beta", id = $"{base_url}/v3/search" },
                new ServiceResource { type = "RegistrationsBaseUrl", id = $"{base_url}/v3/registration" },
                new ServiceResource { type = "RegistrationsBaseUrl/3.0.0-beta", id = $"{base_url}/v3/registration" },
                new ServiceResource { type = "RegistrationsBaseUrl/3.0.0-rc", id = $"{base_url}/v3/registration" },
                new ServiceResource { type = "RegistrationsBaseUrl/3.6.0", id = $"{base_url}/v3/registration" },
                new ServiceResource { type = "PackageBaseAddress/3.0.0", id = $"{base_url}/v3/package" },
                new ServiceResource { type = "SearchAutocompleteService", id = $"{base_url}/v3/autocomplete" },
                new ServiceResource { type = "SearchAutocompleteService/3.0.0-beta", id = $"{base_url}/v3/autocomplete" },
                new ServiceResource { type = "SearchAutocompleteService/3.5.0-beta", id = $"{base_url}/v3/autocomplete" },
            }
        };
    }

    private string GetBaseUrl(HttpContext context)
    {
        return $"{context.Request.Scheme}://{context.Request.Host}";
    }

    public async Task<SearchResponse> SearchPackagesAsync(string query, int skip, int take, bool prerelease, string sem_ver_level, HttpContext context)
    {
        SearchResponse? index = await GetSearchIndexFromMinioAsync();
        if (index == null) return new SearchResponse { data = new List<SearchHit>(), total_hits = 0 };

        List<SearchHit> filtered = index.data
                                        .Where(h => string.IsNullOrEmpty(query) || h.id.Contains(query, StringComparison.OrdinalIgnoreCase))
                                        .Select(h => new SearchHit
                                        {
                                            id = h.id,
                                            description = h.description,
                                            authors = h.authors,
                                            version = h.version,
                                            versions = h.versions.ToList(),
                                            registration_id = h.registration_id,
                                            registration = h.registration,
                                            total_downloads = h.total_downloads,
                                            verified = h.verified
                                        })
                                        .ToList();

        // 1. Filter by prerelease if needed
        if (!prerelease)
        {
            foreach (SearchHit hit in filtered)
            {
                hit.versions = hit.versions.Where(v => !NuGetVersion.Parse(v.version).IsPrerelease).ToList();
            }
            // Remove hits that have no stable versions left
            filtered = filtered.Where(h => h.versions.Any()).ToList();
            // Update 'version' to the latest stable
            foreach (SearchHit hit in filtered)
            {
                hit.version = hit.versions.OrderByDescending(v => NuGetVersion.Parse(v.version)).First().version;
            }
        }

        // 2. Filter by semVerLevel (2.0.0)
        if (!string.IsNullOrEmpty(sem_ver_level))
        {
            bool allow_sem_ver2 = NuGetVersion.TryParse(sem_ver_level, out NuGetVersion? parsed_level) 
                                && parsed_level >= new NuGetVersion(2, 0, 0);
            if (!allow_sem_ver2)
            {
                foreach (SearchHit hit in filtered)
                {
                    hit.versions = hit.versions.Where(v => !NuGetVersion.Parse(v.version).IsSemVer2).ToList();
                }
                filtered = filtered.Where(h => h.versions.Any()).ToList();
                foreach (SearchHit hit in filtered)
                {
                    hit.version = hit.versions.OrderByDescending(v => NuGetVersion.Parse(v.version)).First().version;
                }
            }
        }

        int total_hits = filtered.Count;

        // 3. Apply Pagination (Skip/Take)
        List<SearchHit> paged = filtered
                                .Skip(skip)
                                .Take(take)
                                .ToList();

        string base_url = GetBaseUrl(context);
        foreach (SearchHit hit in paged)
        {
            string id_lower = hit.id.ToLower();
            hit.registration_id = $"{base_url}/v3/registration/{id_lower}/index.json";
            hit.registration = $"{base_url}/v3/registration/{id_lower}/index.json";
            foreach (SearchVersion v in hit.versions)
            {
                v.id = $"{base_url}/v3/registration/{id_lower}/index.json";
            }
        }

        return new SearchResponse { data = paged, total_hits = total_hits };
    }

    public async Task<RegistrationIndexResponse?> GetRegistrationAsync(string id, HttpContext context)
    {
        RegistrationIndexResponse? registration = await GetRegistrationFromMinioAsync(id.ToLower());
        if (registration == null) return null;

        string base_url = GetBaseUrl(context);
        string id_lower = id.ToLower();
        string reg_url = $"{base_url}/v3/registration/{id_lower}/index.json";

        // Set the @id on the registration index itself
        registration.id = reg_url;

        foreach (RegistrationPage page in registration.items)
        {
            page.id = reg_url; 
            if (page.items != null)
            {
                foreach (RegistrationPageItem item in page.items)
                {
                    if (item.catalog_entry != null)
                    {
                        string normalized_version = NuGetVersion.Parse(item.catalog_entry.version).ToNormalizedString();
                        string version_lower = normalized_version.ToLower();
                        
                        // @id for the page item is a fragment URL
                        item.id = $"{base_url}/v3/registration/{id_lower}/{version_lower}.json";
                        
                        // catalogEntry @id is a URL for the catalog entry resource
                        item.catalog_entry.id = $"{base_url}/v3/registration/{id_lower}/{version_lower}.json";
                        
                        // package_id is the human-readable package ID (preserve original case if available)
                        // For legacy data where package_id was not stored, use the id from the URL path
                        if (string.IsNullOrEmpty(item.catalog_entry.package_id))
                        {
                            item.catalog_entry.package_id = id;
                        }
                        
                        // packageContent is the download URL for the .nupkg — use lowercased IDs to match S3 keys
                        item.catalog_entry.package_content = $"{base_url}/v3/package/{id_lower}/{version_lower}/{id_lower}.{version_lower}.nupkg";
                        item.package_content = item.catalog_entry.package_content;
                    }
                }
            }
        }

        return registration;
    }

    public async Task<Stream?> GetPackageStreamAsync(string id, string version)
    {
        string object_name = $"v3/package/{id.ToLower()}/{version.ToLower()}/{id.ToLower()}.{version.ToLower()}.nupkg";
        try
        {
            MemoryStream ms = new MemoryStream();
            GetObjectArgs? get_args = new GetObjectArgs()
                                     .WithBucket(bucket_)
                                     .WithObject(object_name)
                                     .WithCallbackStream(s => s.CopyTo(ms));
            await minio_.GetObjectAsync(get_args);
            ms.Position = 0;
            return ms;
        }
        catch (Exception ex)
        {
            logger_.LogWarning(ex, "Package not found: {ObjectName}", object_name);
            return null;
        }
    }

    public async Task<Stream?> GetNuspecStreamAsync(string id, string version)
    {
        string object_name = $"v3/package/{id.ToLower()}/{version.ToLower()}/{id.ToLower()}.nuspec";
        try
        {
            MemoryStream ms = new MemoryStream();
            GetObjectArgs? get_args = new GetObjectArgs()
                                     .WithBucket(bucket_)
                                     .WithObject(object_name)
                                     .WithCallbackStream(s => s.CopyTo(ms));
            await minio_.GetObjectAsync(get_args);
            ms.Position = 0;
            return ms;
        }
        catch (Exception ex)
        {
            logger_.LogWarning(ex, "Nuspec not found: {ObjectName}", object_name);
            return null;
        }
    }

    public async Task<PackageVersionsResponse?> GetPackageVersionsAsync(string id)
    {
        string object_name = $"v3/package/{id.ToLower()}/index.json";
        return await GetJsonFromMinioAsync<PackageVersionsResponse>(object_name);
    }
}