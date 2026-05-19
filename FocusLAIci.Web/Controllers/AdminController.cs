using FocusLAIci.Web.Models;
using FocusLAIci.Web.Security;
using FocusLAIci.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FocusLAIci.Web.Controllers;

public sealed class AdminController : Controller
{
    private readonly FocusDatabaseTargetService _databaseTargetService;
    private readonly SiteSettingsService _siteSettingsService;
    private readonly PalaceService _palaceService;
    private readonly FocusMcpSessionService _mcpSessionService;
    private readonly FocusMcpToolRegistry _mcpToolRegistry;
    private readonly FocusMcpResourceRegistry _mcpResourceRegistry;
    private readonly FocusMcpEventBus _mcpEventBus;
    private readonly FocusMcpAuthService _mcpAuthService;
    private readonly FocusDiagnosticsService _diagnosticsService;
    private readonly ExternalSkillSuggestionService _externalSkillSuggestionService;

    public AdminController(
        SiteSettingsService siteSettingsService,
        FocusDatabaseTargetService databaseTargetService,
        PalaceService palaceService,
        FocusMcpSessionService mcpSessionService,
        FocusMcpToolRegistry mcpToolRegistry,
        FocusMcpResourceRegistry mcpResourceRegistry,
        FocusMcpEventBus mcpEventBus,
        FocusMcpAuthService mcpAuthService,
        FocusDiagnosticsService diagnosticsService,
        ExternalSkillSuggestionService externalSkillSuggestionService)
    {
        _siteSettingsService = siteSettingsService;
        _databaseTargetService = databaseTargetService;
        _palaceService = palaceService;
        _mcpSessionService = mcpSessionService;
        _mcpToolRegistry = mcpToolRegistry;
        _mcpResourceRegistry = mcpResourceRegistry;
        _mcpEventBus = mcpEventBus;
        _mcpAuthService = mcpAuthService;
        _diagnosticsService = diagnosticsService;
        _externalSkillSuggestionService = externalSkillSuggestionService;
    }

    [HttpGet]
    public async Task<IActionResult> Settings(CancellationToken cancellationToken)
    {
        return View(await _siteSettingsService.BuildAdminSettingsAsync(cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Inspect(
        string? question,
        bool includeCompletedWork,
        bool expandHistory = true,
        int? resultsPerSection = null,
        CancellationToken cancellationToken = default)
    {
        if (!RequestInputPolicy.TryCreateOptionalContextBriefInput(
                question,
                includeCompletedWork,
                expandHistory,
                resultsPerSection,
                out var input,
                out var validationError))
        {
            return BadRequest(validationError);
        }

        var model = await _palaceService.GetInspectorAsync(
            input is not null && string.IsNullOrWhiteSpace(input.Question) && input.IncludeCompletedWork && input.ExpandHistory && input.ResultsPerSection == 6
                ? null
                : input,
            cancellationToken);
        var workspace = await _palaceService.GetWorkspaceExportAsync(cancellationToken);
        var tagGovernance = await _palaceService.GetTagGovernanceAsync(cancellationToken);
        var operatorDiagnostics = await _diagnosticsService.GetOperatorDiagnosticsAsync(_mcpAuthService.DescribeMode(), cancellationToken);

        var diagnosticsUrl = "/api/palace/dashboard-diagnostics";
        var recentChangesUrl = "/api/palace/recent-changes";
        var workspaceUrl = "/api/palace/workspace";
        var operatorDiagnosticsUrl = "/api/palace/operator-diagnostics";
        var selfTestUrl = "/api/palace/mcp-self-test";
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
            FallbackContext = model.Diagnostics.FallbackContext,
            ContextSummary = model.Diagnostics.ContextSummary,
            TopMatchCount = model.Diagnostics.TopMatchCount,
            DetectedGaps = model.Diagnostics.DetectedGaps,
            DetectedGapItems = model.Diagnostics.DetectedGapItems,
            RecentChanges = model.Diagnostics.RecentChanges,
            Sections = model.Diagnostics.Sections
        };

        return View(new InspectorViewModel
        {
            Diagnostics = diagnosticsWithTarget,
            RecentChanges = model.RecentChanges,
            DiagnosticsApiUrl = diagnosticsUrl,
            RecentChangesApiUrl = recentChangesUrl,
            WorkspaceApiUrl = workspaceUrl,
            OperatorDiagnosticsApiUrl = operatorDiagnosticsUrl,
            SelfTestApiUrl = selfTestUrl,
            WorkspaceExport = new WorkspaceExportViewModel
            {
                GeneratedUtc = workspace.GeneratedUtc,
                DatabaseTarget = _databaseTargetService.GetCurrentTarget(),
                Stats = workspace.Stats,
                ExportText = workspace.ExportText,
                PinnedMemories = workspace.PinnedMemories,
                ActiveTodos = workspace.ActiveTodos,
                ActiveTickets = workspace.ActiveTickets,
                CodeGraphProjects = workspace.CodeGraphProjects,
                RecentChanges = workspace.RecentChanges
            },
            TagGovernance = tagGovernance,
            OperatorDiagnostics = operatorDiagnostics
        });
    }

    [HttpGet]
    public async Task<IActionResult> McpConsole(CancellationToken cancellationToken)
    {
        return View(new FocusMcpConsoleViewModel
        {
            AuthMode = _mcpAuthService.DescribeMode(),
            MessageEndpointUrl = Url.Content("~/api/mcp/message"),
            ManifestEndpointUrl = Url.Content("~/api/mcp/manifest"),
            StreamEndpointTemplate = Url.Content("~/api/mcp/events/{sessionId}"),
            SelfTestApiUrl = Url.Content("~/api/palace/mcp-self-test"),
            SampleRequestJson =
                """
                {
                  "type": "initialize",
                  "payload": {
                    "clientName": "Focus admin console",
                    "clientVersion": "1.0"
                  }
                }
                """,
            Tools = _mcpToolRegistry.GetTools(),
            Resources = _mcpResourceRegistry.GetResources(),
            Sessions = _mcpSessionService.GetSessions(),
            RecentEvents = _mcpEventBus.GetRecentEvents(25),
            OperatorDiagnostics = await _diagnosticsService.GetOperatorDiagnosticsAsync(_mcpAuthService.DescribeMode(), cancellationToken)
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
                SkillSourceInput = model.SkillSourceInput,
                TimeZoneOptions = model.TimeZoneOptions,
                ApprovedDatabaseRootPaths = model.ApprovedDatabaseRootPaths,
                ActiveTimeZoneLabel = model.ActiveTimeZoneLabel,
                DatabaseTarget = model.DatabaseTarget,
                ExternalSkillSources = model.ExternalSkillSources
            };
            return View("Settings", rebuilt);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddSkillSource([Bind(Prefix = "SkillSourceInput")] SkillSourceEditorInput input, CancellationToken cancellationToken)
    {
        if (!TryValidateModel(input, nameof(AdminSettingsViewModel.SkillSourceInput)))
        {
            var model = await _siteSettingsService.BuildAdminSettingsAsync(cancellationToken);
            var rebuilt = new AdminSettingsViewModel
            {
                Input = model.Input,
                DatabaseInput = model.DatabaseInput,
                SkillSourceInput = input,
                TimeZoneOptions = model.TimeZoneOptions,
                ApprovedDatabaseRootPaths = model.ApprovedDatabaseRootPaths,
                ActiveTimeZoneLabel = model.ActiveTimeZoneLabel,
                DatabaseTarget = model.DatabaseTarget,
                ExternalSkillSources = model.ExternalSkillSources
            };
            return View("Settings", rebuilt);
        }

        try
        {
            await _externalSkillSuggestionService.AddSourceAsync(input, cancellationToken);
            TempData["SettingsMessage"] = "Skill source added.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["SettingsMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Settings));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveSkillSource(Guid id, CancellationToken cancellationToken)
    {
        await _externalSkillSuggestionService.RemoveSourceAsync(id, cancellationToken);
        TempData["SettingsMessage"] = "Skill source removed.";
        return RedirectToAction(nameof(Settings));
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkMemoryGovernance(MemoryBulkGovernanceInput input, CancellationToken cancellationToken)
    {
        try
        {
            await _palaceService.BulkUpdateMemoryGovernanceAsync(input, cancellationToken);
            TempData["SettingsMessage"] = "Memory governance updates applied.";
        }
        catch (InvalidOperationException ex)
        {
            TempData["SettingsMessage"] = ex.Message;
        }

        return RedirectToAction(nameof(Inspect));
    }
}
