using FocusLAIci.Web.Models;
using FocusLAIci.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FocusLAIci.Web.Controllers;

public sealed class AdminController : Controller
{
    private readonly FocusDatabaseTargetService _databaseTargetService;
    private readonly SiteSettingsService _siteSettingsService;
    private readonly PalaceService _palaceService;

    public AdminController(
        SiteSettingsService siteSettingsService,
        FocusDatabaseTargetService databaseTargetService,
        PalaceService palaceService)
    {
        _siteSettingsService = siteSettingsService;
        _databaseTargetService = databaseTargetService;
        _palaceService = palaceService;
    }

    [HttpGet]
    public async Task<IActionResult> Settings(CancellationToken cancellationToken)
    {
        return View(await _siteSettingsService.BuildAdminSettingsAsync(cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Inspect([FromQuery] ContextBriefInput? input, CancellationToken cancellationToken)
    {
        var model = await _palaceService.GetInspectorAsync(
            input is not null && string.IsNullOrWhiteSpace(input.Question) && !input.IncludeCompletedWork && input.ExpandHistory && input.ResultsPerSection == 6
                ? null
                : input,
            cancellationToken);

        var diagnosticsUrl = "/api/palace/dashboard-diagnostics";
        var recentChangesUrl = "/api/palace/recent-changes";
        var diagnosticsQuery = new List<string>();
        if (!string.IsNullOrWhiteSpace(model.Diagnostics.ContextInput.Question))
        {
            var encodedQuestion = Uri.EscapeDataString(model.Diagnostics.ContextInput.Question);
            diagnosticsQuery.Add($"question={encodedQuestion}");
        }

        diagnosticsQuery.Add($"includeCompletedWork={model.Diagnostics.ContextInput.IncludeCompletedWork.ToString().ToLowerInvariant()}");
        diagnosticsQuery.Add($"expandHistory={model.Diagnostics.ContextInput.ExpandHistory.ToString().ToLowerInvariant()}");
        diagnosticsQuery.Add($"resultsPerSection={model.Diagnostics.ContextInput.ResultsPerSection}");
        diagnosticsUrl = $"{diagnosticsUrl}?{string.Join("&", diagnosticsQuery)}";

        var diagnosticsWithTarget = new DashboardDiagnosticsViewModel
        {
            GeneratedUtc = model.Diagnostics.GeneratedUtc,
            DatabaseTarget = _databaseTargetService.GetCurrentTarget(),
            Stats = model.Diagnostics.Stats,
            ContextInput = model.Diagnostics.ContextInput,
            ContextSummary = model.Diagnostics.ContextSummary,
            TopMatchCount = model.Diagnostics.TopMatchCount,
            DetectedGaps = model.Diagnostics.DetectedGaps,
            RecentChanges = model.Diagnostics.RecentChanges,
            Sections = model.Diagnostics.Sections
        };

        return View(new InspectorViewModel
        {
            Diagnostics = diagnosticsWithTarget,
            RecentChanges = model.RecentChanges,
            DiagnosticsApiUrl = diagnosticsUrl,
            RecentChangesApiUrl = recentChangesUrl
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(AdminSettingsInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(_siteSettingsService.Rebuild(input));
        }

        await _siteSettingsService.SaveAsync(input, cancellationToken);
        TempData["SettingsMessage"] = "Settings saved.";
        return RedirectToAction(nameof(Settings));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDatabaseTarget([Bind(Prefix = "DatabaseInput")] DatabaseTargetInput input, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _databaseTargetService.UpdateTargetAsync(input, cancellationToken);
            TempData["SettingsMessage"] = snapshot.UsesDefaultDatabase
                ? $"Switched Focus L-AIci back to the default database at {snapshot.DatabasePath}."
                : $"Switched Focus L-AIci to {snapshot.DatabasePath}.";
            return RedirectToAction(nameof(Settings));
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError("DatabaseInput.DatabasePath", ex.Message);
            var model = await _siteSettingsService.BuildAdminSettingsAsync(cancellationToken);
            var rebuilt = new AdminSettingsViewModel
            {
                Input = model.Input,
                DatabaseInput = input,
                TimeZoneOptions = model.TimeZoneOptions,
                ActiveTimeZoneLabel = model.ActiveTimeZoneLabel,
                DatabaseTarget = model.DatabaseTarget
            };
            return View("Settings", rebuilt);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CleanupConcurrentWings(CancellationToken cancellationToken)
    {
        var removedCount = await _siteSettingsService.CleanupConcurrentTestWingsAsync(cancellationToken);
        TempData["SettingsMessage"] = removedCount == 0
            ? "No stray Concurrent Wing test entries were found."
            : $"Removed {removedCount} stray Concurrent Wing test entr{(removedCount == 1 ? "y" : "ies")}.";
        return RedirectToAction(nameof(Settings));
    }
}
