using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nugget.Services.DatabaseModels;
using Nugget.Services.Models;
using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using NuGet.Versioning; // Import the NuGet versioning library


namespace Nugget.Services;
public class PackageStorageService
{
    private readonly string package_storage_path_;
    private readonly ILogger<PackageStorageService> logger_;
    private readonly PackageDbContext context_; // Use the DbContext

    public PackageStorageService(
        IHostEnvironment env, 
        ILogger<PackageStorageService> logger, 
        PackageDbContext context) // Inject the DbContext
    {
        package_storage_path_ = Path.Combine(env.ContentRootPath, "packages");
        Directory.CreateDirectory(package_storage_path_);
        logger_ = logger;
        context_ = context; // Store the context
    }

    // --- Core Logic: Adding a Package ---

    public async Task AddPackageAsync(IFormFile file)
    {
        logger_.LogInformation("Attempting to add package...");

        // 1. Read package metadata from the .nuspec file
        PackageMetadata metadata;
        await using (Stream? stream = file.OpenReadStream())
        await using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Read))
        {
            ZipArchiveEntry? nuspec_entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".nuspec"));
            if (nuspec_entry == null)
            {
                throw new InvalidDataException("'.nuspec' file not found in package.");
            }

            await using (Stream nuspec_stream = nuspec_entry.Open())
            {
                metadata = ParseNuspec(nuspec_stream);
            }
        }
        
        // 2. Check if this version already exists
        string package_id_lower = metadata.id.ToLower();
        string version_lower = metadata.version.ToLower();
        
        bool exists = await context_.package_versions
                                    .AnyAsync(p => p.package_id == package_id_lower && p.version == version_lower);
            
        if (exists)
        {
            throw new InvalidOperationException($"Package '{package_id_lower}' version '{version_lower}' already exists.");
        }

        // 3. Save the .nupkg file to disk
        string package_dir = Path.Combine(package_storage_path_, package_id_lower);
        Directory.CreateDirectory(package_dir);
        string version_dir = Path.Combine(package_dir, version_lower);
        Directory.CreateDirectory(version_dir);

        string file_path = Path.Combine(version_dir, $"{package_id_lower}.{version_lower}.nupkg");
        await using (FileStream file_stream = new FileStream(file_path, FileMode.Create))
        {
            await file.CopyToAsync(file_stream);
        }
        logger_.LogInformation($"Saved package to: {file_path}");

        // 4. Add metadata to our database
        PackageVersion new_package_version = new PackageVersion
        {
            package_id = package_id_lower,
            version = version_lower,
            description = metadata.description,
            authors = metadata.authors,
            published = DateTime.UtcNow
        };
        
        context_.package_versions.Add(new_package_version);
        await context_.SaveChangesAsync();
    }

    private PackageMetadata ParseNuspec(Stream stream)
    {
        XDocument xml = XDocument.Load(stream);
        XNamespace ns = xml.Root.GetDefaultNamespace();
        XElement metadata_node = xml.Root.Elements().First(e => e.Name.LocalName == "metadata");

        return new PackageMetadata
        {
            id = metadata_node.Elements().First(e => e.Name.LocalName == "id").Value,
            version = metadata_node.Elements().First(e => e.Name.LocalName == "version").Value,
            description = metadata_node.Elements().First(e => e.Name.LocalName == "description").Value,
            authors = metadata_node.Elements().FirstOrDefault(e => e.Name.LocalName == "authors")?.Value ?? "N/A",
        };
    }

    // --- Endpoint Helpers ---

    private string GetBaseUrl(HttpContext context)
    {
        return $"{context.Request.Scheme}://{context.Request.Host}";
    }

    public ServiceIndexResponse GetServiceIndex(HttpContext context)
    {
        // This is static, no DB call needed.
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

    public async Task<SearchResponse> SearchPackagesAsync(string query, bool prerelease)
    {
        // Find all packages matching the query
        IQueryable<PackageVersion> db_query = context_.package_versions.AsNoTracking()
                                                      .Where(p => p.package_id.Contains(query));

        if (!prerelease)
        {
            // A simple check for prerelease. A more robust way would use NuGet.Versioning
            db_query = db_query.Where(p => !p.version.Contains("-"));
        }

        // Group by package ID
        List<IGrouping<string, PackageVersion>> package_groups = await db_query.GroupBy(p => p.package_id).ToListAsync();

        List<SearchHit> search_hits = new List<SearchHit>();
        foreach (IGrouping<string, PackageVersion> group in package_groups)
        {
            List<PackageVersion> versions = group.ToList();
            
            // Use NuGet.Versioning to find the latest version
            PackageVersion latest_version = versions
                                            .OrderByDescending(v => NuGetVersion.Parse(v.version))
                                            .First();

            search_hits.Add(new SearchHit
            {
                id = latest_version.package_id,
                version = latest_version.version,
                description = latest_version.description,
                authors = latest_version.authors.Split(','),
                versions = versions.Select(v => new SearchVersion { version = v.version, id = "" }).ToList()
            });
        }
        
        return new SearchResponse { data = search_hits, total_hits = search_hits.Count };
    }

    public async Task<RegistrationIndexResponse?> GetRegistrationAsync(string id, HttpContext context)
    {
        string package_id_lower = id.ToLower();
        List<PackageVersion> versions = await context_.package_versions.AsNoTracking()
                                                      .Where(p => p.package_id == package_id_lower)
                                                      .ToListAsync();

        if (!versions.Any())
        {
            return null;
        }

        string base_url = GetBaseUrl(context);

        // Use NuGet.Versioning to sort and find bounds
        var sorted_versions = versions
            .Select(v => new { Version = NuGetVersion.Parse(v.version), Data = v })
            .OrderBy(v => v.Version)
            .ToList();
        
        RegistrationPage page = new RegistrationPage
        {
            lower = sorted_versions.First().Version.ToNormalizedString(),
            upper = sorted_versions.Last().Version.ToNormalizedString(),
            count = versions.Count,
            items = sorted_versions.Select(v => new RegistrationPageItem
            {
                catalog_entry = new RegistrationCatalogEntry
                {
                    id = v.Data.package_id,
                    version = v.Data.version,
                    description = v.Data.description,
                    package_content = $"{base_url}/v3/package/{v.Data.package_id}/{v.Data.version}/{v.Data.package_id}.{v.Data.version}.nupkg"
                }
            }).ToList()
        };

        return new RegistrationIndexResponse
        {
            count = 1,
            items = new List<RegistrationPage> { page }
        };
    }
}
