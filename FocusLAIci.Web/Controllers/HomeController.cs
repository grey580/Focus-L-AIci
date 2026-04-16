using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using FocusLAIci.Web.Models;
using FocusLAIci.Web.Services;

namespace FocusLAIci.Web.Controllers;

public sealed class HomeController : Controller
{
    private readonly PalaceService _palaceService;

    public HomeController(PalaceService palaceService)
    {
        _palaceService = palaceService;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(await _palaceService.GetDashboardAsync(cancellationToken));
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
