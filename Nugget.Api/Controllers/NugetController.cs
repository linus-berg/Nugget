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
  public IResult GetRegistration(string id) {
    RegistrationIndexResponse? registration =
      package_storage_service_.GetRegistration(id, HttpContext);
    if (registration == null) {
      return Results.NotFound();
    }

    return Results.Ok(registration);
  }
}