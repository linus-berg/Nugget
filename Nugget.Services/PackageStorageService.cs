using System.Collections.Concurrent;
using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nugget.Services.Models;

namespace Nugget.Services;

public class PackageStorageService {
  private readonly ILogger<PackageStorageService> logger_;

  private readonly string package_storage_path_;

  // In-memory "database" of packages. Key is package ID (lowercase).
  private readonly ConcurrentDictionary<string, List<PackageMetadata>>
    packages_ = new();

  public PackageStorageService(IHostEnvironment env,
                               ILogger<PackageStorageService> logger) {
    package_storage_path_ = Path.Combine(env.ContentRootPath, "packages");
    Directory.CreateDirectory(package_storage_path_);
    logger_ = logger;
  }

  // --- Core Logic: Adding a Package ---

  public async Task AddPackageAsync(IFormFile file) {
    logger_.LogInformation("Attempting to add package...");

    // 1. Read package metadata from the .nuspec file
    PackageMetadata metadata;
    await using (Stream? stream = file.OpenReadStream())
    await using (ZipArchive zip = new(stream, ZipArchiveMode.Read)) {
      ZipArchiveEntry? nuspec_entry =
        zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".nuspec"));
      if (nuspec_entry == null) {
        throw new InvalidDataException("'.nuspec' file not found in package.");
      }

      await using (Stream nuspec_stream = nuspec_entry.Open()) {
        metadata = ParseNuspec(nuspec_stream);
      }
    }

    // 2. Save the .nupkg file to disk
    string package_dir = Path.Combine(
      package_storage_path_,
      metadata.id.ToLower()
    );
    Directory.CreateDirectory(package_dir);
    string version_dir = Path.Combine(package_dir, metadata.version.ToLower());
    Directory.CreateDirectory(version_dir);

    string file_path = Path.Combine(
      version_dir,
      $"{metadata.id.ToLower()}.{metadata.version.ToLower()}.nupkg"
    );
    await using (FileStream file_stream = new(file_path, FileMode.Create)) {
      await file.CopyToAsync(file_stream);
    }

    logger_.LogInformation($"Saved package to: {file_path}");

    // 3. Add metadata to our in-memory "database"
    packages_.AddOrUpdate(
      metadata.id.ToLower(),
      // New package
      key => new List<PackageMetadata> {
        metadata
      },
      // Existing package
      (key, existing_list) => {
        existing_list.Add(metadata);
        return existing_list;
      }
    );
  }

  private PackageMetadata ParseNuspec(Stream stream) {
    XDocument xml = XDocument.Load(stream);
    XNamespace ns = xml.Root.GetDefaultNamespace();
    XElement metadata_node =
      xml.Root.Elements().First(e => e.Name.LocalName == "metadata");

    return new PackageMetadata {
      id = metadata_node.Elements().First(e => e.Name.LocalName == "id").Value,
      version = metadata_node.Elements()
                             .First(e => e.Name.LocalName == "version")
                             .Value,
      description = metadata_node.Elements()
                                 .First(e => e.Name.LocalName == "description")
                                 .Value,
      authors =
        metadata_node.Elements()
                     .FirstOrDefault(e => e.Name.LocalName == "authors")
                     ?.Value ??
        "N/A"
    };
  }

  // --- Endpoint Helpers ---

  private string GetBaseUrl(HttpContext context) {
    return $"{context.Request.Scheme}://{context.Request.Host}";
  }

  public ServiceIndexResponse GetServiceIndex(HttpContext context) {
    string base_url = GetBaseUrl(context);
    return new ServiceIndexResponse {
      resources = new List<ServiceResource> {
        new() {
          type = "PackagePublish/2.0.0",
          id = $"{base_url}/v3/packages"
        },
        new() {
          type = "SearchQueryService/3.0.0-beta",
          id = $"{base_url}/v3/search"
        },
        new() {
          type = "RegistrationsBaseUrl/3.0.0-beta",
          id = $"{base_url}/v3/registration"
        },
        new() {
          type = "PackageBaseAddress/3.0.0",
          id = $"{base_url}/v3/package"
        }
      }
    };
  }

  public SearchResponse SearchPackages(string query, bool prerelease) {
    List<SearchHit> results = new();

    foreach (KeyValuePair<string, List<PackageMetadata>> package in packages_) {
      if (package.Key.Contains(query, StringComparison.OrdinalIgnoreCase)) {
        PackageMetadata latest_version = package.Value
                                                .OrderByDescending(
                                                  v => new Version(v.version)
                                                )
                                                .First();
        results.Add(
          new SearchHit {
            id = latest_version.id,
            version = latest_version.version,
            description = latest_version.description,
            authors = latest_version.authors.Split(','),
            versions = package.Value.Select(
                                v => new SearchVersion {
                                  version = v.version,
                                  id = ""
                                }
                              )
                              .ToList()
          }
        );
      }
    }

    return new SearchResponse {
      data = results,
      total_hits = results.Count
    };
  }

  public RegistrationIndexResponse? GetRegistration(
    string id, HttpContext context) {
    if (!packages_.TryGetValue(
          id.ToLower(),
          out List<PackageMetadata>? versions
        )) {
      return null;
    }

    string base_url = GetBaseUrl(context);

    RegistrationPage page = new() {
      lower = versions.Min(v => v.version) ?? "0.0.0",
      upper = versions.Max(v => v.version) ?? "0.0.0",
      count = versions.Count,
      items = versions.Select(
                        v => new RegistrationPageItem {
                          catalog_entry = new RegistrationCatalogEntry {
                            id = v.id,
                            version = v.version,
                            description = v.description,
                            package_content =
                              $"{base_url}/v3/package/{v.id.ToLower()}/{v.version.ToLower()}/{v.id.ToLower()}.{v.version.ToLower()}.nupkg"
                          }
                        }
                      )
                      .ToList()
    };

    return new RegistrationIndexResponse {
      count = 1,
      items = new List<RegistrationPage> {
        page
      }
    };
  }
}