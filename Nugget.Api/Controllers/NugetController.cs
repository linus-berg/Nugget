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

  // GET
  [HttpGet("/v3/index.json")]
  public IResult Index() {
    return Results.Ok(package_storage_service_.GetServiceIndex(HttpContext));
  }

  [HttpGet("/v3/registration/{id}/index.json")]
  public async Task<IResult> GetRegistration(string id) {
    RegistrationIndexResponse? registration =
      await package_storage_service_.GetRegistrationAsync(id, HttpContext);
    if (registration == null) {
      return Results.NotFound();
    }

    return Results.Ok(registration);
  }

  [HttpGet("/v3/search")]
  public async Task<IResult> Search([FromQuery] string? q, [FromQuery] int skip = 0, [FromQuery] int take = 20, [FromQuery] bool prerelease = false, [FromQuery] string? semVerLevel = "1.0.0") {
    return Results.Ok(await package_storage_service_.SearchPackagesAsync(q ?? "", skip, take, prerelease, semVerLevel ?? "1.0.0", HttpContext));
  }

  [HttpPut("/v3/packages")]
  public async Task<IResult> Push(IFormFile package) {
    try {
      await package_storage_service_.AddPackageAsync(package);
      return Results.Created();
    } catch (Exception ex) {
      return Results.BadRequest(ex.Message);
    }
  }

  [HttpGet("/v3/package/{id}/index.json")]
  public async Task<IResult> GetVersions(string id) {
    PackageVersionsResponse? versions = await package_storage_service_.GetPackageVersionsAsync(id);
    if (versions == null) {
      return Results.NotFound();
    }
    return Results.Ok(versions);
  }

  [HttpGet("/v3/package/{id}/{version}/{filename}")]
  public async Task<IResult> Download(string id, string version, string filename) {
    if (filename.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)) {
      string nuspecUrl = await package_storage_service_.GetNuspecUrlAsync(id, version);
      return Results.Redirect(nuspecUrl);
    }

    string url = await package_storage_service_.GetDownloadUrlAsync(id, version);
    return Results.Redirect(url);
  }
}