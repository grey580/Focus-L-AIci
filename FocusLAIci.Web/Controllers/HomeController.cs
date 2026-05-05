using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using FocusLAIci.Web.Models;
using FocusLAIci.Web.Security;
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
        if (!ModelState.IsValid)
        {
            return View(await RebuildDashboardAsync(input, cancellationToken));
        }

        return View(await _palaceService.GetDashboardAsync(RequestInputPolicy.NormalizeBoundContextBriefInput(input), cancellationToken));
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

    private async Task<DashboardViewModel> RebuildDashboardAsync(ContextBriefInput input, CancellationToken cancellationToken)
    {
        var dashboard = await _palaceService.GetDashboardAsync(cancellationToken);
        return new DashboardViewModel
        {
            Stats = dashboard.Stats,
            ContextInput = input,
            ContextPack = dashboard.ContextPack,
            QuickCaptureInput = dashboard.QuickCaptureInput,
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
        };
    }
}
