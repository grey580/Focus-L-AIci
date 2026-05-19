using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using FocusLAIci.Web.Models;
using FocusLAIci.Web.Security;
using FocusLAIci.Web.Services;

namespace FocusLAIci.Web.Controllers;

public sealed class HomeController : Controller
{
    private readonly PalaceService _palaceService;
    private readonly ExternalSkillSuggestionService _externalSkillSuggestionService;

    public HomeController(PalaceService palaceService, ExternalSkillSuggestionService externalSkillSuggestionService)
    {
        _palaceService = palaceService;
        _externalSkillSuggestionService = externalSkillSuggestionService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        return View(ApplyMessages(await _palaceService.GetDashboardShellAsync(cancellationToken)));
    }

    [HttpGet]
    public async Task<IActionResult> UseOnDashboard([FromQuery] ContextBriefInput input, CancellationToken cancellationToken)
    {
        return View("Index", ApplyMessages(await RebuildDashboardShellAsync(input, cancellationToken)));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(ContextBriefInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(ApplyMessages(await RebuildDashboardAsync(input, cancellationToken)));
        }

        return View(ApplyMessages(await _palaceService.GetDashboardAsync(RequestInputPolicy.NormalizeBoundContextBriefInput(input), cancellationToken)));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickCapture(QuickCaptureInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            var dashboard = await _palaceService.GetDashboardAsync(cancellationToken);
            return View("Index", ApplyMessages(new DashboardViewModel
            {
                Stats = dashboard.Stats,
                ContextInput = dashboard.ContextInput,
                ContextPack = dashboard.ContextPack,
                FallbackContext = dashboard.FallbackContext,
                QuickCaptureInput = input,
                ActiveTickets = dashboard.ActiveTickets,
                RecentActivity = dashboard.RecentActivity,
                Wings = dashboard.Wings,
                RecentMemories = dashboard.RecentMemories,
                PinnedMemories = dashboard.PinnedMemories,
                ResurfacingMemories = dashboard.ResurfacingMemories,
                RecommendedAgents = dashboard.RecommendedAgents,
                FeaturedAgents = dashboard.FeaturedAgents,
                RecommendedSkills = dashboard.RecommendedSkills,
                FeaturedSkills = dashboard.FeaturedSkills,
                CurrentTodos = dashboard.CurrentTodos,
                MissingContextWarnings = dashboard.MissingContextWarnings,
                MissingContextWarningItems = dashboard.MissingContextWarningItems,
                SearchExamples = dashboard.SearchExamples
            }));
        }

        var id = await _palaceService.QuickCaptureAsync(input, cancellationToken);
        return RedirectToAction("Memory", "Palace", new { id });
    }

    [HttpGet]
    public async Task<IActionResult> DashboardPack([FromQuery] ContextBriefInput input, CancellationToken cancellationToken)
    {
        var normalizedInput = RequestInputPolicy.NormalizeBoundContextBriefInput(input);
        var panel = await _palaceService.BuildDashboardContextPanelAsync(normalizedInput, cancellationToken);
        if (panel is null)
        {
            return NoContent();
        }

        return PartialView("_DashboardContextPack", panel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportExternalSkill(
        string skillUrl,
        string sourceName,
        ContextBriefInput input,
        CancellationToken cancellationToken)
    {
        try
        {
            await _externalSkillSuggestionService.ImportSuggestionAsync(skillUrl, sourceName, cancellationToken);
            TempData["StatusMessage"] = "External skill imported and the pack was rebuilt.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return View("Index", ApplyMessages(await _palaceService.GetDashboardAsync(RequestInputPolicy.NormalizeBoundContextBriefInput(input), cancellationToken)));
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
            FallbackContext = dashboard.FallbackContext,
            QuickCaptureInput = dashboard.QuickCaptureInput,
            ActiveTickets = dashboard.ActiveTickets,
            RecentActivity = dashboard.RecentActivity,
            Wings = dashboard.Wings,
            RecentMemories = dashboard.RecentMemories,
            PinnedMemories = dashboard.PinnedMemories,
            ResurfacingMemories = dashboard.ResurfacingMemories,
            RecommendedAgents = dashboard.RecommendedAgents,
            FeaturedAgents = dashboard.FeaturedAgents,
            RecommendedSkills = dashboard.RecommendedSkills,
            FeaturedSkills = dashboard.FeaturedSkills,
            CurrentTodos = dashboard.CurrentTodos,
            MissingContextWarnings = dashboard.MissingContextWarnings,
            MissingContextWarningItems = dashboard.MissingContextWarningItems,
            SearchExamples = dashboard.SearchExamples
        };
    }

    private async Task<DashboardViewModel> RebuildDashboardShellAsync(ContextBriefInput input, CancellationToken cancellationToken)
    {
        var dashboard = await _palaceService.GetDashboardShellAsync(cancellationToken);
        return new DashboardViewModel
        {
            Stats = dashboard.Stats,
            ContextInput = RequestInputPolicy.NormalizeBoundContextBriefInput(input),
            ContextPack = dashboard.ContextPack,
            FallbackContext = dashboard.FallbackContext,
            QuickCaptureInput = dashboard.QuickCaptureInput,
            ActiveTickets = dashboard.ActiveTickets,
            RecentActivity = dashboard.RecentActivity,
            Wings = dashboard.Wings,
            RecentMemories = dashboard.RecentMemories,
            PinnedMemories = dashboard.PinnedMemories,
            ResurfacingMemories = dashboard.ResurfacingMemories,
            RecommendedAgents = dashboard.RecommendedAgents,
            FeaturedAgents = dashboard.FeaturedAgents,
            RecommendedSkills = dashboard.RecommendedSkills,
            FeaturedSkills = dashboard.FeaturedSkills,
            CurrentTodos = dashboard.CurrentTodos,
            MissingContextWarnings = dashboard.MissingContextWarnings,
            MissingContextWarningItems = dashboard.MissingContextWarningItems,
            SearchExamples = dashboard.SearchExamples
        };
    }

    private DashboardViewModel ApplyMessages(DashboardViewModel model)
    {
        return new DashboardViewModel
        {
            Stats = model.Stats,
            ContextInput = model.ContextInput,
            ContextPack = model.ContextPack,
            FallbackContext = model.FallbackContext,
            QuickCaptureInput = model.QuickCaptureInput,
            ActiveTickets = model.ActiveTickets,
            RecentActivity = model.RecentActivity,
            Wings = model.Wings,
            RecentMemories = model.RecentMemories,
            PinnedMemories = model.PinnedMemories,
            ResurfacingMemories = model.ResurfacingMemories,
            RecommendedAgents = model.RecommendedAgents,
            FeaturedAgents = model.FeaturedAgents,
            RecommendedSkills = model.RecommendedSkills,
            FeaturedSkills = model.FeaturedSkills,
            CurrentTodos = model.CurrentTodos,
            MissingContextWarnings = model.MissingContextWarnings,
            MissingContextWarningItems = model.MissingContextWarningItems,
            SearchExamples = model.SearchExamples,
            StatusMessage = TempData["StatusMessage"] as string,
            ErrorMessage = TempData["ErrorMessage"] as string
        };
    }
}
