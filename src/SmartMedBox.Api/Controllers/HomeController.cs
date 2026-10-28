using Microsoft.AspNetCore.Mvc;

namespace SmartMedBox.Api.Controllers;

public sealed class HomeController : Controller
{
    public IActionResult Index() => View();
}
