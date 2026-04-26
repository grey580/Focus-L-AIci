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

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(await _palaceService.GetDashboardAsync(cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ContextBriefInput input, CancellationToken cancellationToken)
    {
        return View(await _palaceService.GetDashboardAsync(input, cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickCapture(QuickCaptureInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var dashboard = await _palaceService.GetDashboardAsync(cancellationToken);
            return View("Index", new DashboardViewModel
            {
                Stats = dashboard.Stats,
                ContextInput = dashboard.ContextInput,
                ContextPack = dashboard.ContextPack,
                QuickCaptureInput = input,
                ActiveTickets = dashboard.ActiveTickets,
                RecentActivity = dashboard.RecentActivity,
                Wings = dashboard.Wings,
                RecentMemories = dashboard.RecentMemories,
                PinnedMemories = dashboard.PinnedMemories,
                ResurfacingMemories = dashboard.ResurfacingMemories,
                CurrentTodos = dashboard.CurrentTodos,
                MissingContextWarnings = dashboard.MissingContextWarnings,
                MissingContextWarningItems = dashboard.MissingContextWarningItems,
                SearchExamples = dashboard.SearchExamples
            });
        }

        var id = await _palaceService.QuickCaptureAsync(input, cancellationToken);
        return RedirectToAction("Memory", "Palace", new { id });
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
