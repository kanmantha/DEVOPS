using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using DevopsMvcApp.Models;

namespace DevopsMvcApp.Controllers;

/// <summary>Home controller — landing page, privacy policy, and error page.</summary>
public class HomeController : Controller
{
    /// <summary>Landing page.</summary>
    public IActionResult Index() => View();

    /// <summary>Privacy policy page.</summary>
    public IActionResult Privacy() => View();

    /// <summary>Error page — captures the current request ID for debugging.</summary>
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
