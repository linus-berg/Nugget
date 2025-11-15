using Microsoft.AspNetCore.Mvc;

namespace Nugget.Api.Controllers;

public class NugetController : Controller
{
  // GET
  public IActionResult Index()
  {
    return View();
  }
}