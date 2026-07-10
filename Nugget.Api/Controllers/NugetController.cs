using Microsoft.AspNetCore.Mvc;
using Nugget.Services;
using Nugget.Services.Models;

namespace Nugget.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class NugetController : Controller {
  private readonly ILogger<NugetController> logger_;
  private readonly PackageStorageService package_storage_service_;

  public NugetController(PackageStorageService package_storage_service,
                         ILogger<NugetController> logger) {
    logger_ = logger;
    package_storage_service_ = package_storage_service;
  }

  // Service Index — the entry point for the NuGet V3 protocol
  [HttpGet("/v3/index.json")]
  public IResult Index() {
    return Results.Ok(package_storage_service_.GetServiceIndex(HttpContext));
  }

  // Registration — package metadata for restore/install
  [HttpGet("/v3/registration/{id}/index.json")]
  public async Task<IResult> GetRegistration(string id) {
    RegistrationIndexResponse? registration =
      await package_storage_service_.GetRegistrationAsync(id, HttpContext);
    if (registration == null) {
      return Results.NotFound();
    }

    return Results.Ok(registration);
  }

  // Search — query packages by keyword
  [HttpGet("/v3/search")]
  public async Task<IResult> Search(
      [FromQuery] string? q,
      [FromQuery] int skip = 0,
      [FromQuery] int take = 20,
      [FromQuery] bool prerelease = false,
      [FromQuery] string? semVerLevel = "2.0.0") {
    return Results.Ok(await package_storage_service_.SearchPackagesAsync(
        q ?? "", skip, take, prerelease, semVerLevel ?? "2.0.0", HttpContext));
  }

  // Autocomplete — package ID completion (returns same as search for simplicity)
  [HttpGet("/v3/autocomplete")]
  public async Task<IResult> Autocomplete(
      [FromQuery] string? q,
      [FromQuery] int skip = 0,
      [FromQuery] int take = 20,
      [FromQuery] bool prerelease = false,
      [FromQuery] string? semVerLevel = "2.0.0") {
    SearchResponse searchResult = await package_storage_service_.SearchPackagesAsync(
                                    q ?? "", skip, take, prerelease, semVerLevel ?? "2.0.0", HttpContext);
    
    // Autocomplete returns package IDs, not full search results
    List<string> ids = searchResult.data.Select(h => h.id).ToList();
    return Results.Ok(new { totalHits = searchResult.total_hits, data = ids });
  }

  // Package Publish — push a .nupkg
  // dotnet nuget push sends the file as multipart/form-data.
  // The NuGet client does NOT use a well-known field name — it just sends
  // the file as the first (and only) part. We read it from Request.Form.Files.
  [HttpPut("/v3/packages")]
  [DisableRequestSizeLimit]
  [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
  public async Task<IResult> Push() {
    try {
      // Read the file from the multipart form data
      IFormFile? file = Request.Form.Files.FirstOrDefault();
      if (file == null || file.Length == 0) {
        return Results.BadRequest("No package file was uploaded.");
      }

      logger_.LogInformation("Received package upload: {FileName} ({Length} bytes)",
          file.FileName, file.Length);

      using Stream stream = file.OpenReadStream();
      await package_storage_service_.AddPackageAsync(stream, file.Length);
      return Results.StatusCode(201);
    } catch (InvalidOperationException ex) {
      // Package already exists — NuGet client treats 409 Conflict as "skip duplicate"
      logger_.LogWarning("Package already exists: {Message}", ex.Message);
      return Results.Conflict(ex.Message);
    } catch (Exception ex) {
      logger_.LogError(ex, "Failed to push package");
      return Results.BadRequest(ex.Message);
    }
  }

  // Flat Container — list versions for a package (PackageBaseAddress)
  [HttpGet("/v3/package/{id}/index.json")]
  public async Task<IResult> GetVersions(string id) {
    PackageVersionsResponse? versions = await package_storage_service_.GetPackageVersionsAsync(id);
    if (versions == null) {
      return Results.NotFound();
    }
    return Results.Ok(versions);
  }

  // Flat Container — download .nupkg or .nuspec
  // Streams the content directly instead of redirecting to a presigned MinIO URL,
  // which would expose internal infrastructure and may not be reachable by the client.
  [HttpGet("/v3/package/{id}/{version}/{filename}")]
  public async Task<IResult> Download(string id, string version, string filename) {
    if (filename.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)) {
      Stream? stream = await package_storage_service_.GetNuspecStreamAsync(id, version);
      if (stream == null) {
        return Results.NotFound();
      }
      return Results.File(stream, "text/xml", filename);
    }

    if (filename.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)) {
      Stream? stream = await package_storage_service_.GetPackageStreamAsync(id, version);
      if (stream == null) {
        return Results.NotFound();
      }
      return Results.File(stream, "application/octet-stream", filename);
    }

    return Results.NotFound();
  }
}