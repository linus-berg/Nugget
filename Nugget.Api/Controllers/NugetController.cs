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
}