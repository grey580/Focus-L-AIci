using System.Text.RegularExpressions;
using FocusLAIci.Web.Data;
using FocusLAIci.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace FocusLAIci.Web.Services;

public sealed partial class ContextService
{
    private static readonly HashSet<string> StopWords =
    [
        "about", "after", "all", "also", "and", "are", "around", "because", "been", "before", "being", "between",
        "build", "built", "can", "could", "did", "does", "doing", "done", "find", "for", "from", "get", "got",
        "had", "has", "have", "how", "into", "its", "just", "like", "look", "make", "more", "most", "need", "not",
        "our", "out", "over", "same", "should", "show", "that", "the", "their", "them", "then", "there", "these",
        "they", "this", "those", "through", "use", "using", "very", "want", "what", "when", "where", "which",
        "with", "would", "your"
    ];
    private static readonly HashSet<string> PreservedShortTokens = ["ad", "pc"];
    private static readonly Dictionary<string, string[]> SemanticAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bug"] = ["issue", "failure", "incident"],
        ["incident"] = ["alert", "issue", "failure"],
        ["deploy"] = ["deployment", "release", "ship", "install"],
        ["deployment"] = ["deploy", "release", "install", "rollout"],
        ["install"] = ["installer", "setup", "register", "deployment"],
        ["installer"] = ["install", "setup", "register"],
        ["register"] = ["registration", "token", "install"],
        ["token"] = ["credential", "auth", "registration"],
        ["auth"] = ["authentication", "authorization", "login", "token"],
        ["login"] = ["signin", "auth", "credential"],
        ["context"] = ["memory", "workspace", "pack"],
        ["memory"] = ["context", "note", "knowledge"],
        ["research"] = ["investigation", "analysis", "study"],
        ["architecture"] = ["design", "system", "structure"],
        ["delivery"] = ["release", "ship", "handoff"],
        ["debug"] = ["troubleshoot", "diagnose", "fix"],
        ["code"] = ["symbol", "file", "project", "implementation"],
        ["snare"] = ["canary"],
        ["canary"] = ["snare"],
        ["platform"] = ["product", "app", "system"],
        ["product"] = ["platform", "app", "solution"],
        ["blueprint"] = ["architecture", "design", "topology"],
        ["topology"] = ["architecture", "structure", "blueprint"],
        ["screen"] = ["view", "page", "ui", "form"],
        ["view"] = ["screen", "page", "ui"],
        ["customer"] = ["client", "account", "company"],
        ["client"] = ["customer", "account", "company"],
        ["headquarters"] = ["address", "location", "office"],
        ["address"] = ["location", "headquarters", "site"],
        ["autofill"] = ["copy", "populate", "automatic"],
        ["copy"] = ["clone", "populate", "autofill"],
        ["workspace"] = ["context", "project", "repo"]
    };
    private static readonly HashSet<string> ExternalOpsTokens =
    [
        "powershell", "script", "active", "directory", "emails", "email", "mail", "mailboxes", "users", "user", "ldap", "ad"
    ];
    private static readonly HashSet<string> CodeIntentTokens =
    [
        "code", "repo", "project", "file", "files", "symbol", "symbols", "class", "method", "controller", "service", "implementation", "source"
    ];
    private static readonly HashSet<string> StrongCodeIntentTokens =
    [
        "code", "repo", "repository", "project", "symbol", "symbols", "class", "method", "controller", "implementation", "codebase", "namespace"
    ];
    private static readonly HashSet<string> DirectoryDomainTokens =
    [
        "active", "directory", "ldap", "entra", "graph", "mail", "email", "emails", "mailbox", "mailboxes", "domain", "ad", "forest", "dns", "forwarder", "exchange", "o365", "m365", "proxyaddresses", "upn", "userprincipalname", "mailnickname"
    ];
    private static readonly HashSet<string> DirectoryAdminTokens =
    [
        "directory", "ldap", "entra", "graph", "mail", "email", "emails", "mailbox", "mailboxes", "domain", "ad", "exchange", "o365", "m365", "proxyaddresses", "upn", "userprincipalname", "mailnickname", "attribute", "attributes", "password", "passwords", "expiry", "expiring", "expires", "expired"
    ];
    private static readonly HashSet<string> DirectoryAdminHighSignalTokens =
    [
        "ldap", "entra", "graph", "mail", "email", "emails", "mailbox", "mailboxes", "domain", "forest", "dns", "forwarder", "exchange", "o365", "m365", "proxyaddresses", "upn", "userprincipalname", "mailnickname", "password", "passwords", "expiry", "expiring", "expires", "expired"
    ];
    private static readonly HashSet<string> DirectoryAdminAttributeTokens =
    [
        "email", "emails", "mail", "mailbox", "mailboxes", "proxyaddresses", "attribute", "attributes", "upn", "userprincipalname", "mailnickname", "title", "department", "phone", "telephone", "password", "passwords", "expiry", "expiring", "expires", "expired"
    ];
    private static readonly HashSet<string> DirectoryAdminExactAttributeTokens =
    [
        "title", "department", "phone", "telephone", "proxyaddresses", "upn", "userprincipalname", "mailnickname", "password", "passwords", "expiry", "expiring", "expires", "expired"
    ];
    private static readonly string[] DirectoryAdminAttributePhrases =
    [
        "proxy addresses", "user principal name", "mail nickname", "password expiry", "password expires", "password expiring"
    ];
    private static readonly HashSet<string> DirectoryAdminBroadInfraTokens =
    [
        "migration", "migrate", "admt", "immutable", "consistencyguid", "forwarder", "forest", "dns", "sidhistory"
    ];
    private static readonly HashSet<string> DirectoryAdminAuditActionTokens =
    [
        "audit", "check", "report", "reports", "find", "verify", "export", "query", "list", "missing", "blank"
    ];
    private static readonly HashSet<string> TicketingTokens =
    [
        "ticket", "tickets", "task", "tasks", "subticket", "subtickets", "completed", "backlog"
    ];
    private static readonly HashSet<string> GenericAutomationTokens =
    [
        "powershell", "script", "export", "csv", "disabled", "audit", "report", "query", "list", "automation"
    ];
    private static readonly HashSet<string> GenericAutomationHighSignalTokens =
    [
        "export", "csv", "disabled", "audit", "report", "query", "list", "automation", "inactive", "users", "user",
        "compare", "diff", "difference", "differences", "folder", "folders", "file", "files", "directory", "directories",
        "port", "ports", "tcp", "udp", "listener", "listeners", "socket", "sockets"
    ];
    private static readonly HashSet<string> LocalSupportTokens =
    [
        "windows", "pc", "network", "wifi", "slow", "performance", "troubleshoot", "troubleshooting", "latency", "local",
        "wmi", "cim", "winmgmt", "rpc", "computer", "computers", "winrm", "port", "ports", "tcp", "udp"
    ];
    private static readonly HashSet<string> LocalSupportHighSignalTokens =
    [
        "network", "wifi", "slow", "performance", "troubleshoot", "troubleshooting", "latency",
        "wmi", "cim", "winmgmt", "rpc", "computer", "computers", "winrm", "port", "ports", "tcp", "udp"
    ];
    private static readonly HashSet<string> RetrievalLowSignalTokens =
    [
        "build", "check", "checks", "command", "commands", "computer", "computers", "create", "find", "help", "line",
        "list", "local", "machine", "machines", "make", "need", "pc", "pcs", "please", "powershell", "run", "script",
        "show", "tell", "use", "using", "windows", "will", "with"
    ];
    private static readonly HashSet<string> WebUiTokens =
    [
        "website", "web", "ui", "layout", "homepage", "spacing", "css", "frontend", "design"
    ];
    private static readonly HashSet<string> WebUiHighSignalTokens =
    [
        "website", "web", "ui", "layout", "homepage", "spacing", "css", "frontend"
    ];
    private static readonly HashSet<string> ProductUiChangeTokens =
    [
        "page", "pages", "view", "views", "form", "forms", "field", "fields", "checkbox", "dropdown", "button", "buttons",
        "slider", "panel", "address", "location", "locations", "company", "client", "clients", "region", "country", "city", "state"
    ];
    private static readonly HashSet<string> StaticSiteTokens =
    [
        "homepage", "landing", "marketing", "css", "javascript", "js", "asset", "assets", "static", "site"
    ];
    private static readonly HashSet<string> CloudOpsTokens =
    [
        "azure", "cloud", "deployment", "entra", "identity", "tenant", "subscription", "app", "insights", "appinsights"
    ];
    private static readonly HashSet<string> CloudOpsHighSignalTokens =
    [
        "azure", "cloud", "deployment", "entra", "identity", "tenant", "subscription", "insights", "appinsights", "microsoft", "graph", "oauth", "mailbox"
    ];
    private static readonly HashSet<string> CloudOpsSkillTokens =
    [
        "azure", "cloud", "deployment", "entra", "identity", "tenant", "subscription", "insights", "appinsights", "telemetry", "microsoft", "graph", "mailbox", "oauth"
    ];
    private static readonly HashSet<string> DesktopAppTokens =
    [
        "desktop", "winforms", "windowsforms", "forms", "windows", "app"
    ];
    private static readonly HashSet<string> DesktopAppSkillTokens =
    [
        "desktop", "winforms", "windowsforms", "forms", "dotnet", "csharp"
    ];
    private static readonly string[] DesktopAppPhrases =
    [
        "windows forms", "desktop app"
    ];
    private static readonly HashSet<string> RepositoryArchitectureTokens =
    [
        "repo", "repository", "codebase", "architecture", "refactor", "map", "structure", "design", "system", "module", "component", "onboarding"
    ];
    private static readonly string[] RepositoryArchitecturePhrases =
    [
        "service boundaries", "module layout", "codebase structure", "system architecture", "architecture overview"
    ];
    private static readonly HashSet<string> CodeMemoryTokens =
    [
        "code", "repo", "repository", "project", "file", "files", "class", "method", "controller", "service", "implementation", "source", "startup", "runtime", "path", "ranking"
    ];

    private readonly FocusMemoryContext _dbContext;
    private readonly CurrentProjectContext? _currentProjectContext;
    private readonly IPackIntentModel _packIntentModel;
    private readonly IPackDecisionEngine _packDecisionEngine;
    private readonly IPackCriticEngine _packCriticEngine;
    private readonly PackBuildArchiveService? _packBuildArchiveService;
    private readonly IContextEmbeddingService? _contextEmbeddingService;

    public ContextService(
        FocusMemoryContext dbContext,
        IHostEnvironment? hostEnvironment = null,
        IPackIntentModel? packIntentModel = null,
        IPackDecisionEngine? packDecisionEngine = null,
        IPackCriticEngine? packCriticEngine = null,
        PackBuildArchiveService? packBuildArchiveService = null,
        IContextEmbeddingService? contextEmbeddingService = null)
    {
        _dbContext = dbContext;
        _currentProjectContext = ResolveCurrentProjectContext(hostEnvironment?.ContentRootPath);
        _packIntentModel = packIntentModel ?? TinyLocalPackIntentModel.Shared;
        _packDecisionEngine = packDecisionEngine ?? new PackDecisionEngine();
        _packCriticEngine = packCriticEngine ?? new PackCriticEngine();
        _packBuildArchiveService = packBuildArchiveService;
        _contextEmbeddingService = contextEmbeddingService;
    }

    public async Task<ContextPackViewModel?> BuildContextPackAsync(string? question, CancellationToken cancellationToken)
        => await BuildContextPackAsync(new ContextBriefInput
        {
            Question = question?.Trim() ?? string.Empty
        }, cancellationToken);

    public async Task<ContextPackViewModel?> BuildContextPackAsync(ContextBriefInput? input, CancellationToken cancellationToken)
    {
        var effectiveInput = input ?? new ContextBriefInput();
        var normalizedQuestion = effectiveInput.Question?.Trim() ?? string.Empty;
        var normalizedQuestionPhrase = NormalizePhrase(normalizedQuestion);
        var tokens = Tokenize(normalizedQuestion);
        var semanticTokens = ExpandSemanticTokens(tokens);
        var semanticProfile = BuildSemanticQueryProfile(normalizedQuestion, tokens, semanticTokens);
        var intentPrediction = _packIntentModel.Predict(normalizedQuestion);
        var preferDurableMemoryLead = ShouldPreferDurableMemoryLead(normalizedQuestion, effectiveInput);
        var fileComparisonQuery = intentPrediction.IsFileComparisonQuery;
        var codeFamilyPreferred = intentPrediction.CodeFamilyScore >= intentPrediction.OperationsFamilyScore + 0.08m;
        var operationsFamilyPreferred = intentPrediction.OperationsFamilyScore >= intentPrediction.CodeFamilyScore + 0.08m;
        var externalAdminQuery = intentPrediction.IsExternalOperationsQuery
                                 || (!codeFamilyPreferred
                                     && intentPrediction.ExternalOperationsScore >= 0.35m
                                     && IsExternalOperationsQuery(tokens));
        var directoryAdminQuery = intentPrediction.IsDirectoryAdminQuery
                                  || (!codeFamilyPreferred
                                      && intentPrediction.DirectoryAdminScore >= 0.35m
                                      && IsDirectoryAdminQuery(tokens));
        var explicitCodeQuery = intentPrediction.HasExplicitCodeIntent
                                || (!operationsFamilyPreferred
                                    && intentPrediction.CodeIntentScore >= 0.38m
                                    && HasExplicitCodeIntent(tokens));
        var repositoryArchitectureQuery = intentPrediction.IsRepositoryArchitectureQuery
                                          || (!operationsFamilyPreferred
                                              && intentPrediction.RepositoryArchitectureScore >= 0.40m
                                              && IsRepositoryArchitectureQuery(tokens, normalizedQuestionPhrase));
        var currentProjectHint = HasCurrentProjectHint(tokens);
        if (fileComparisonQuery && !currentProjectHint)
        {
            explicitCodeQuery = false;
        }

        var wmiDiagnosticQuery = intentPrediction.IsWmiDiagnosticQuery;
        var portCheckQuery = intentPrediction.IsPortCheckQuery;
        var softwareInstallQuery = intentPrediction.IsSoftwareInstallQuery;
        var windowsServicingQuery = intentPrediction.IsWindowsServicingQuery;
        var windowsUpdateQuery = intentPrediction.IsWindowsUpdateQuery;
        var localSupportQuery = !softwareInstallQuery && !windowsServicingQuery && !windowsUpdateQuery && (wmiDiagnosticQuery || portCheckQuery || IsLocalSupportQuery(tokens));
        var genericAutomationQuery = intentPrediction.IsGenericAutomationQuery && !windowsServicingQuery && !windowsUpdateQuery && !directoryAdminQuery && !explicitCodeQuery && !localSupportQuery;
        var currentProjectCodeQuery = explicitCodeQuery && currentProjectHint;
        var projectHistoryQuery = intentPrediction.IsProjectHistoryQuery && !directoryAdminQuery && !genericAutomationQuery && !fileComparisonQuery;
        if (projectHistoryQuery)
        {
            preferDurableMemoryLead = true;
        }

        var projectCatalog = await _dbContext.CodeGraphProjects
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var hasNamedProjectReference = projectCatalog.Any(project => BuildProjectQueryBoost(project, tokens, semanticTokens, normalizedQuestion) >= 5m);
        var productUiChangeQuery = hasNamedProjectReference && IsProductUiChangeQuery(tokens, normalizedQuestionPhrase);

        var webUiQuery = IsWebUiQuery(tokens);
        var cloudOpsQuery = IsCloudOpsQuery(tokens);
        var desktopAppQuery = IsDesktopAppQuery(tokens, normalizedQuestionPhrase);
        var hasStrongDomainSignals = HasStrongPackDomainSignals(
            tokens,
            normalizedQuestionPhrase,
            repositoryArchitectureQuery,
            fileComparisonQuery,
                                    projectHistoryQuery,
                                    intentPrediction.IsPasswordExpiryQuery,
                                    softwareInstallQuery,
                                    windowsServicingQuery,
                                    windowsUpdateQuery,
                                    localSupportQuery,
                                    wmiDiagnosticQuery,
                                    portCheckQuery,
            webUiQuery,
            cloudOpsQuery,
            desktopAppQuery,
            currentProjectHint);
        var queryDecision = _packDecisionEngine.EvaluateQuery(intentPrediction, tokens.Count > 0, hasStrongDomainSignals);
        var suppressCodeGraph = (externalAdminQuery && !explicitCodeQuery)
                                || fileComparisonQuery
                                || genericAutomationQuery
                                || windowsServicingQuery
                                || windowsUpdateQuery
                                || localSupportQuery
                                || (cloudOpsQuery && !explicitCodeQuery)
                                || ((webUiQuery || desktopAppQuery) && !currentProjectCodeQuery && !repositoryArchitectureQuery && !hasNamedProjectReference);
        var allowSemanticHybrid = ShouldEnableSemanticHybrid(
            normalizedQuestion,
            fileComparisonQuery,
            projectHistoryQuery,
            externalAdminQuery && !explicitCodeQuery,
            directoryAdminQuery && !explicitCodeQuery,
            wmiDiagnosticQuery,
            portCheckQuery,
            softwareInstallQuery,
            windowsServicingQuery,
            windowsUpdateQuery,
            genericAutomationQuery);
        if (tokens.Count == 0)
        {
            return null;
        }

        var resultsPerSection = Math.Clamp(effectiveInput.ResultsPerSection, 3, 10);
        if (!queryDecision.ShouldProceed)
        {
            return await BuildDecisionPackAsync(
                queryDecision,
                normalizedQuestion,
                effectiveInput,
                resultsPerSection,
                tokens,
                normalizedQuestionPhrase,
                projectHistoryQuery,
                explicitCodeQuery,
                fileComparisonQuery,
                intentPrediction.IsPasswordExpiryQuery,
                softwareInstallQuery,
                windowsServicingQuery,
                windowsUpdateQuery,
                intentPrediction.IsGenericAutomationQuery,
                localSupportQuery,
                wmiDiagnosticQuery,
                portCheckQuery,
                webUiQuery,
                cloudOpsQuery,
                desktopAppQuery,
                cancellationToken);
        }

        var memories = await _dbContext.Memories
            .AsNoTracking()
            .Include(x => x.Wing)
            .Include(x => x.Room)
            .Include(x => x.MemoryTags)
                .ThenInclude(x => x.Tag)
            .ToListAsync(cancellationToken);

        if (!effectiveInput.IncludeRetired)
        {
            memories = memories.Where(x => x.LifecycleState == MemoryLifecycleState.Active).ToList();
        }

        if (effectiveInput.WingId.HasValue)
        {
            memories = memories.Where(x => x.WingId == effectiveInput.WingId.Value).ToList();
        }

        if (effectiveInput.RoomId.HasValue)
        {
            memories = memories.Where(x => x.RoomId == effectiveInput.RoomId.Value).ToList();
        }

        if (effectiveInput.Kind.HasValue)
        {
            memories = memories.Where(x => x.Kind == effectiveInput.Kind.Value).ToList();
        }

        if (!string.IsNullOrWhiteSpace(effectiveInput.Tag))
        {
            var tagSlug = SlugUtility.CreateSlug(effectiveInput.Tag);
            var tagMatchedMemories = memories.Where(x => x.MemoryTags.Any(tag => tag.Tag!.Slug == tagSlug)).ToList();
            if (tagMatchedMemories.Count > 0)
            {
                memories = tagMatchedMemories;
            }
        }

        var todosQuery = _dbContext.Todos
            .AsNoTracking()
            .AsQueryable();
        if (!effectiveInput.IncludeCompletedWork)
        {
            todosQuery = todosQuery.Where(x => x.Status != TodoStatus.Done);
        }

        var todos = await todosQuery.ToListAsync(cancellationToken);

        var ticketsQuery = _dbContext.Tickets
            .AsNoTracking()
            .AsQueryable();
        if (!effectiveInput.IncludeCompletedWork)
        {
            ticketsQuery = ticketsQuery.Where(x => x.Status != TicketStatus.Completed);
        }

        var tickets = await ticketsQuery.ToListAsync(cancellationToken);

        List<CodeGraphProject> projects = [];
        List<CodeGraphFile> files = [];

        Dictionary<Guid, string> ticketNotes = [];
        Dictionary<Guid, string> ticketActivities = [];
        Dictionary<Guid, string> ticketTimeLogs = [];
        Dictionary<Guid, string> subTicketTexts = [];
        if (effectiveInput.ExpandHistory)
        {
            ticketNotes = await _dbContext.TicketNotes
                .AsNoTracking()
                .GroupBy(x => x.TicketId)
                .ToDictionaryAsync(
                    x => x.Key,
                    x => string.Join(' ', x.OrderByDescending(item => item.UpdatedUtc).Select(item => item.Content)),
                    cancellationToken);

            ticketActivities = await _dbContext.TicketActivities
                .AsNoTracking()
                .GroupBy(x => x.TicketId)
                .ToDictionaryAsync(
                    x => x.Key,
                    x => string.Join(' ', x.OrderByDescending(item => item.CreatedUtc).Select(item => $"{item.ActivityType} {item.Message} {item.Metadata}")),
                    cancellationToken);

            ticketTimeLogs = await _dbContext.TicketTimeLogs
                .AsNoTracking()
                .GroupBy(x => x.TicketId)
                .ToDictionaryAsync(
                    x => x.Key,
                    x => string.Join(' ', x.OrderByDescending(item => item.LoggedUtc).Select(item => $"{item.ModelName} {item.Summary} {item.MinutesSpent} minutes")),
                    cancellationToken);

            subTicketTexts = await _dbContext.Tickets
                .AsNoTracking()
                .Where(x => x.ParentTicketId != null)
                .GroupBy(x => x.ParentTicketId!.Value)
                .ToDictionaryAsync(
                    x => x.Key,
                    x => string.Join(' ', x.Select(item => $"{item.Title} {item.Description} {item.GitBranch}")),
                    cancellationToken);
        }

        Dictionary<Guid, int> nodeDegrees = [];
        List<CodeGraphNode> nodes = [];
        if (!suppressCodeGraph)
        {
            projects = projectCatalog;
            files = await _dbContext.CodeGraphFiles
                .AsNoTracking()
                .Include(x => x.Project)
                .ToListAsync(cancellationToken);

            var edgeNodeIds = await _dbContext.CodeGraphEdges
                .AsNoTracking()
                .Select(x => x.FromNodeId)
                .Concat(_dbContext.CodeGraphEdges.AsNoTracking().Select(x => x.ToNodeId))
                .ToListAsync(cancellationToken);

            nodeDegrees = edgeNodeIds
                .GroupBy(x => x)
                .ToDictionary(x => x.Key, x => x.Count());

            nodes = await _dbContext.CodeGraphNodes
                .AsNoTracking()
                .Include(x => x.File)
                .ToListAsync(cancellationToken);
        }

        var recentMemoryIds = memories
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(12)
            .Select(x => x.Id)
            .ToHashSet();
        var recentTicketIds = tickets
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(12)
            .Select(x => x.Id)
            .ToHashSet();
        var recentProjectIds = projects
            .OrderByDescending(x => x.LastScannedUtc ?? x.UpdatedUtc)
            .Take(8)
            .Select(x => x.Id)
            .ToHashSet();
        var recentFileIds = files
            .OrderByDescending(x => x.ScannedUtc)
            .Take(12)
            .Select(x => x.Id)
            .ToHashSet();
        var recentNodeIds = nodes
            .Where(x => x.FileId.HasValue && recentFileIds.Contains(x.FileId.Value))
            .Select(x => x.Id)
            .ToHashSet();
        var skills = await _dbContext.Skills
            .AsNoTracking()
            .Include(x => x.Wing)
            .ToListAsync(cancellationToken);
        var projectPreferences = suppressCodeGraph
            ? new Dictionary<Guid, ProjectPreference>()
            : projects.ToDictionary(
                project => project.Id,
                project => BuildProjectPreference(project, tokens, semanticTokens, normalizedQuestion));
        var projectHistoryFocusProject = suppressCodeGraph || !projectHistoryQuery
            ? null
            : projects
                .Where(project => projectPreferences[project.Id].ProjectQueryBoost > 0m || projectPreferences[project.Id].HasStrongAffinity)
                .OrderByDescending(project => projectPreferences[project.Id].ProjectQueryBoost)
                .ThenByDescending(project => projectPreferences[project.Id].RepoAffinityBoost)
                .FirstOrDefault();
        var projectHistoryFocusTokens = projectHistoryFocusProject is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : Tokenize($"{projectHistoryFocusProject.Name} {Path.GetFileName(projectHistoryFocusProject.RootPath)}");
        var externalOpsQuery = IsExternalOperationsQuery(tokens);

        var memoryResults = memories
            .Select(memory =>
            {
                var trust = MemoryTrustHelper.Build(memory);
                var score = ScoreFields(
                    normalizedQuestion,
                    tokens,
                    semanticTokens,
                    semanticProfile,
                    BuildMemorySemanticPolicy(effectiveInput.PackGoal, preferDurableMemoryLead, repositoryArchitectureQuery, projectHistoryQuery),
                    MemoryTrustHelper.GetEffectiveTimestamp(memory.UpdatedUtc, memory.LastVerifiedUtc),
                    new WeightedField(memory.Title, "title", "Title", 20m, "Title matches your question closely.", "Title shares your search terms."),
                    new WeightedField(memory.Summary, "summary", "Summary", 12m, "Summary closely matches the request.", "Summary reinforces the match."),
                    new WeightedField(TrimPreview(memory.Content, 2000), "content", "Content", 7m, "Memory content contains the full request.", "Memory content covers the same terms."),
                    new WeightedField($"{memory.Wing?.Name} {memory.Room?.Name}", "location", "Wing/room", 8m, "Wing or room naming matches the request.", "Wing or room naming overlaps the request."),
                    new WeightedField(string.Join(' ', memory.MemoryTags.Select(x => x.Tag!.Name)), "tags", "Tags", 10m, "Tags line up with the request.", "Tags overlap the request."));

                score = ApplyBoost(score, memory.IsPinned ? 4m : 0m, "Pinned memory");
                score = ApplyBoost(score, Math.Max(memory.Importance - 3, 0), "High importance");
                score = ApplyBoost(score, trust.RetrievalAdjustment, trust.RetrievalAdjustmentLabel);
                score = ApplyBoost(score, GoalBoost(effectiveInput.PackGoal, ContextRecordKind.Memory, memory.SourceKind, memory.Kind), GoalBoostLabel(effectiveInput.PackGoal, ContextRecordKind.Memory));
                score = ApplyBoost(score, BuildScopedMemoryBoost(memory, effectiveInput), BuildScopedMemoryBoostLabel(effectiveInput));
                score = ApplyBoost(score, effectiveInput.PreferRecentChanges && recentMemoryIds.Contains(memory.Id) ? 2m : 0m, "Recent change");
                score = ApplyBoost(score, projectHistoryQuery && recentMemoryIds.Contains(memory.Id) ? 4m : 0m, projectHistoryQuery ? "Recent project history" : string.Empty);
                score = ApplyBoost(score, preferDurableMemoryLead ? DurableMemoryQuestionBoost(memory) : 0m, "Durable memory lead");
                score = ApplyBoost(score, externalAdminQuery ? BuildExternalAdminMemoryBoost(memory) : 0m, externalAdminQuery ? "Directory admin context" : string.Empty);
                score = ApplyBoost(
                    score,
                    directoryAdminQuery ? BuildDirectoryAdminMismatchPenalty(memory) : 0m,
                    directoryAdminQuery ? "Directory domain mismatch" : string.Empty);

                return new { Memory = memory, Match = score, Trust = trust };
            })
            .Where(x => x.Match.Score > 0)
            .Where(x => !directoryAdminQuery || explicitCodeQuery || IsDirectoryAdminRelevantMemory(x.Memory, tokens, normalizedQuestionPhrase))
            .Where(x => !directoryAdminQuery || explicitCodeQuery || x.Match.Score >= 20m)
            .Where(x => !fileComparisonQuery || IsFileComparisonRelevantMemory(x.Memory))
            .Where(x => !projectHistoryQuery || projectHistoryFocusTokens.Count == 0 || IsProjectHistoryRelevantMemory(x.Memory, projectHistoryFocusTokens))
            .Where(x => !wmiDiagnosticQuery || IsWmiDiagnosticRelevantMemory(x.Memory))
            .Where(x => !portCheckQuery || IsPortCheckRelevantMemory(x.Memory))
            .Where(x => !softwareInstallQuery || IsSoftwareInstallRelevantMemory(x.Memory))
            .Where(x => !windowsServicingQuery || IsWindowsServicingRelevantMemory(x.Memory))
            .Where(x => !windowsUpdateQuery || IsWindowsUpdateRelevantMemory(x.Memory))
            .Where(x => !genericAutomationQuery || IsGenericAutomationRelevantMemory(x.Memory))
            .Where(x => !localSupportQuery || IsLocalSupportRelevantMemory(x.Memory))
            .Where(x => !webUiQuery || currentProjectCodeQuery || IsWebUiRelevantMemory(x.Memory))
            .Where(x => !cloudOpsQuery || IsCloudOpsRelevantMemory(x.Memory))
            .Where(x => !desktopAppQuery || currentProjectCodeQuery || IsDesktopAppRelevantMemory(x.Memory))
            .Where(x => !repositoryArchitectureQuery || IsRepositoryArchitectureRelevantMemory(x.Memory) || IsCurrentProjectRelevantMemory(x.Memory))
            .Where(x => !currentProjectCodeQuery || IsCurrentProjectRelevantMemory(x.Memory) || IsCodeRelevantMemory(x.Memory))
            .OrderByDescending(x => x.Match.Score)
            .ThenByDescending(x => x.Memory.UpdatedUtc)
            .Take(resultsPerSection)
            .Select(x => new ContextRecordViewModel
            {
                Kind = ContextRecordKind.Memory,
                Id = x.Memory.Id,
                KindLabel = "Memory",
                Title = x.Memory.Title,
                Subtitle = $"{x.Memory.Kind} • {x.Memory.Wing?.Name ?? "Unsorted"} / {x.Memory.Room?.Name ?? "General"}",
                Preview = x.Memory.Summary,
                Url = $"/Palace/Memory/{x.Memory.Id}",
                Score = x.Match.Score,
                SemanticScore = x.Match.SemanticScore,
                ScoreLabel = FormatScore(x.Match.Score),
                MatchReason = x.Match.Reason,
                FreshnessWarning = x.Trust.FreshnessWarning,
                Provenance = MapProvenance(x.Match)
            })
            .ToArray();

        var todoResults = todos
            .Select(todo =>
            {
                var score = ScoreFields(
                    normalizedQuestion,
                    tokens,
                    semanticTokens,
                    semanticProfile,
                    ConservativeSemanticPolicy,
                    todo.UpdatedUtc,
                    new WeightedField(todo.Title, "title", "Title", 20m, "Todo title matches your question closely.", "Todo title shares your search terms."),
                    new WeightedField(todo.Details, "details", "Details", 8m, "Todo details contain the full request.", "Todo details reinforce the match."));

                score = ApplyBoost(score, todo.Status == TodoStatus.InProgress ? 4m : todo.Status == TodoStatus.Pending ? 2m : 0m, todo.Status == TodoStatus.InProgress ? "In-progress work" : "Pending work");
                score = ApplyBoost(score, GoalBoost(effectiveInput.PackGoal, ContextRecordKind.Todo), GoalBoostLabel(effectiveInput.PackGoal, ContextRecordKind.Todo));
                score = ApplyBoost(score, effectiveInput.PreferRecentChanges && todo.UpdatedUtc >= DateTime.UtcNow.AddDays(-7) ? 1.5m : 0m, "Recent change");

                return new { Todo = todo, Match = score };
            })
            .Where(x => x.Match.Score > 0)
            .OrderByDescending(x => x.Match.Score)
            .ThenByDescending(x => x.Todo.UpdatedUtc)
            .Take(resultsPerSection)
            .Select(x => new ContextRecordViewModel
            {
                Kind = ContextRecordKind.Todo,
                Id = x.Todo.Id,
                KindLabel = "Todo",
                Title = x.Todo.Title,
                Subtitle = $"{MapTodoLabel(x.Todo.Status)} • updated {x.Todo.UpdatedUtc:g} UTC",
                Preview = TrimPreview(x.Todo.Details, 180),
                Url = $"/Todos/Details/{x.Todo.Id}",
                Score = x.Match.Score,
                SemanticScore = x.Match.SemanticScore,
                ScoreLabel = FormatScore(x.Match.Score),
                MatchReason = x.Match.Reason,
                Provenance = MapProvenance(x.Match)
            })
            .ToArray();

        var ticketResults = tickets
            .Select(ticket =>
            {
                var score = ScoreFields(
                    normalizedQuestion,
                    tokens,
                    semanticTokens,
                    semanticProfile,
                    ConservativeSemanticPolicy,
                    ticket.UpdatedUtc,
                    new WeightedField($"{ticket.TicketNumber} {ticket.Title}", "title", "Title/number", 20m, "Ticket title or number matches your request closely.", "Ticket title or number overlaps the request."),
                    new WeightedField(ticket.Description, "description", "Description", 8m, "Ticket description contains the full request.", "Ticket description reinforces the match."),
                    new WeightedField(ticket.GitBranch, "branch", "Git branch", 14m, "Git branch name matches your request closely.", "Git branch overlaps the request."),
                    new WeightedField($"{ticket.Assignee} {ticket.TagsText}", "tags", "Tags/assignee", 10m, "Ticket tags or assignee line up with the request.", "Ticket tags or assignee overlap the request."),
                    new WeightedField(subTicketTexts.GetValueOrDefault(ticket.Id), "subtickets", "Sub-tickets", 6m, "Sub-ticket work directly matches the request.", "Sub-ticket work reinforces the request."),
                    new WeightedField(ticketNotes.GetValueOrDefault(ticket.Id), "notes", "Notes", 6m, "Ticket notes directly match the request.", "Ticket notes reinforce the request."),
                    new WeightedField(ticketActivities.GetValueOrDefault(ticket.Id), "activity", "Activity", 6m, "Ticket activity history directly matches the request.", "Ticket activity history reinforces the request."),
                    new WeightedField(ticketTimeLogs.GetValueOrDefault(ticket.Id), "time-logs", "Time logs", 5m, "Ticket time logs directly match the request.", "Ticket time logs reinforce the request."));

                score = ApplyBoost(score, ticket.Status == TicketStatus.InProgress ? 5m : ticket.Status == TicketStatus.New ? 3m : 0m, ticket.Status == TicketStatus.InProgress ? "In-progress ticket" : "New ticket");
                score = ApplyBoost(score, ticket.Priority == TicketPriority.Critical ? 2m : ticket.Priority == TicketPriority.High ? 1m : 0m, ticket.Priority == TicketPriority.Critical ? "Critical priority" : "High priority");
                score = ApplyBoost(score, GoalBoost(effectiveInput.PackGoal, ContextRecordKind.Ticket), GoalBoostLabel(effectiveInput.PackGoal, ContextRecordKind.Ticket));
                score = ApplyBoost(score, effectiveInput.PreferRecentChanges && recentTicketIds.Contains(ticket.Id) ? 2m : 0m, "Recent change");

                return new { Ticket = ticket, Match = score };
            })
            .Where(x => x.Match.Score > 0)
            .OrderByDescending(x => x.Match.Score)
            .ThenByDescending(x => x.Ticket.UpdatedUtc)
            .Take(resultsPerSection)
            .Select(x => new ContextRecordViewModel
            {
                Kind = ContextRecordKind.Ticket,
                Id = x.Ticket.Id,
                KindLabel = "Ticket",
                Title = $"{x.Ticket.TicketNumber} • {x.Ticket.Title}",
                Subtitle = $"{MapTicketLabel(x.Ticket.Status)} • {x.Ticket.Priority} • {x.Ticket.Assignee}",
                Preview = TrimPreview(x.Ticket.Description, 180),
                Url = $"/Tickets/Details/{x.Ticket.Id}",
                Score = x.Match.Score,
                SemanticScore = x.Match.SemanticScore,
                ScoreLabel = FormatScore(x.Match.Score),
                MatchReason = x.Match.Reason,
                Provenance = MapProvenance(x.Match)
            })
            .ToArray();

        var projectResults = suppressCodeGraph
            ? Array.Empty<ContextRecordViewModel>()
            : projects
            .Select(project =>
            {
                var score = ScoreFields(
                    normalizedQuestion,
                    tokens,
                    semanticTokens,
                    semanticProfile,
                    BuildProjectSemanticPolicy(projectPreferences[project.Id].HasStrongAffinity && (repositoryArchitectureQuery || currentProjectCodeQuery || hasNamedProjectReference)),
                    project.UpdatedUtc,
                    new WeightedField(project.Name, "name", "Project name", 22m, "Project name matches your request closely.", "Project name overlaps the request."),
                    new WeightedField(project.RootPath, "path", "Project path", 18m, "Project path matches the request.", "Project path overlaps the request."),
                    new WeightedField($"{project.Description} {project.Summary}", "summary", "Description/summary", 8m, "Project description contains the full request.", "Project description reinforces the match."));

                score = ApplyBoost(score, Math.Min(project.RelationshipCount / 50m, 3m), "High relationship count");
                score = ApplyBoost(score, GoalBoost(effectiveInput.PackGoal, ContextRecordKind.CodeGraphProject), GoalBoostLabel(effectiveInput.PackGoal, ContextRecordKind.CodeGraphProject));
                score = ApplyBoost(score, effectiveInput.PreferRecentChanges && recentProjectIds.Contains(project.Id) ? 1.5m : 0m, "Recent change");
                score = ApplyBoost(score, preferDurableMemoryLead ? 0.5m : 0m, "Project context");
                score = ApplyBoost(score, projectPreferences[project.Id].ProjectQueryBoost, projectPreferences[project.Id].ProjectQueryBoostLabel);
                score = ApplyBoost(score, projectPreferences[project.Id].RepoAffinityBoost, projectPreferences[project.Id].RepoAffinityBoostLabel);
                score = ApplyBoost(
                    score,
                    externalOpsQuery && !projectPreferences[project.Id].HasStrongAffinity ? -8m : 0m,
                    externalOpsQuery && !projectPreferences[project.Id].HasStrongAffinity ? "Non-code admin query" : string.Empty);

                return new { Project = project, Match = score };
            })
            .Where(x => x.Match.Score > 0)
            .Where(x => !projectHistoryQuery || projectHistoryFocusProject is null || x.Project.Id == projectHistoryFocusProject.Id || projectPreferences[x.Project.Id].ProjectQueryBoost > 0m)
            .OrderByDescending(x => x.Match.Score)
            .ThenByDescending(x => x.Project.UpdatedUtc)
            .Take(projectHistoryQuery ? Math.Min(2, Math.Max(1, resultsPerSection)) : Math.Max(3, Math.Min(6, resultsPerSection)))
            .Select(x => new ContextRecordViewModel
            {
                Kind = ContextRecordKind.CodeGraphProject,
                Id = x.Project.Id,
                KindLabel = "Code graph",
                Title = x.Project.Name,
                Subtitle = x.Project.RootPath,
                Preview = string.IsNullOrWhiteSpace(x.Project.Description) ? x.Project.Summary : x.Project.Description,
                Url = $"/CodeGraph/Project/{x.Project.Id}",
                Score = x.Match.Score,
                SemanticScore = x.Match.SemanticScore,
                ScoreLabel = FormatScore(x.Match.Score),
                MatchReason = x.Match.Reason,
                Provenance = MapProvenance(x.Match)
            })
            .ToArray();

        var fileResults = suppressCodeGraph || (projectHistoryQuery && !explicitCodeQuery)
            ? Array.Empty<ContextRecordViewModel>()
            : files
            .Select(file =>
            {
                projectPreferences.TryGetValue(file.ProjectId, out var fileProjectPreference);
                var score = ScoreFields(
                    normalizedQuestion,
                    tokens,
                    semanticTokens,
                    semanticProfile,
                    BuildProjectSemanticPolicy(
                        fileProjectPreference?.HasStrongAffinity == true
                        && (repositoryArchitectureQuery || currentProjectCodeQuery || hasNamedProjectReference)),
                    file.ScannedUtc,
                    new WeightedField(file.RelativePath, "path", "File path", 20m, "File path matches your request closely.", "File path overlaps the request."),
                    new WeightedField(file.Language, "language", "Language", 5m, "File language matches your request.", "File language reinforces the request."),
                    new WeightedField(file.Project?.Name, "project", "Project", 8m, "Project name matches your request closely.", "Project name overlaps the request."));

                score = ApplyBoost(score, GoalBoost(effectiveInput.PackGoal, ContextRecordKind.CodeGraphFile), GoalBoostLabel(effectiveInput.PackGoal, ContextRecordKind.CodeGraphFile));
                score = ApplyBoost(score, effectiveInput.PreferRecentChanges && recentFileIds.Contains(file.Id) ? 1.5m : 0m, "Recent change");
                score = ApplyBoost(score, preferDurableMemoryLead ? 0.25m : 0m, "Supporting file context");
                if (fileProjectPreference is not null)
                {
                    score = ApplyBoost(score, fileProjectPreference.ProjectQueryBoost, fileProjectPreference.ProjectQueryBoostLabel);
                    score = ApplyBoost(score, fileProjectPreference.RepoAffinityBoost, fileProjectPreference.RepoAffinityBoostLabel);
                    score = ApplyBoost(
                        score,
                        productUiChangeQuery ? BuildProductUiFileBoost(file, nodes, fileProjectPreference.HasStrongAffinity, tokens) : 0m,
                        productUiChangeQuery ? "Product UI change" : string.Empty);
                    score = ApplyBoost(
                        score,
                        externalOpsQuery && !fileProjectPreference.HasStrongAffinity ? -8m : 0m,
                        externalOpsQuery && !fileProjectPreference.HasStrongAffinity ? "Non-code admin query" : string.Empty);
                }

                return new { File = file, Match = score };
            })
            .Where(x => x.Match.Score > 0)
            .OrderByDescending(x => x.Match.Score)
            .ThenBy(x => x.File.RelativePath)
            .Take(resultsPerSection)
            .Select(x => new ContextRecordViewModel
            {
                Kind = ContextRecordKind.CodeGraphFile,
                Id = x.File.Id,
                KindLabel = "Code file",
                Title = x.File.RelativePath,
                Subtitle = $"{x.File.Language} • {x.File.Project?.Name}",
                Preview = $"{x.File.LineCount} lines",
                Url = $"/CodeGraph/Project/{x.File.ProjectId}?selectedFileId={x.File.Id}",
                Score = x.Match.Score,
                SemanticScore = x.Match.SemanticScore,
                ScoreLabel = FormatScore(x.Match.Score),
                MatchReason = x.Match.Reason,
                Provenance = MapProvenance(x.Match)
            })
            .ToArray();

        var nodeResults = suppressCodeGraph || (projectHistoryQuery && !explicitCodeQuery)
            ? Array.Empty<ContextRecordViewModel>()
            : nodes
            .Select(node =>
            {
                projectPreferences.TryGetValue(node.ProjectId, out var nodeProjectPreference);
                var score = ScoreFields(
                    normalizedQuestion,
                    tokens,
                    semanticTokens,
                    semanticProfile,
                    BuildProjectSemanticPolicy(
                        nodeProjectPreference?.HasStrongAffinity == true
                        && (repositoryArchitectureQuery || currentProjectCodeQuery || hasNamedProjectReference)),
                    null,
                    new WeightedField(node.Label, "name", "Symbol name", 24m, "Symbol name matches your request closely.", "Symbol name overlaps the request."),
                    new WeightedField(node.File?.RelativePath, "path", "File path", 20m, "File path matches the request.", "File path overlaps the request."),
                    new WeightedField($"{node.NodeType} {node.SecondaryLabel}", "metadata", "Symbol metadata", 8m, "Symbol metadata contains the full request.", "Symbol metadata reinforces the match."));

                score = ApplyBoost(score, Math.Min(nodeDegrees.GetValueOrDefault(node.Id) / 4m, 3m), "High graph connectivity");
                score = ApplyBoost(score, GoalBoost(effectiveInput.PackGoal, ContextRecordKind.CodeGraphNode), GoalBoostLabel(effectiveInput.PackGoal, ContextRecordKind.CodeGraphNode));
                score = ApplyBoost(score, effectiveInput.PreferRecentChanges && recentNodeIds.Contains(node.Id) ? 1.5m : 0m, "Recent change");
                score = ApplyBoost(score, preferDurableMemoryLead ? 0.25m : 0m, "Supporting symbol context");
                if (nodeProjectPreference is not null)
                {
                    score = ApplyBoost(score, nodeProjectPreference.ProjectQueryBoost, nodeProjectPreference.ProjectQueryBoostLabel);
                    score = ApplyBoost(score, nodeProjectPreference.RepoAffinityBoost, nodeProjectPreference.RepoAffinityBoostLabel);
                    score = ApplyBoost(
                        score,
                        productUiChangeQuery ? BuildProductUiNodeBoost(node, nodeProjectPreference.HasStrongAffinity, tokens) : 0m,
                        productUiChangeQuery ? "Product UI change" : string.Empty);
                    score = ApplyBoost(
                        score,
                        externalOpsQuery && !nodeProjectPreference.HasStrongAffinity ? -8m : 0m,
                        externalOpsQuery && !nodeProjectPreference.HasStrongAffinity ? "Non-code admin query" : string.Empty);
                }

                return new { Node = node, Match = score };
            })
            .Where(x => x.Match.Score > 0)
            .OrderByDescending(x => x.Match.Score)
            .ThenBy(x => x.Node.Label)
            .Take(resultsPerSection)
            .Select(x => new ContextRecordViewModel
            {
                Kind = ContextRecordKind.CodeGraphNode,
                Id = x.Node.Id,
                KindLabel = "Code symbol",
                Title = x.Node.Label,
                Subtitle = $"{x.Node.NodeType} • {x.Node.File?.RelativePath ?? x.Node.SecondaryLabel}",
                Preview = x.Node.SecondaryLabel,
                Url = $"/CodeGraph/Project/{x.Node.ProjectId}?selectedNodeId={x.Node.Id}",
                Score = x.Match.Score,
                SemanticScore = x.Match.SemanticScore,
                ScoreLabel = FormatScore(x.Match.Score),
                MatchReason = x.Match.Reason,
                Provenance = MapProvenance(x.Match)
            })
            .ToArray();

        var linkedKeys = (await _dbContext.ContextLinks
                .AsNoTracking()
                .ToListAsync(cancellationToken))
            .SelectMany(link => new[]
            {
                new ContextRecordKey(link.SourceKind, link.SourceId),
                new ContextRecordKey(link.TargetKind, link.TargetId)
            })
            .ToHashSet();

        memoryResults = ApplyLinkBoost(memoryResults, linkedKeys);
        todoResults = ApplyLinkBoost(todoResults, linkedKeys);
        ticketResults = ApplyLinkBoost(ticketResults, linkedKeys);
        projectResults = ApplyLinkBoost(projectResults, linkedKeys);
        fileResults = ApplyLinkBoost(fileResults, linkedKeys);
        nodeResults = ApplyLinkBoost(nodeResults, linkedKeys);
        if (allowSemanticHybrid)
        {
            memoryResults = await ApplySemanticHybridAsync(memoryResults, EmbeddingTargetKind.Memory, normalizedQuestion, cancellationToken);
            todoResults = await ApplySemanticHybridAsync(todoResults, EmbeddingTargetKind.Todo, normalizedQuestion, cancellationToken);
            ticketResults = await ApplySemanticHybridAsync(ticketResults, EmbeddingTargetKind.Ticket, normalizedQuestion, cancellationToken);
        }

        var topMatches = BuildTopMatches(
            memoryResults,
            todoResults,
            ticketResults,
            projectResults,
            fileResults,
            nodeResults,
            resultsPerSection,
            preferDurableMemoryLead);
        var rawRecommendedSkillMatches = SkillRecommendationEngine.Recommend(
                skills,
                normalizedQuestion,
                effectiveInput.WingId,
                null,
                Math.Clamp(resultsPerSection, 3, 6),
                intentPrediction);
        var retrievalAgreementRatio = CalculateRetrievalAgreementRatio(intentPrediction, rawRecommendedSkillMatches);
        var specificGroundingRatio = CalculateSpecificGroundingRatio(tokens, normalizedQuestionPhrase, topMatches, rawRecommendedSkillMatches);
        var recommendedSkillMatches = rawRecommendedSkillMatches;
        if (directoryAdminQuery && !explicitCodeQuery)
        {
            recommendedSkillMatches = recommendedSkillMatches
                .Where(match => IsDirectoryAdminRelevantSkill(match.Skill, tokens, normalizedQuestionPhrase))
                .ToArray();
        }
        else if (projectHistoryQuery)
        {
            recommendedSkillMatches = Array.Empty<SkillRecommendationMatch>();
        }
        else if (wmiDiagnosticQuery)
        {
            recommendedSkillMatches = recommendedSkillMatches
                .Where(match => IsWmiDiagnosticRelevantSkill(match.Skill))
                .ToArray();
        }
        else if (portCheckQuery)
        {
            recommendedSkillMatches = recommendedSkillMatches
                .Where(match => IsPortCheckRelevantSkill(match.Skill))
                .ToArray();
        }
        else if (softwareInstallQuery)
        {
            recommendedSkillMatches = recommendedSkillMatches
                .Where(match => IsSoftwareInstallRelevantSkill(match.Skill))
                .ToArray();
        }
        else if (windowsServicingQuery)
        {
            recommendedSkillMatches = recommendedSkillMatches
                .Where(match => IsWindowsServicingRelevantSkill(match.Skill))
                .ToArray();
        }
        else if (windowsUpdateQuery)
        {
            recommendedSkillMatches = recommendedSkillMatches
                .Where(match => IsWindowsUpdateRelevantSkill(match.Skill))
                .ToArray();
        }
        else if (localSupportQuery)
        {
            recommendedSkillMatches = recommendedSkillMatches
                .Where(match => IsLocalSupportRelevantSkill(match.Skill))
                .ToArray();
        }
        else if (genericAutomationQuery)
        {
            recommendedSkillMatches = recommendedSkillMatches
                .Where(match => IsGenericAutomationRelevantSkill(match.Skill, tokens, normalizedQuestionPhrase))
                .ToArray();
        }
        else if (productUiChangeQuery)
        {
            recommendedSkillMatches = recommendedSkillMatches
                .Where(match => IsProductUiRelevantSkill(match.Skill))
                .ToArray();
        }
        else if (webUiQuery)
        {
            recommendedSkillMatches = recommendedSkillMatches
                .Where(match => IsWebUiRelevantSkill(match.Skill))
                .ToArray();
        }
        else if (cloudOpsQuery)
        {
            recommendedSkillMatches = recommendedSkillMatches
                .Where(match => IsCloudOpsRelevantSkill(match.Skill))
                .ToArray();
        }
        else if (desktopAppQuery)
        {
            recommendedSkillMatches = recommendedSkillMatches
                .Where(match => IsDesktopAppRelevantSkill(match.Skill))
                .ToArray();
        }
        else if (repositoryArchitectureQuery)
        {
            recommendedSkillMatches = recommendedSkillMatches
                .Where(match => IsRepositoryArchitectureRelevantSkill(match.Skill))
                .ToArray();
        }
        if (allowSemanticHybrid)
        {
            recommendedSkillMatches = await ApplySkillSemanticHybridAsync(recommendedSkillMatches, normalizedQuestion, cancellationToken);
        }

        var recommendedSkills = recommendedSkillMatches
            .Select(MapRecommendedSkill)
            .ToArray();
        var hasFacetRoute =
            fileComparisonQuery
            || projectHistoryQuery
            || wmiDiagnosticQuery
            || portCheckQuery
            || softwareInstallQuery
            || windowsServicingQuery
            || windowsUpdateQuery
            || intentPrediction.IsPasswordExpiryQuery;
        var retrievalDecision = _packDecisionEngine.EvaluateRetrieval(
            intentPrediction,
            topMatches.Count,
            recommendedSkills.Length,
            retrievalAgreementRatio,
            specificGroundingRatio,
            hasFacetRoute);
        if (!retrievalDecision.ShouldProceed)
        {
            return await BuildDecisionPackAsync(
                retrievalDecision,
                normalizedQuestion,
                effectiveInput,
                resultsPerSection,
                tokens,
                normalizedQuestionPhrase,
                projectHistoryQuery,
                explicitCodeQuery,
                fileComparisonQuery,
                intentPrediction.IsPasswordExpiryQuery,
                softwareInstallQuery,
                windowsServicingQuery,
                windowsUpdateQuery,
                intentPrediction.IsGenericAutomationQuery,
                localSupportQuery,
                wmiDiagnosticQuery,
                portCheckQuery,
                webUiQuery,
                cloudOpsQuery,
                desktopAppQuery,
                cancellationToken);
        }

        var candidatePack = new ContextPackViewModel
        {
            Question = normalizedQuestion,
            Summary = BuildSummary(memoryResults, todoResults, ticketResults, projectResults, fileResults, nodeResults),
            GoalLabel = GetPackGoalLabel(effectiveInput.PackGoal),
            NeedsMoreContext = false,
            Decision = BuildDecisionViewModel(retrievalDecision),
            Input = new ContextBriefInput
            {
                Question = normalizedQuestion,
                IncludeCompletedWork = effectiveInput.IncludeCompletedWork,
                WingId = effectiveInput.WingId,
                RoomId = effectiveInput.RoomId,
                Kind = effectiveInput.Kind,
                Tag = effectiveInput.Tag,
                IncludeRetired = effectiveInput.IncludeRetired,
                ExpandHistory = effectiveInput.ExpandHistory,
                ResultsPerSection = resultsPerSection,
                PackGoal = effectiveInput.PackGoal,
                PreferRecentChanges = effectiveInput.PreferRecentChanges
            },
            SearchTokens = tokens.OrderBy(x => x).ToArray(),
            DetectedGapItems = Array.Empty<DashboardWarningViewModel>(),
            ClarifyingQuestions = Array.Empty<string>(),
            TopMatches = topMatches,
            Memories = memoryResults,
            Todos = todoResults,
            Tickets = ticketResults,
            CodeGraphProjects = projectResults,
            CodeGraphFiles = fileResults,
            CodeGraphNodes = nodeResults,
            RecommendedSkills = recommendedSkills,
            ExportText = BuildExportText(
                normalizedQuestion,
                effectiveInput.PackGoal,
                BuildDecisionViewModel(retrievalDecision),
                recommendedSkills,
                memoryResults,
                todoResults,
                ticketResults,
                projectResults,
                fileResults,
                nodeResults,
                Array.Empty<DashboardWarningViewModel>(),
                Array.Empty<string>())
        };
        var allowCodeGraph = explicitCodeQuery || repositoryArchitectureQuery || projectHistoryQuery || currentProjectHint || hasNamedProjectReference;
        var (finalPack, alreadyArchived) = await RunCriticLoopAsync(
            candidatePack,
            normalizedQuestion,
            normalizedQuestionPhrase,
            tokens,
            effectiveInput,
            resultsPerSection,
            intentPrediction,
            projectHistoryQuery,
            explicitCodeQuery,
            fileComparisonQuery,
            localSupportQuery,
            wmiDiagnosticQuery,
            portCheckQuery,
            webUiQuery,
            cloudOpsQuery,
            desktopAppQuery,
            repositoryArchitectureQuery,
            softwareInstallQuery,
            windowsServicingQuery,
            windowsUpdateQuery,
            allowCodeGraph,
            hasFacetRoute,
            preferDurableMemoryLead,
            cancellationToken);
        if (alreadyArchived)
        {
            return finalPack;
        }

        await TouchMemoryReferencesAsync(finalPack.Memories.Select(x => x.Id).ToArray(), cancellationToken);
        return await ArchivePackIfNeededAsync(finalPack, cancellationToken);
    }

    public async Task<ContextLinksPanelViewModel> BuildLinksPanelAsync(
        ContextRecordKind sourceKind,
        Guid sourceId,
        string sourceTitle,
        string sourceText,
        string returnUrl,
        CancellationToken cancellationToken)
    {
        sourceText = TrimPreview(sourceText, 500);

        var directLinks = await _dbContext.ContextLinks
            .AsNoTracking()
            .Where(x => (x.SourceKind == sourceKind && x.SourceId == sourceId) || (x.TargetKind == sourceKind && x.TargetId == sourceId))
            .OrderByDescending(x => x.CreatedUtc)
            .ToListAsync(cancellationToken);

        var linkedRefs = directLinks
            .Select(link => link.SourceKind == sourceKind && link.SourceId == sourceId
                ? new LinkedContextRef(link.Id, link.Label, link.TargetKind, link.TargetId)
                : new LinkedContextRef(link.Id, link.Label, link.SourceKind, link.SourceId))
            .GroupBy(link => new { link.Kind, link.TargetId })
            .Select(group => group.First())
            .ToArray();

        var linkedItems = await ResolveLinkedItemsAsync(linkedRefs, cancellationToken);
        var contextPack = await BuildContextPackAsync(new ContextBriefInput
        {
            Question = sourceText,
            ExpandHistory = true,
            IncludeCompletedWork = true,
            ResultsPerSection = 8,
            PackGoal = ContextPackGoal.General
        }, cancellationToken);
        var suggestedItems = contextPack is null
            ? Array.Empty<ContextRecordViewModel>()
            : contextPack.TopMatches
                .Where(x => !(x.Kind == sourceKind && x.Id == sourceId))
                .Where(x => !linkedRefs.Any(link => link.Kind == x.Kind && link.TargetId == x.Id))
                .Take(8)
                .ToArray();

        return new ContextLinksPanelViewModel
        {
            SourceKind = sourceKind,
            SourceId = sourceId,
            SourceTitle = sourceTitle,
            SourceText = sourceText,
            ReturnUrl = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl,
            LinkedItems = linkedItems,
            SuggestedItems = suggestedItems
        };
    }

    public async Task AddLinkAsync(ContextLinkCreateInput input, CancellationToken cancellationToken)
    {
        if (input.SourceKind == input.TargetKind && input.SourceId == input.TargetId)
        {
            throw new InvalidOperationException("An item cannot be linked to itself.");
        }

        await EnsureExistsAsync(input.SourceKind, input.SourceId, cancellationToken);
        await EnsureExistsAsync(input.TargetKind, input.TargetId, cancellationToken);

        var normalizedLink = NormalizeLink(input.SourceKind, input.SourceId, input.TargetKind, input.TargetId);

        var exists = await _dbContext.ContextLinks.AnyAsync(
            x => x.SourceKind == normalizedLink.SourceKind
                 && x.SourceId == normalizedLink.SourceId
                 && x.TargetKind == normalizedLink.TargetKind
                 && x.TargetId == normalizedLink.TargetId,
            cancellationToken);

        if (exists)
        {
            return;
        }

        _dbContext.ContextLinks.Add(new ContextLinkEntry
        {
            SourceKind = normalizedLink.SourceKind,
            SourceId = normalizedLink.SourceId,
            TargetKind = normalizedLink.TargetKind,
            TargetId = normalizedLink.TargetId,
            Label = string.IsNullOrWhiteSpace(input.Label) ? "Related" : input.Label.Trim(),
            CreatedUtc = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveLinkAsync(Guid linkId, CancellationToken cancellationToken)
    {
        var link = await _dbContext.ContextLinks.FirstOrDefaultAsync(x => x.Id == linkId, cancellationToken);
        if (link is null)
        {
            throw new InvalidOperationException("That context link no longer exists.");
        }

        _dbContext.ContextLinks.Remove(link);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> AddSuggestedLinksAsync(ContextSuggestedLinksInput input, CancellationToken cancellationToken)
    {
        await EnsureExistsAsync(input.SourceKind, input.SourceId, cancellationToken);
        var contextPack = await BuildContextPackAsync(new ContextBriefInput
        {
            Question = input.SourceText,
            IncludeCompletedWork = true,
            ExpandHistory = true,
            ResultsPerSection = Math.Clamp(input.Limit + 2, 3, 10),
            PackGoal = ContextPackGoal.General
        }, cancellationToken);

        if (contextPack is null)
        {
            return 0;
        }

        var suggestions = contextPack.TopMatches
            .Where(x => !(x.Kind == input.SourceKind && x.Id == input.SourceId))
            .Take(Math.Clamp(input.Limit, 1, 10))
            .ToArray();

        var added = 0;
        foreach (var suggestion in suggestions)
        {
            var countBefore = await _dbContext.ContextLinks.CountAsync(cancellationToken);
            await AddLinkAsync(new ContextLinkCreateInput
            {
                SourceKind = input.SourceKind,
                SourceId = input.SourceId,
                TargetKind = suggestion.Kind,
                TargetId = suggestion.Id,
                Label = input.Label,
                ReturnUrl = input.ReturnUrl
            }, cancellationToken);

            var countAfter = await _dbContext.ContextLinks.CountAsync(cancellationToken);
            if (countAfter > countBefore)
            {
                added++;
            }
        }

        return added;
    }

    private async Task<IReadOnlyCollection<ContextLinkedItemViewModel>> ResolveLinkedItemsAsync(
        IEnumerable<LinkedContextRef> refs,
        CancellationToken cancellationToken)
    {
        var items = new List<ContextLinkedItemViewModel>();
        foreach (var item in refs)
        {
            var resolved = await ResolveRecordAsync(item.Kind, item.TargetId, cancellationToken);
            if (resolved is null)
            {
                continue;
            }

            items.Add(new ContextLinkedItemViewModel
            {
                LinkId = item.LinkId,
                Kind = resolved.Kind,
                TargetId = resolved.Id,
                KindLabel = resolved.KindLabel,
                Title = resolved.Title,
                Subtitle = resolved.Subtitle,
                Url = resolved.Url,
                Label = item.Label
            });
        }

        return items;
    }

    private async Task<ContextRecordViewModel?> ResolveRecordAsync(ContextRecordKind kind, Guid id, CancellationToken cancellationToken)
    {
        return kind switch
        {
            ContextRecordKind.Memory => await _dbContext.Memories
                .AsNoTracking()
                .Include(x => x.Wing)
                .Include(x => x.Room)
                .Where(x => x.Id == id)
                .Select(x => new ContextRecordViewModel
                {
                    Kind = ContextRecordKind.Memory,
                    Id = x.Id,
                    KindLabel = "Memory",
                    Title = x.Title,
                    Subtitle = $"{x.Kind} • {x.Wing!.Name ?? "Unsorted"}",
                    Preview = x.Summary,
                    Url = $"/Palace/Memory/{x.Id}"
                })
                .FirstOrDefaultAsync(cancellationToken),
            ContextRecordKind.Todo => await _dbContext.Todos
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new ContextRecordViewModel
                {
                    Kind = ContextRecordKind.Todo,
                    Id = x.Id,
                    KindLabel = "Todo",
                    Title = x.Title,
                    Subtitle = MapTodoLabel(x.Status),
                    Preview = x.Details,
                    Url = $"/Todos/Details/{x.Id}"
                })
                .FirstOrDefaultAsync(cancellationToken),
            ContextRecordKind.Ticket => await _dbContext.Tickets
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new ContextRecordViewModel
                {
                    Kind = ContextRecordKind.Ticket,
                    Id = x.Id,
                    KindLabel = "Ticket",
                    Title = $"{x.TicketNumber} • {x.Title}",
                    Subtitle = string.IsNullOrWhiteSpace(x.GitBranch) ? MapTicketLabel(x.Status) : $"{MapTicketLabel(x.Status)} • {x.GitBranch}",
                    Preview = x.Description,
                    Url = $"/Tickets/Details/{x.Id}"
                })
                .FirstOrDefaultAsync(cancellationToken),
            ContextRecordKind.CodeGraphProject => await _dbContext.CodeGraphProjects
                .AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new ContextRecordViewModel
                {
                    Kind = ContextRecordKind.CodeGraphProject,
                    Id = x.Id,
                    KindLabel = "Code graph",
                    Title = x.Name,
                    Subtitle = x.RootPath,
                    Preview = x.Description,
                    Url = $"/CodeGraph/Project/{x.Id}"
                })
                .FirstOrDefaultAsync(cancellationToken),
            ContextRecordKind.CodeGraphNode => await _dbContext.CodeGraphNodes
                .AsNoTracking()
                .Include(x => x.File)
                .Where(x => x.Id == id)
                .Select(x => new ContextRecordViewModel
                {
                    Kind = ContextRecordKind.CodeGraphNode,
                    Id = x.Id,
                    KindLabel = "Code symbol",
                    Title = x.Label,
                    Subtitle = x.SecondaryLabel,
                    Preview = x.File!.RelativePath ?? string.Empty,
                    Url = $"/CodeGraph/Project/{x.ProjectId}?selectedNodeId={x.Id}"
                })
                .FirstOrDefaultAsync(cancellationToken),
            ContextRecordKind.CodeGraphFile => await _dbContext.CodeGraphFiles
                .AsNoTracking()
                .Include(x => x.Project)
                .Where(x => x.Id == id)
                .Select(x => new ContextRecordViewModel
                {
                    Kind = ContextRecordKind.CodeGraphFile,
                    Id = x.Id,
                    KindLabel = "Code file",
                    Title = x.RelativePath,
                    Subtitle = $"{x.Language} • {x.Project!.Name}",
                    Preview = $"{x.LineCount} lines",
                    Url = $"/CodeGraph/Project/{x.ProjectId}?selectedFileId={x.Id}"
                })
                .FirstOrDefaultAsync(cancellationToken),
            _ => null
        };
    }

    private async Task EnsureExistsAsync(ContextRecordKind kind, Guid id, CancellationToken cancellationToken)
    {
        var exists = kind switch
        {
            ContextRecordKind.Memory => await _dbContext.Memories.AnyAsync(x => x.Id == id, cancellationToken),
            ContextRecordKind.Todo => await _dbContext.Todos.AnyAsync(x => x.Id == id, cancellationToken),
            ContextRecordKind.Ticket => await _dbContext.Tickets.AnyAsync(x => x.Id == id, cancellationToken),
            ContextRecordKind.CodeGraphProject => await _dbContext.CodeGraphProjects.AnyAsync(x => x.Id == id, cancellationToken),
            ContextRecordKind.CodeGraphNode => await _dbContext.CodeGraphNodes.AnyAsync(x => x.Id == id, cancellationToken),
            ContextRecordKind.CodeGraphFile => await _dbContext.CodeGraphFiles.AnyAsync(x => x.Id == id, cancellationToken),
            _ => false
        };

        if (!exists)
        {
            throw new InvalidOperationException("That context target no longer exists.");
        }
    }

    private static string BuildSummary(
        IReadOnlyCollection<ContextRecordViewModel> memories,
        IReadOnlyCollection<ContextRecordViewModel> todos,
        IReadOnlyCollection<ContextRecordViewModel> tickets,
        IReadOnlyCollection<ContextRecordViewModel> projects,
        IReadOnlyCollection<ContextRecordViewModel> files,
        IReadOnlyCollection<ContextRecordViewModel> nodes)
    {
        return $"Found {memories.Count} memories, {todos.Count} todos, {tickets.Count} tickets, {projects.Count} code graph projects, {files.Count} code files, and {nodes.Count} matching code symbols.";
    }

    private static IReadOnlyCollection<ContextRecordViewModel> BuildTopMatches(
        IReadOnlyCollection<ContextRecordViewModel> memoryResults,
        IReadOnlyCollection<ContextRecordViewModel> todoResults,
        IReadOnlyCollection<ContextRecordViewModel> ticketResults,
        IReadOnlyCollection<ContextRecordViewModel> projectResults,
        IReadOnlyCollection<ContextRecordViewModel> fileResults,
        IReadOnlyCollection<ContextRecordViewModel> nodeResults,
        int resultsPerSection,
        bool preferDurableMemoryLead)
    {
        var ordered = memoryResults
            .Concat(todoResults)
            .Concat(ticketResults)
            .Concat(projectResults)
            .Concat(fileResults)
            .Concat(nodeResults)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Title)
            .ToArray();

        var takeCount = Math.Max(6, resultsPerSection);
        if (!preferDurableMemoryLead || memoryResults.Count == 0)
        {
            return ordered.Take(takeCount).ToArray();
        }

        var bestMemory = memoryResults
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Title)
            .First();
        var bestOverall = ordered.FirstOrDefault();
        if (bestOverall is null || bestOverall.Id == bestMemory.Id || bestMemory.Score + 6m < bestOverall.Score)
        {
            return ordered.Take(takeCount).ToArray();
        }

        return new[] { bestMemory }
            .Concat(ordered.Where(x => x.Id != bestMemory.Id))
            .Take(takeCount)
            .ToArray();
    }

    private static string BuildExportText(
        string question,
        ContextPackGoal goal,
        ContextPackDecisionViewModel decision,
        IReadOnlyCollection<SkillCardViewModel> recommendedSkills,
        IReadOnlyCollection<ContextRecordViewModel> memories,
        IReadOnlyCollection<ContextRecordViewModel> todos,
        IReadOnlyCollection<ContextRecordViewModel> tickets,
        IReadOnlyCollection<ContextRecordViewModel> projects,
        IReadOnlyCollection<ContextRecordViewModel> files,
        IReadOnlyCollection<ContextRecordViewModel> nodes,
        IReadOnlyCollection<DashboardWarningViewModel> detectedGapItems,
        IReadOnlyCollection<string> clarifyingQuestions)
    {
        var sections = new[]
        {
            ("Memories", memories),
            ("Todos", todos),
            ("Tickets", tickets),
            ("Code graph projects", projects),
            ("Code files", files),
            ("Code symbols", nodes)
        };

        var builder = new System.Text.StringBuilder()
            .AppendLine($"Context pack: {question}")
            .AppendLine($"Goal: {GetPackGoalLabel(goal)}")
            .AppendLine();

        if (!string.IsNullOrWhiteSpace(decision.Kind))
        {
            builder.AppendLine("Decision");
            builder.AppendLine("--------");
            builder.Append("- Outcome: ").AppendLine(decision.Kind);
            if (!string.IsNullOrWhiteSpace(decision.PrimaryCause))
            {
                builder.Append("- Primary cause: ").AppendLine(decision.PrimaryCause);
            }

            if (decision.Causes.Count > 0)
            {
                builder.Append("- Causes: ").AppendLine(string.Join(", ", decision.Causes));
            }

            foreach (var evidenceLine in decision.Evidence)
            {
                builder.Append("- Evidence: ").AppendLine(evidenceLine);
            }

            foreach (var reason in decision.Reasons)
            {
                builder.Append("- Reason: ").AppendLine(reason);
            }

            builder.AppendLine();
        }

        if (detectedGapItems.Count > 0)
        {
            builder.AppendLine("Context gaps");
            builder.AppendLine("------------");
            foreach (var gap in detectedGapItems)
            {
                builder.Append("- ").AppendLine(gap.Message);
            }

            builder.AppendLine();
        }

        if (clarifyingQuestions.Count > 0)
        {
            builder.AppendLine("Clarifying follow-ups");
            builder.AppendLine("---------------------");
            foreach (var questionPrompt in clarifyingQuestions)
            {
                builder.Append("- ").AppendLine(questionPrompt);
            }

            builder.AppendLine();
        }

        builder.AppendLine("Recommended skills");
        builder.AppendLine("------------------");
        if (recommendedSkills.Count == 0)
        {
            builder.AppendLine("- No recommended skills");
        }
        else
        {
            foreach (var skill in recommendedSkills)
            {
                builder.Append("- ").Append(skill.Name);
                if (!string.IsNullOrWhiteSpace(skill.RecommendationReason))
                {
                    builder.Append(" | ").Append(skill.RecommendationReason);
                }

                if (skill.NeedsReview)
                {
                    builder.Append(" | ").Append(skill.ReviewLabel);
                }

                builder.AppendLine();
                builder.AppendLine($"  {skill.Summary}");
                builder.AppendLine($"  /Skills/Skill/{skill.Slug}");
            }
        }

        builder.AppendLine();

        foreach (var (title, items) in sections)
        {
            builder.AppendLine(title);
            builder.AppendLine(new string('-', title.Length));

            if (items.Count == 0)
            {
                builder.AppendLine("- No matches");
            }
            else
            {
                foreach (var item in items)
                {
                    builder.Append("- ").Append(item.Title);
                    if (!string.IsNullOrWhiteSpace(item.FreshnessWarning))
                    {
                        builder.Append(" [").Append(item.FreshnessWarning).Append(']');
                    }
                    if (!string.IsNullOrWhiteSpace(item.MatchReason))
                    {
                        builder.Append(" | ").Append(item.MatchReason);
                    }
                    if (item.SemanticScore > 0m)
                    {
                        builder.Append(" | semantic ").Append(item.SemanticScore.ToString("0.0"));
                    }

                    builder.AppendLine();

                    if (!string.IsNullOrWhiteSpace(item.Subtitle))
                    {
                        builder.AppendLine($"  {item.Subtitle}");
                    }

                    if (!string.IsNullOrWhiteSpace(item.Preview))
                    {
                        builder.AppendLine($"  {item.Preview}");
                    }

                    builder.AppendLine($"  {item.Url}");
                }
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static ContextPackDecisionViewModel BuildDecisionViewModel(PackDecision decision)
    {
        var causes = decision.Scorecard.EffectiveCauses
            .Select(FormatDecisionCause)
            .ToArray();
        return new ContextPackDecisionViewModel
        {
            Kind = decision.Kind.ToString(),
            PrimaryCause = causes.FirstOrDefault() ?? string.Empty,
            Causes = causes,
            Reasons = decision.Scorecard.EffectiveReasons.ToArray(),
            Evidence = BuildDecisionEvidence(decision.Scorecard)
        };
    }

    private static IReadOnlyCollection<string> BuildDecisionEvidence(PackDecisionScorecard scorecard)
    {
        var evidence = new List<string>
        {
            $"Top route score {scorecard.TopScore:0.00} with margin {scorecard.TopMargin:0.00}.",
            $"Informative tokens: {scorecard.InformativeTokenCount}; specific informative tokens: {scorecard.SpecificInformativeTokenCount}; facet signals: {scorecard.FacetSignalCount}."
        };

        if (scorecard.RetrievalYield > 0 || scorecard.TopMatchCount > 0 || scorecard.RecommendedSkillCount > 0)
        {
            evidence.Add($"Grounded retrieval items: {scorecard.TopMatchCount} top matches, {scorecard.RecommendedSkillCount} skills, total yield {scorecard.RetrievalYield}.");
        }

        if (scorecard.RetrievalAgreementRatio.HasValue)
        {
            evidence.Add($"Retrieval agreement ratio: {scorecard.RetrievalAgreementRatio.Value:0.00}.");
        }

        if (scorecard.SpecificGroundingRatio.HasValue)
        {
            evidence.Add($"Specific grounding ratio: {scorecard.SpecificGroundingRatio.Value:0.00}.");
        }

        return evidence;
    }

    private static string FormatDecisionCause(PackDecisionCause cause)
        => cause switch
        {
            PackDecisionCause.InsufficientContext => "insufficient-context",
            PackDecisionCause.MissingFamily => "missing-family",
            PackDecisionCause.NearNeighborCollision => "near-neighbor-collision",
            PackDecisionCause.RetrievalPollution => "retrieval-pollution",
            _ => cause.ToString()
        };

    private static SkillCardViewModel MapRecommendedSkill(SkillRecommendationMatch match)
    {
        var now = DateTime.UtcNow;
        return new SkillCardViewModel
        {
            Id = match.Skill.Id,
            WingId = match.Skill.WingId,
            Name = match.Skill.Name,
            Slug = match.Skill.Slug,
            Summary = match.Skill.Summary,
            Category = match.Skill.Category,
            CategoryLabel = match.Skill.Category.ToString(),
            WingName = match.Skill.Wing?.Name ?? string.Empty,
            TriggerHintsText = match.Skill.TriggerHintsText,
            TriggerHints = SplitSkillText(match.Skill.TriggerHintsText),
            IsPinned = match.Skill.IsPinned,
            UseCount = match.Skill.UseCount,
            LastUsedUtc = match.Skill.LastUsedUtc,
            LastReviewedUtc = match.Skill.LastReviewedUtc,
            ReviewAfterUtc = match.Skill.ReviewAfterUtc,
            NeedsReview = SkillRecommendationEngine.NeedsReview(match.Skill, now),
            ReviewLabel = SkillRecommendationEngine.GetReviewLabel(match.Skill, now),
            RecommendationReason = match.Reason,
            RecommendationScore = match.Score,
            RecommendationScoreLabel = match.Score <= 0m ? string.Empty : match.Score.ToString("0.#"),
            UpdatedUtc = match.Skill.UpdatedUtc
        };
    }

    private async Task<ContextPackViewModel> ArchivePackIfNeededAsync(ContextPackViewModel pack, CancellationToken cancellationToken)
    {
        if (_packBuildArchiveService is null)
        {
            return pack;
        }

        var archivedBuildId = await _packBuildArchiveService.RecordAsync(pack, cancellationToken);
        return new ContextPackViewModel
        {
            ArchivedBuildId = archivedBuildId,
            Question = pack.Question,
            Summary = pack.Summary,
            GoalLabel = pack.GoalLabel,
            NeedsMoreContext = pack.NeedsMoreContext,
            Decision = pack.Decision,
            Input = pack.Input,
            SearchTokens = pack.SearchTokens,
            DetectedGapItems = pack.DetectedGapItems,
            ClarifyingQuestions = pack.ClarifyingQuestions,
            TopMatches = pack.TopMatches,
            Memories = pack.Memories,
            Todos = pack.Todos,
            Tickets = pack.Tickets,
            CodeGraphProjects = pack.CodeGraphProjects,
            CodeGraphFiles = pack.CodeGraphFiles,
            CodeGraphNodes = pack.CodeGraphNodes,
            RecommendedSkills = pack.RecommendedSkills,
            ExternalSkillAlert = pack.ExternalSkillAlert,
            ExportText = pack.ExportText
        };
    }

    private async Task<(ContextPackViewModel Pack, bool AlreadyArchived)> RunCriticLoopAsync(
        ContextPackViewModel candidatePack,
        string normalizedQuestion,
        string normalizedQuestionPhrase,
        IReadOnlyCollection<string> tokens,
        ContextBriefInput effectiveInput,
        int resultsPerSection,
        PackIntentPrediction intentPrediction,
        bool projectHistoryQuery,
        bool explicitCodeQuery,
        bool fileComparisonQuery,
        bool localSupportQuery,
        bool wmiDiagnosticQuery,
        bool portCheckQuery,
        bool webUiQuery,
        bool cloudOpsQuery,
        bool desktopAppQuery,
        bool repositoryArchitectureQuery,
        bool softwareInstallQuery,
        bool windowsServicingQuery,
        bool windowsUpdateQuery,
        bool allowCodeGraph,
        bool hasFacetRoute,
        bool preferDurableMemoryLead,
        CancellationToken cancellationToken)
    {
        if (allowCodeGraph)
        {
            return (candidatePack, false);
        }

        var workingPack = candidatePack;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var critique = _packCriticEngine.Evaluate(new PackCritiqueContext(
                normalizedQuestionPhrase,
                tokens,
                workingPack,
                hasFacetRoute,
                allowCodeGraph,
                attempt));
            if (critique.Action == PackCritiqueAction.Accept)
            {
                return (workingPack, false);
            }

            if (critique.Action == PackCritiqueAction.Repair && attempt < 3)
            {
                workingPack = ApplyCriticRepairs(
                    workingPack,
                    critique,
                    normalizedQuestion,
                    effectiveInput.PackGoal,
                    resultsPerSection,
                    preferDurableMemoryLead);
                continue;
            }

            var unsupportedDecision = new PackDecision(
                PackDecisionKind.Unsupported,
                new PackDecisionScorecard(
                    TopScore: intentPrediction.TopScore,
                    TopMargin: intentPrediction.TopMargin,
                    IsAmbiguous: intentPrediction.IsAmbiguous,
                    QueryNeedsMoreContext: intentPrediction.NeedsMoreContext,
                    InformativeTokenCount: intentPrediction.InformativeTokenCount,
                    SpecificInformativeTokenCount: intentPrediction.SpecificInformativeTokenCount,
                    FacetSignalCount: intentPrediction.FacetSignalCount,
                    Causes: critique.Issues.Any(issue => issue.Code is "ungrounded-skills" or "ungrounded-memories" or "unexpected-codegraph" or "generic-overlap-only")
                        ? new[] { PackDecisionCause.RetrievalPollution }
                        : Array.Empty<PackDecisionCause>(),
                    Reasons: critique.Issues.Select(issue => issue.Message).ToArray()));
            var unsupportedPack = await BuildDecisionPackAsync(
                unsupportedDecision,
                normalizedQuestion,
                effectiveInput,
                resultsPerSection,
                tokens,
                normalizedQuestionPhrase,
                projectHistoryQuery,
                explicitCodeQuery,
                fileComparisonQuery,
                intentPrediction.IsPasswordExpiryQuery,
                softwareInstallQuery,
                windowsServicingQuery,
                windowsUpdateQuery,
                intentPrediction.IsGenericAutomationQuery,
                localSupportQuery,
                wmiDiagnosticQuery,
                portCheckQuery,
                webUiQuery,
                cloudOpsQuery,
                desktopAppQuery,
                cancellationToken);
            return (unsupportedPack, true);
        }

        return (workingPack, false);
    }

    private static ContextPackViewModel ApplyCriticRepairs(
        ContextPackViewModel pack,
        PackCritiqueResult critique,
        string normalizedQuestion,
        ContextPackGoal goal,
        int resultsPerSection,
        bool preferDurableMemoryLead)
    {
        var issueCodes = critique.Issues.Select(issue => issue.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var groundedSkillIds = (critique.Directive.GroundedSkillIds ?? Array.Empty<Guid>())
            .ToHashSet();
        var groundedMemoryIds = (critique.Directive.GroundedMemoryIds ?? Array.Empty<Guid>())
            .ToHashSet();
        var recommendedSkills = issueCodes.Contains("ungrounded-skills")
            ? pack.RecommendedSkills.Where(skill => groundedSkillIds.Contains(skill.Id)).ToArray()
            : pack.RecommendedSkills.ToArray();
        var memories = issueCodes.Contains("ungrounded-memories")
            ? pack.Memories.Where(memory => groundedMemoryIds.Contains(memory.Id)).ToArray()
            : pack.Memories.ToArray();
        var codeGraphProjects = critique.Directive.SuppressCodeGraph ? Array.Empty<ContextRecordViewModel>() : pack.CodeGraphProjects.ToArray();
        var codeGraphFiles = critique.Directive.SuppressCodeGraph ? Array.Empty<ContextRecordViewModel>() : pack.CodeGraphFiles.ToArray();
        var codeGraphNodes = critique.Directive.SuppressCodeGraph ? Array.Empty<ContextRecordViewModel>() : pack.CodeGraphNodes.ToArray();
        var topMatches = BuildTopMatches(
            memories,
            pack.Todos,
            pack.Tickets,
            codeGraphProjects,
            codeGraphFiles,
            codeGraphNodes,
            resultsPerSection,
            preferDurableMemoryLead);

        return new ContextPackViewModel
        {
            Question = pack.Question,
            Summary = BuildSummary(memories, pack.Todos, pack.Tickets, codeGraphProjects, codeGraphFiles, codeGraphNodes),
            GoalLabel = pack.GoalLabel,
            NeedsMoreContext = pack.NeedsMoreContext,
            Decision = pack.Decision,
            Input = pack.Input,
            SearchTokens = pack.SearchTokens,
            DetectedGapItems = pack.DetectedGapItems,
            ClarifyingQuestions = pack.ClarifyingQuestions,
            TopMatches = topMatches,
            Memories = memories,
            Todos = pack.Todos,
            Tickets = pack.Tickets,
            CodeGraphProjects = codeGraphProjects,
            CodeGraphFiles = codeGraphFiles,
            CodeGraphNodes = codeGraphNodes,
            RecommendedSkills = recommendedSkills,
            ExternalSkillAlert = pack.ExternalSkillAlert,
            ExportText = BuildExportText(
                normalizedQuestion,
                goal,
                pack.Decision,
                recommendedSkills,
                memories,
                pack.Todos,
                pack.Tickets,
                codeGraphProjects,
                codeGraphFiles,
                codeGraphNodes,
                pack.DetectedGapItems,
                pack.ClarifyingQuestions)
        };
    }

    private async Task<ContextPackViewModel> BuildDecisionPackAsync(
        PackDecision decision,
        string normalizedQuestion,
        ContextBriefInput effectiveInput,
        int resultsPerSection,
        IReadOnlyCollection<string> tokens,
        string normalizedQuestionPhrase,
        bool projectHistoryQuery,
        bool explicitCodeQuery,
        bool fileComparisonQuery,
        bool passwordExpiryQuery,
        bool softwareInstallQuery,
        bool windowsServicingQuery,
        bool windowsUpdateQuery,
        bool genericAutomationQuery,
        bool localSupportQuery,
        bool wmiDiagnosticQuery,
        bool portCheckQuery,
        bool webUiQuery,
        bool cloudOpsQuery,
        bool desktopAppQuery,
        CancellationToken cancellationToken)
    {
        var nearbyMemories = decision.Kind == PackDecisionKind.Unsupported
                             || decision.Scorecard.SpecificInformativeTokenCount >= 2
            ? await BuildNearbyMemoryMatchesAsync(
                normalizedQuestion,
                effectiveInput,
                resultsPerSection,
                tokens,
                normalizedQuestionPhrase,
                projectHistoryQuery,
                fileComparisonQuery,
                softwareInstallQuery,
                windowsServicingQuery,
                windowsUpdateQuery,
                genericAutomationQuery,
                localSupportQuery,
                wmiDiagnosticQuery,
                portCheckQuery,
                webUiQuery,
                cloudOpsQuery,
                desktopAppQuery,
                explicitCodeQuery,
                cancellationToken)
            : Array.Empty<ContextRecordViewModel>();
        var gapItems = decision.Kind == PackDecisionKind.Unsupported
            ? BuildUnsupportedGroundingGapItems()
            : BuildLowContextGapItems();
        var clarifyingQuestions = BuildClarifyingQuestions(
            tokens,
            normalizedQuestionPhrase,
            projectHistoryQuery,
            explicitCodeQuery,
            fileComparisonQuery,
            passwordExpiryQuery,
            softwareInstallQuery,
            windowsServicingQuery,
            windowsUpdateQuery,
            localSupportQuery,
            wmiDiagnosticQuery,
            portCheckQuery,
            webUiQuery,
            cloudOpsQuery,
            desktopAppQuery);
        var summary = decision.Kind == PackDecisionKind.Unsupported
            ? nearbyMemories.Count > 0
                ? "Focus found a likely lane but not enough grounded support to answer safely yet; nearby memories may help you refine the request."
                : "Focus found a likely lane, but it does not have enough grounded supporting context to answer safely yet."
            : nearbyMemories.Count > 0
                ? "Need more context before Focus can build a fact-based pack, but nearby memories may help narrow the request."
                : "Need more context before Focus can build a fact-based pack.";
        var topMatches = BuildTopMatches(
            nearbyMemories,
            Array.Empty<ContextRecordViewModel>(),
            Array.Empty<ContextRecordViewModel>(),
            Array.Empty<ContextRecordViewModel>(),
            Array.Empty<ContextRecordViewModel>(),
            Array.Empty<ContextRecordViewModel>(),
            resultsPerSection,
            ShouldPreferDurableMemoryLead(normalizedQuestion, effectiveInput));
        return await ArchivePackIfNeededAsync(new ContextPackViewModel
        {
            Question = normalizedQuestion,
            Summary = summary,
            GoalLabel = GetPackGoalLabel(effectiveInput.PackGoal),
            NeedsMoreContext = true,
            Decision = BuildDecisionViewModel(decision),
            Input = new ContextBriefInput
            {
                Question = normalizedQuestion,
                IncludeCompletedWork = effectiveInput.IncludeCompletedWork,
                WingId = effectiveInput.WingId,
                RoomId = effectiveInput.RoomId,
                Kind = effectiveInput.Kind,
                Tag = effectiveInput.Tag,
                IncludeRetired = effectiveInput.IncludeRetired,
                ExpandHistory = effectiveInput.ExpandHistory,
                ResultsPerSection = resultsPerSection,
                PackGoal = effectiveInput.PackGoal,
                PreferRecentChanges = effectiveInput.PreferRecentChanges
            },
            SearchTokens = tokens.OrderBy(x => x).ToArray(),
            DetectedGapItems = gapItems,
            ClarifyingQuestions = clarifyingQuestions,
            TopMatches = topMatches,
            Memories = nearbyMemories,
            ExportText = BuildExportText(
                normalizedQuestion,
                effectiveInput.PackGoal,
                BuildDecisionViewModel(decision),
                Array.Empty<SkillCardViewModel>(),
                nearbyMemories,
                Array.Empty<ContextRecordViewModel>(),
                Array.Empty<ContextRecordViewModel>(),
                Array.Empty<ContextRecordViewModel>(),
                Array.Empty<ContextRecordViewModel>(),
                Array.Empty<ContextRecordViewModel>(),
                gapItems,
                clarifyingQuestions)
        }, cancellationToken);
    }

    private async Task<IReadOnlyCollection<ContextRecordViewModel>> BuildNearbyMemoryMatchesAsync(
        string normalizedQuestion,
        ContextBriefInput effectiveInput,
        int resultsPerSection,
        IReadOnlyCollection<string> tokens,
        string normalizedQuestionPhrase,
        bool projectHistoryQuery,
        bool fileComparisonQuery,
        bool softwareInstallQuery,
        bool windowsServicingQuery,
        bool windowsUpdateQuery,
        bool genericAutomationQuery,
        bool localSupportQuery,
        bool wmiDiagnosticQuery,
        bool portCheckQuery,
        bool webUiQuery,
        bool cloudOpsQuery,
        bool desktopAppQuery,
        bool explicitCodeQuery,
        CancellationToken cancellationToken)
    {
        if (tokens.Count == 0)
        {
            return Array.Empty<ContextRecordViewModel>();
        }

        var semanticTokens = ExpandSemanticTokens(tokens);
        var semanticProfile = BuildSemanticQueryProfile(normalizedQuestion, tokens, semanticTokens);
        var preferDurableMemoryLead = ShouldPreferDurableMemoryLead(normalizedQuestion, effectiveInput);
        var repositoryArchitectureQuery = IsRepositoryArchitectureQuery(tokens, normalizedQuestionPhrase);
        var currentProjectCodeQuery = explicitCodeQuery && HasCurrentProjectHint(tokens);
        var directoryAdminQuery = IsDirectoryAdminQuery(tokens);

        var memories = await _dbContext.Memories
            .AsNoTracking()
            .Include(x => x.Wing)
            .Include(x => x.Room)
            .Include(x => x.MemoryTags)
                .ThenInclude(x => x.Tag)
            .ToListAsync(cancellationToken);

        if (!effectiveInput.IncludeRetired)
        {
            memories = memories.Where(x => x.LifecycleState == MemoryLifecycleState.Active).ToList();
        }

        if (effectiveInput.WingId.HasValue)
        {
            memories = memories.Where(x => x.WingId == effectiveInput.WingId.Value).ToList();
        }

        if (effectiveInput.RoomId.HasValue)
        {
            memories = memories.Where(x => x.RoomId == effectiveInput.RoomId.Value).ToList();
        }

        if (effectiveInput.Kind.HasValue)
        {
            memories = memories.Where(x => x.Kind == effectiveInput.Kind.Value).ToList();
        }

        if (!string.IsNullOrWhiteSpace(effectiveInput.Tag))
        {
            var tagSlug = SlugUtility.CreateSlug(effectiveInput.Tag);
            var tagMatchedMemories = memories.Where(x => x.MemoryTags.Any(tag => tag.Tag!.Slug == tagSlug)).ToList();
            if (tagMatchedMemories.Count > 0)
            {
                memories = tagMatchedMemories;
            }
        }

        return memories
            .Select(memory =>
            {
                var trust = MemoryTrustHelper.Build(memory);
                var score = ScoreFields(
                    normalizedQuestion,
                    tokens,
                    semanticTokens,
                    semanticProfile,
                    BuildMemorySemanticPolicy(effectiveInput.PackGoal, preferDurableMemoryLead, repositoryArchitectureQuery, projectHistoryQuery),
                    MemoryTrustHelper.GetEffectiveTimestamp(memory.UpdatedUtc, memory.LastVerifiedUtc),
                    new WeightedField(memory.Title, "title", "Title", 20m, "Title matches your question closely.", "Title shares your search terms."),
                    new WeightedField(memory.Summary, "summary", "Summary", 12m, "Summary closely matches the request.", "Summary reinforces the match."),
                    new WeightedField(TrimPreview(memory.Content, 2000), "content", "Content", 7m, "Memory content contains the full request.", "Memory content covers the same terms."),
                    new WeightedField($"{memory.Wing?.Name} {memory.Room?.Name}", "location", "Wing/room", 8m, "Wing or room naming matches the request.", "Wing or room naming overlaps the request."),
                    new WeightedField(string.Join(' ', memory.MemoryTags.Select(x => x.Tag!.Name)), "tags", "Tags", 10m, "Tags line up with the request.", "Tags overlap the request."));

                score = ApplyBoost(score, memory.IsPinned ? 4m : 0m, "Pinned memory");
                score = ApplyBoost(score, Math.Max(memory.Importance - 3, 0), "High importance");
                score = ApplyBoost(score, trust.RetrievalAdjustment, trust.RetrievalAdjustmentLabel);
                score = ApplyBoost(score, GoalBoost(effectiveInput.PackGoal, ContextRecordKind.Memory, memory.SourceKind, memory.Kind), GoalBoostLabel(effectiveInput.PackGoal, ContextRecordKind.Memory));
                score = ApplyBoost(score, BuildScopedMemoryBoost(memory, effectiveInput), BuildScopedMemoryBoostLabel(effectiveInput));
                score = ApplyBoost(score, preferDurableMemoryLead ? DurableMemoryQuestionBoost(memory) : 0m, "Durable memory lead");
                score = ApplyBoost(score, directoryAdminQuery ? BuildDirectoryAdminMismatchPenalty(memory) : 0m, directoryAdminQuery ? "Directory domain mismatch" : string.Empty);

                return new { Memory = memory, Match = score, Trust = trust };
            })
            .Where(x => x.Match.Score >= 18m)
            .Where(x => !directoryAdminQuery || explicitCodeQuery || IsDirectoryAdminRelevantMemory(x.Memory, tokens, normalizedQuestionPhrase))
            .Where(x => !fileComparisonQuery || IsFileComparisonRelevantMemory(x.Memory))
            .Where(x => !projectHistoryQuery || IsProjectHistoryRelevantMemory(x.Memory, tokens))
            .Where(x => !wmiDiagnosticQuery || IsWmiDiagnosticRelevantMemory(x.Memory))
            .Where(x => !portCheckQuery || IsPortCheckRelevantMemory(x.Memory))
            .Where(x => !softwareInstallQuery || IsSoftwareInstallRelevantMemory(x.Memory))
            .Where(x => !windowsServicingQuery || IsWindowsServicingRelevantMemory(x.Memory))
            .Where(x => !windowsUpdateQuery || IsWindowsUpdateRelevantMemory(x.Memory))
            .Where(x => !genericAutomationQuery || IsGenericAutomationRelevantMemory(x.Memory))
            .Where(x => !localSupportQuery || IsLocalSupportRelevantMemory(x.Memory))
            .Where(x => !webUiQuery || currentProjectCodeQuery || IsWebUiRelevantMemory(x.Memory))
            .Where(x => !cloudOpsQuery || IsCloudOpsRelevantMemory(x.Memory))
            .Where(x => !desktopAppQuery || currentProjectCodeQuery || IsDesktopAppRelevantMemory(x.Memory))
            .Where(x => !repositoryArchitectureQuery || IsRepositoryArchitectureRelevantMemory(x.Memory) || IsCurrentProjectRelevantMemory(x.Memory))
            .Where(x => !currentProjectCodeQuery || IsCurrentProjectRelevantMemory(x.Memory) || IsCodeRelevantMemory(x.Memory))
            .OrderByDescending(x => x.Match.Score)
            .ThenByDescending(x => x.Memory.UpdatedUtc)
            .Take(Math.Min(resultsPerSection, 3))
            .Select(x => new ContextRecordViewModel
            {
                Kind = ContextRecordKind.Memory,
                Id = x.Memory.Id,
                KindLabel = "Memory",
                Title = x.Memory.Title,
                Subtitle = $"{x.Memory.Kind} • {x.Memory.Wing?.Name ?? "Unsorted"} / {x.Memory.Room?.Name ?? "General"}",
                Preview = x.Memory.Summary,
                Url = $"/Palace/Memory/{x.Memory.Id}",
                Score = x.Match.Score,
                SemanticScore = x.Match.SemanticScore,
                ScoreLabel = FormatScore(x.Match.Score),
                MatchReason = x.Match.Reason,
                FreshnessWarning = x.Trust.FreshnessWarning,
                Provenance = MapProvenance(x.Match)
            })
            .ToArray();
    }

    private static IReadOnlyCollection<DashboardWarningViewModel> BuildLowContextGapItems()
    {
        return
        [
            new DashboardWarningViewModel
            {
                Code = "need-more-context",
                Severity = "warning",
                Message = "The question is too thin to route reliably, so Focus is holding back instead of guessing.",
                ActionLabel = string.Empty,
                ActionUrl = string.Empty
            }
        ];
    }

    private static IReadOnlyCollection<DashboardWarningViewModel> BuildUnsupportedGroundingGapItems()
    {
        return
        [
            new DashboardWarningViewModel
            {
                Code = "insufficient-grounding",
                Severity = "warning",
                Message = "Focus found a likely lane, but it does not have enough grounded support to answer without guessing.",
                ActionLabel = string.Empty,
                ActionUrl = string.Empty
            }
        ];
    }

    private static IReadOnlyCollection<string> BuildClarifyingQuestions(
        IReadOnlyCollection<string> tokens,
        string normalizedQuestion,
        bool projectHistoryQuery,
        bool explicitCodeQuery,
        bool fileComparisonQuery,
        bool passwordExpiryQuery,
        bool softwareInstallQuery,
        bool windowsServicingQuery,
        bool windowsUpdateQuery,
        bool localSupportQuery,
        bool wmiDiagnosticQuery,
        bool portCheckQuery,
        bool webUiQuery,
        bool cloudOpsQuery,
        bool desktopAppQuery)
    {
        if (fileComparisonQuery)
        {
            return
            [
                "Which two folders should Focus compare, and do you want name, size, hash, or content differences?",
                "Should the result show changed files only, or also left-only and right-only files?"
            ];
        }

        if (passwordExpiryQuery)
        {
            return
            [
                "Which user or account should Focus check for password expiry?",
                "Do you need an on-prem AD PowerShell command, or a quick net user fallback?"
            ];
        }

        if (softwareInstallQuery)
        {
            return
            [
                "Which software package should Focus install, and do you already know the exact vendor download URL?",
                "Do you want a silent install for one local PC, or a reusable script with logging and exit-code checks?"
            ];
        }

        if (windowsServicingQuery)
        {
            return
            [
                "Do you need a quick DISM health-check command, or the full scan and repair sequence?",
                "Is this for the local Windows image, an offline image path, or follow-up repair guidance after a failure?"
            ];
        }

        if (windowsUpdateQuery)
        {
            return
            [
                "Do you want to check a single local PC, or do you need a reusable script that can run on multiple machines?",
                "Should the script use the built-in Windows Update Agent API only, or may it depend on the PSWindowsUpdate module if it is installed?"
            ];
        }

        if (projectHistoryQuery || tokens.Contains("project") || tokens.Contains("repo") || tokens.Contains("repository"))
        {
            return
            [
                "Which project or path should Focus inspect?",
                "Do you want recent changes, current status, or specific files and symbols?"
            ];
        }

        if (explicitCodeQuery || tokens.Contains("code") || tokens.Contains("file") || tokens.Contains("files"))
        {
            return
            [
                "Which project, file, class, or method should Focus inspect?",
                "What exact change or question do you want answered about that code?"
            ];
        }

        if (wmiDiagnosticQuery || (localSupportQuery && (normalizedQuestion.Contains("wmi", StringComparison.Ordinal) || normalizedQuestion.Contains("winrm", StringComparison.Ordinal))))
        {
            return
            [
                "Which PC or endpoint is this about, and do you want a local-only or remote check?",
                "Do you want to test WMI itself, WinRM remoting, or a specific namespace or class?"
            ];
        }

        if (localSupportQuery && IsRemoteLoggedInUserQuery(tokens, normalizedQuestion))
        {
            return
            [
                "Do you need the currently logged-in user on one remote PC, or a script that can check many PCs on the network?",
                "Can the script rely on PowerShell remoting, WMI/CIM, or do you need an option that works when WinRM is not enabled?"
            ];
        }

        if (portCheckQuery)
        {
            return
            [
                "Which host and port should Focus test, and is it TCP or UDP?",
                "Do you want a local listener check, a remote reachability test, or both?"
            ];
        }

        if (webUiQuery)
        {
            return
            [
                "Which page or component is affected, and what is visibly wrong?",
                "Is this a layout issue, CSS regression, accessibility problem, or dark-mode issue?"
            ];
        }

        if (cloudOpsQuery)
        {
            return
            [
                "Which Azure service, app, or subscription is this about?",
                "Do you want telemetry, deployment, identity, or configuration guidance?"
            ];
        }

        if (desktopAppQuery)
        {
            return
            [
                "Which desktop app is this about, and what behavior is failing?",
                "Do you need diagnostics, UX fixes, packaging help, or runtime troubleshooting?"
            ];
        }

        return
        [
            "What system, project, or machine is this about?",
            "What exact outcome do you need so Focus can route on facts instead of guessing?"
        ];
    }

    private static bool HasStrongPackDomainSignals(
        IReadOnlyCollection<string> tokens,
        string normalizedQuestion,
        bool repositoryArchitectureQuery,
        bool fileComparisonQuery,
        bool projectHistoryQuery,
        bool passwordExpiryQuery,
        bool softwareInstallQuery,
        bool windowsServicingQuery,
        bool windowsUpdateQuery,
        bool localSupportQuery,
        bool wmiDiagnosticQuery,
        bool portCheckQuery,
        bool webUiQuery,
        bool cloudOpsQuery,
        bool desktopAppQuery,
        bool currentProjectHint)
    {
        if (repositoryArchitectureQuery || fileComparisonQuery || projectHistoryQuery || passwordExpiryQuery || softwareInstallQuery || windowsServicingQuery || windowsUpdateQuery || wmiDiagnosticQuery || portCheckQuery)
        {
            return true;
        }

        if (localSupportQuery && tokens.Intersect(LocalSupportHighSignalTokens, StringComparer.OrdinalIgnoreCase).Any())
        {
            return true;
        }

        if (webUiQuery && tokens.Intersect(WebUiHighSignalTokens, StringComparer.OrdinalIgnoreCase).Count() >= 2)
        {
            return true;
        }

        if (cloudOpsQuery && tokens.Intersect(CloudOpsHighSignalTokens, StringComparer.OrdinalIgnoreCase).Count() >= 2)
        {
            return true;
        }

        if (desktopAppQuery && tokens.Intersect(DesktopAppTokens, StringComparer.OrdinalIgnoreCase).Count() >= 2)
        {
            return true;
        }

        return normalizedQuestion.Contains("windows forms", StringComparison.Ordinal)
               || normalizedQuestion.Contains("app insights", StringComparison.Ordinal)
               || normalizedQuestion.Contains("high dpi", StringComparison.Ordinal)
               || currentProjectHint;
    }

    private static decimal? CalculateRetrievalAgreementRatio(
        PackIntentPrediction prediction,
        IReadOnlyCollection<SkillRecommendationMatch> matches)
    {
        if (matches.Count == 0)
        {
            return null;
        }

        Func<SkillRecommendationMatch, bool>? qualifier = null;
        if (prediction.IsDirectoryAdminQuery)
        {
            qualifier = match => match.IsDirectoryAdminQualified;
        }
        else if (prediction.IsRepositoryArchitectureQuery)
        {
            qualifier = match => match.IsRepositoryArchitectureQualified;
        }
        else if (prediction.IsGenericAutomationQuery)
        {
            qualifier = match => match.IsGenericAutomationQualified;
        }
        else if (prediction.IsExternalOperationsQuery)
        {
            qualifier = match => match.IsExternalOpsQualified;
        }

        if (qualifier is null)
        {
            return null;
        }

        var considered = matches.Take(3).ToArray();
        if (considered.Length == 0)
        {
            return null;
        }

        var aligned = considered.Count(qualifier);
        return decimal.Round((decimal)aligned / considered.Length, 2);
    }

    private static decimal? CalculateSpecificGroundingRatio(
        IReadOnlyCollection<string> queryTokens,
        string normalizedQuestion,
        IReadOnlyCollection<ContextRecordViewModel> topMatches,
        IReadOnlyCollection<SkillRecommendationMatch> skillMatches)
    {
        var specificTokens = queryTokens
            .Where(token => !RetrievalLowSignalTokens.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var specificPhrases = BuildSpecificGroundingPhrases(normalizedQuestion)
            .ToArray();
        if (specificTokens.Length == 0 && specificPhrases.Length == 0)
        {
            return null;
        }

        var considered = topMatches
            .Select(match => BuildGroundingText(match))
            .Concat(skillMatches.Select(match => BuildGroundingText(match.Skill)))
            .Take(6)
            .ToArray();
        if (considered.Length == 0)
        {
            return null;
        }

        var groundedCount = considered.Count(text => HasSpecificGrounding(text, specificTokens, specificPhrases));
        return decimal.Round((decimal)groundedCount / considered.Length, 2);
    }

    private static IEnumerable<string> BuildSpecificGroundingPhrases(string normalizedQuestion)
    {
        var tokens = normalizedQuestion
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !RetrievalLowSignalTokens.Contains(token))
            .ToArray();
        if (tokens.Length < 2)
        {
            return Array.Empty<string>();
        }

        var phrases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < tokens.Length - 1; index++)
        {
            phrases.Add($"{tokens[index]} {tokens[index + 1]}");
        }

        if (tokens.Length >= 3)
        {
            for (var index = 0; index < tokens.Length - 2; index++)
            {
                phrases.Add($"{tokens[index]} {tokens[index + 1]} {tokens[index + 2]}");
            }
        }

        return phrases;
    }

    private static bool HasSpecificGrounding(
        string text,
        IReadOnlyCollection<string> specificTokens,
        IReadOnlyCollection<string> specificPhrases)
    {
        var normalizedText = NormalizePhrase(text);
        if (specificPhrases.Any(phrase => normalizedText.Contains(phrase, StringComparison.Ordinal)))
        {
            return true;
        }

        var textTokens = Tokenize(text);
        var overlap = specificTokens.Count(token => textTokens.Contains(token));
        return specificTokens.Count == 1
            ? overlap == 1
            : overlap >= 2;
    }

    private static string BuildGroundingText(ContextRecordViewModel record)
        => string.Join(' ', new[]
        {
            record.Title,
            record.Subtitle,
            record.Preview,
            record.MatchReason,
            string.Join(' ', record.Provenance?.MatchedTokens ?? Array.Empty<string>())
        });

    private static string BuildGroundingText(SkillEntry skill)
        => string.Join(' ', new[]
        {
            skill.Name,
            skill.Summary,
            skill.WhenToUse,
            skill.Flow,
            skill.TriggerHintsText
        });

    private static IReadOnlyCollection<string> SplitSkillText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static ContextRecordViewModel[] ApplyLinkBoost(IEnumerable<ContextRecordViewModel> items, ISet<ContextRecordKey> linkedKeys)
    {
        return items
            .Select(item =>
            {
                var isLinked = linkedKeys.Contains(new ContextRecordKey(item.Kind, item.Id));
                var boostedScore = item.Score + (isLinked ? 5m : 0m);
                var provenance = item.Provenance is null
                    ? null
                    : new ContextMatchDetailViewModel
                    {
                        MatchedTokens = item.Provenance.MatchedTokens,
                        FieldHits = item.Provenance.FieldHits,
                        ExactPhraseMatched = item.Provenance.ExactPhraseMatched,
                        Boosts = isLinked
                            ? item.Provenance.Boosts.Concat(new[]
                            {
                                new ContextMatchBoostViewModel
                                {
                                    Label = "Linked context",
                                    Value = 5m
                                }
                            }).ToArray()
                            : item.Provenance.Boosts
                    };

                return new ContextRecordViewModel
                {
                    Kind = item.Kind,
                    Id = item.Id,
                    KindLabel = item.KindLabel,
                    Title = item.Title,
                    Subtitle = item.Subtitle,
                    Preview = item.Preview,
                    Url = item.Url,
                    Score = boostedScore,
                    SemanticScore = item.SemanticScore,
                    ScoreLabel = FormatScore(boostedScore),
                    MatchReason = isLinked && !item.MatchReason.Contains("Linked context", StringComparison.OrdinalIgnoreCase)
                        ? $"{item.MatchReason} Linked context already exists."
                        : item.MatchReason,
                    FreshnessWarning = item.FreshnessWarning,
                    DuplicateWarning = item.DuplicateWarning,
                    DuplicateCandidateId = item.DuplicateCandidateId,
                    IsLinked = isLinked,
                    Provenance = provenance
                };
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Title)
            .ToArray();
    }

    private bool ShouldEnableSemanticHybrid(
        string normalizedQuestion,
        bool fileComparisonQuery,
        bool projectHistoryQuery,
        bool externalAdminQuery,
        bool directoryAdminQuery,
        bool wmiDiagnosticQuery,
        bool portCheckQuery,
        bool softwareInstallQuery,
        bool windowsServicingQuery,
        bool windowsUpdateQuery,
        bool genericAutomationQuery)
        => _contextEmbeddingService?.IsEnabled == true
           && !string.IsNullOrWhiteSpace(normalizedQuestion)
           && !fileComparisonQuery
           && !projectHistoryQuery
           && !externalAdminQuery
           && !directoryAdminQuery
           && !wmiDiagnosticQuery
           && !portCheckQuery
           && !softwareInstallQuery
           && !windowsServicingQuery
           && !windowsUpdateQuery
           && !genericAutomationQuery;

    private async Task<ContextRecordViewModel[]> ApplySemanticHybridAsync(
        ContextRecordViewModel[] items,
        EmbeddingTargetKind targetKind,
        string question,
        CancellationToken cancellationToken)
    {
        if (_contextEmbeddingService is null || items.Length == 0)
        {
            return items;
        }

        var candidateTexts = items.ToDictionary(
            item => item.Id,
            BuildSemanticCandidateText);
        var semanticScores = await _contextEmbeddingService.ScoreAsync(targetKind, question, candidateTexts, cancellationToken);
        if (semanticScores.Count == 0)
        {
            return items;
        }

        return items
            .Select(item => semanticScores.TryGetValue(item.Id, out var semanticScore)
                ? ApplySemanticBoost(item, semanticScore)
                : item)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Title)
            .ToArray();
    }

    private async Task<SkillRecommendationMatch[]> ApplySkillSemanticHybridAsync(
        IReadOnlyCollection<SkillRecommendationMatch> matches,
        string question,
        CancellationToken cancellationToken)
    {
        if (_contextEmbeddingService is null || matches.Count == 0)
        {
            return matches.ToArray();
        }

        var candidateTexts = matches.ToDictionary(
            match => match.Skill.Id,
            match => BuildSemanticCandidateText(match.Skill));
        var semanticScores = await _contextEmbeddingService.ScoreAsync(EmbeddingTargetKind.Skill, question, candidateTexts, cancellationToken);
        if (semanticScores.Count == 0)
        {
            return matches.ToArray();
        }

        return matches
            .Select(match =>
            {
                if (!semanticScores.TryGetValue(match.Skill.Id, out var semanticScore))
                {
                    return match;
                }

                var reason = string.IsNullOrWhiteSpace(match.Reason)
                    ? $"{semanticScore.Label} complements the lexical match."
                    : $"{match.Reason} {semanticScore.Label} reinforces the match.";
                return match with
                {
                    Score = match.Score + semanticScore.Boost,
                    Reason = reason
                };
            })
            .OrderByDescending(match => match.Score)
            .ThenByDescending(match => match.Skill.IsPinned)
            .ThenByDescending(match => match.Skill.UseCount)
            .ThenByDescending(match => match.Skill.LastUsedUtc ?? DateTime.MinValue)
            .ThenBy(match => match.Skill.Name)
            .ToArray();
    }

    private static ContextRecordViewModel ApplySemanticBoost(ContextRecordViewModel item, SemanticCandidateScore semanticScore)
    {
        var boostedScore = item.Score + semanticScore.Boost;
        var provenance = item.Provenance is null
            ? null
            : new ContextMatchDetailViewModel
            {
                MatchedTokens = item.Provenance.MatchedTokens,
                FieldHits = item.Provenance.FieldHits,
                ExactPhraseMatched = item.Provenance.ExactPhraseMatched,
                Boosts = item.Provenance.Boosts.Concat(new[]
                {
                    new ContextMatchBoostViewModel
                    {
                        Label = semanticScore.Label,
                        Value = semanticScore.Boost
                    }
                }).ToArray()
            };

        return new ContextRecordViewModel
        {
            Kind = item.Kind,
            Id = item.Id,
            KindLabel = item.KindLabel,
            Title = item.Title,
            Subtitle = item.Subtitle,
            Preview = item.Preview,
            Url = item.Url,
            Score = boostedScore,
            SemanticScore = item.SemanticScore + semanticScore.Boost,
            ScoreLabel = FormatScore(boostedScore),
            MatchReason = string.IsNullOrWhiteSpace(item.MatchReason)
                ? $"{semanticScore.Label} complements the lexical match."
                : $"{item.MatchReason} {semanticScore.Label} reinforces the match.",
            FreshnessWarning = item.FreshnessWarning,
            DuplicateWarning = item.DuplicateWarning,
            DuplicateCandidateId = item.DuplicateCandidateId,
            IsLinked = item.IsLinked,
            Provenance = provenance
        };
    }

    private static string BuildSemanticCandidateText(ContextRecordViewModel item)
        => string.Join(" ", new[] { item.Title, item.Subtitle, item.Preview }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string BuildSemanticCandidateText(SkillEntry skill)
        => string.Join(" ", new[]
        {
            skill.Name,
            skill.Summary,
            skill.TriggerHintsText,
            skill.WhenToUse,
            skill.Flow
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private async Task TouchMemoryReferencesAsync(IReadOnlyCollection<Guid> memoryIds, CancellationToken cancellationToken)
    {
        if (memoryIds.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var ids = memoryIds.Distinct().ToArray();
        var memories = await _dbContext.Memories
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(cancellationToken);

        foreach (var memory in memories)
        {
            memory.LastReferencedUtc = now;
            memory.ReferenceCount += 1;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static ContextScoreOutcome ScoreFields(
        string question,
        IReadOnlyCollection<string> tokens,
        IReadOnlyCollection<string> semanticTokens,
        SemanticQueryProfile semanticProfile,
        SemanticScoringPolicy semanticPolicy,
        DateTime? updatedUtc,
        params WeightedField[] fields)
    {
        if (tokens.Count == 0)
        {
            return ContextScoreOutcome.None;
        }

        var normalizedQuestion = NormalizePhrase(question);
        var matchedTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        decimal score = 0m;
        decimal semanticScore = 0m;
        string reason = string.Empty;
        var exactPhraseMatched = false;
        var highSignalFieldMatched = false;
        var fieldHits = new List<ContextMatchFieldHitViewModel>();
        var boosts = new List<ContextMatchBoostViewModel>();

        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field.Text))
            {
                continue;
            }

            var fieldTokens = Tokenize(field.Text);
            if (fieldTokens.Count == 0)
            {
                continue;
            }

            var overlap = tokens.Where(fieldTokens.Contains).ToArray();
            if (overlap.Length > 0)
            {
                foreach (var token in overlap)
                {
                    matchedTokens.Add(token);
                }

                score += overlap.Length * field.PerTokenWeight;

                if (string.IsNullOrWhiteSpace(reason))
                {
                    reason = overlap.Length == tokens.Count ? field.ExactReason : field.PartialReason;
                }

                fieldHits.Add(new ContextMatchFieldHitViewModel
                {
                    FieldKey = field.Key,
                    Label = field.Label,
                    Tokens = overlap.Order(StringComparer.OrdinalIgnoreCase).ToArray()
                });
            }

            var normalizedField = NormalizePhrase(field.Text);
            if (!string.IsNullOrWhiteSpace(normalizedQuestion) && normalizedField.Contains(normalizedQuestion, StringComparison.Ordinal))
            {
                score += field.PerTokenWeight + 10m;
                reason = field.ExactReason;
                exactPhraseMatched = true;
                highSignalFieldMatched = true;
                boosts.Add(new ContextMatchBoostViewModel
                {
                    Label = $"{field.Label} exact phrase",
                    Value = field.PerTokenWeight + 10m
                });
                continue;
            }

            var fieldSemanticTokens = ExpandSemanticTokens(fieldTokens);
            var semanticOverlap = semanticTokens.Intersect(fieldSemanticTokens, StringComparer.OrdinalIgnoreCase)
                .Except(overlap, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (semanticOverlap.Length > 0)
            {
                var semanticBoost = Math.Min(semanticOverlap.Length, 3) * Math.Max(field.PerTokenWeight / 4m, 1m);
                score += semanticBoost;
                semanticScore += semanticBoost;
                if (overlap.Length > 0 || matchedTokens.Count > 0)
                {
                    foreach (var semanticToken in semanticOverlap)
                    {
                        matchedTokens.Add(semanticToken);
                    }
                }
                boosts.Add(new ContextMatchBoostViewModel
                {
                    Label = $"{field.Label} semantic similarity",
                    Value = semanticBoost
                });
            }

            var semanticSignal = ScoreSemanticField(normalizedField, fieldTokens, fieldSemanticTokens, semanticProfile);
            if (semanticSignal.SharedQueryTerms.Count >= semanticPolicy.MinimumSharedTerms
                && semanticSignal.Similarity >= semanticPolicy.MinimumSimilarity)
            {
                var denseBoost = Math.Min(
                    semanticPolicy.MaxBoost,
                    Math.Round(
                        semanticSignal.Similarity
                        * Math.Max(field.PerTokenWeight * semanticPolicy.BoostMultiplier, 1m),
                        2));

                if (denseBoost > 0m)
                {
                    score += denseBoost;
                    semanticScore += denseBoost;
                    boosts.Add(new ContextMatchBoostViewModel
                    {
                        Label = $"{field.Label} hybrid semantic match",
                        Value = denseBoost
                    });
                }

                if (overlap.Length == 0)
                {
                    fieldHits.Add(new ContextMatchFieldHitViewModel
                    {
                        FieldKey = field.Key,
                        Label = $"{field.Label} semantic",
                        Tokens = semanticSignal.SharedQueryTerms
                    });
                }
            }

            var phraseSimilarity = ScorePhraseSimilarity(normalizedQuestion, normalizedField);
            if (phraseSimilarity >= 0.72m)
            {
                var phraseBoost = Math.Round(phraseSimilarity * Math.Max(field.PerTokenWeight / 3m, 1m), 2);
                score += phraseBoost;
                semanticScore += phraseBoost;
                boosts.Add(new ContextMatchBoostViewModel
                {
                    Label = $"{field.Label} semantic phrasing",
                    Value = phraseBoost
                });
                if (string.IsNullOrWhiteSpace(reason))
                {
                    reason = field.PartialReason;
                }
            }

            if (overlap.Length == tokens.Count)
            {
                score += 12m;
                reason = field.ExactReason;
                highSignalFieldMatched = true;
                boosts.Add(new ContextMatchBoostViewModel
                {
                    Label = $"{field.Label} full token coverage",
                    Value = 12m
                });
            }
        }

        if (matchedTokens.Count == 0)
        {
            return ContextScoreOutcome.None;
        }

        if (tokens.Count >= 3 && matchedTokens.Count < 2 && !exactPhraseMatched && !highSignalFieldMatched)
        {
            return ContextScoreOutcome.None;
        }

        var matchedTokenCount = Math.Min(matchedTokens.Count, tokens.Count);
        var tokenCoverageBoost = (decimal)matchedTokenCount / tokens.Count * 20m;
        score += tokenCoverageBoost;
        boosts.Add(new ContextMatchBoostViewModel
        {
            Label = "Token coverage",
            Value = tokenCoverageBoost
        });

        var recencyBoost = ScoreRecency(updatedUtc);
        if (recencyBoost > 0m)
        {
            score += recencyBoost;
            boosts.Add(new ContextMatchBoostViewModel
            {
                Label = "Recent activity",
                Value = recencyBoost
            });
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            reason = matchedTokenCount == tokens.Count
                ? "All search terms matched."
                : matchedTokenCount == 1
                    ? $"Matched \"{matchedTokens.First()}\"."
                    : $"Matched {matchedTokenCount} search terms.";
        }

        return new ContextScoreOutcome(
            score,
            semanticScore,
            reason,
            matchedTokens.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            fieldHits.ToArray(),
            boosts.ToArray(),
            exactPhraseMatched);
    }

    private static ContextScoreOutcome ApplyBoost(ContextScoreOutcome score, decimal amount, string label)
    {
        if (amount == 0m || string.IsNullOrWhiteSpace(label))
        {
            return score;
        }

        return score with
        {
            Score = score.Score + amount,
            Boosts = score.Boosts.Concat(new[]
            {
                new ContextMatchBoostViewModel
                {
                    Label = label,
                    Value = amount
                }
            }).ToArray()
        };
    }

    private static ContextMatchDetailViewModel? MapProvenance(ContextScoreOutcome score)
    {
        if (score.Score <= 0m || score.MatchedTokens.Count == 0)
        {
            return null;
        }

        return new ContextMatchDetailViewModel
        {
            MatchedTokens = score.MatchedTokens.ToArray(),
            FieldHits = score.FieldHits.ToArray(),
            Boosts = score.Boosts.ToArray(),
            ExactPhraseMatched = score.ExactPhraseMatched
        };
    }

    private static string FormatScore(decimal score) => score switch
    {
        >= 60m => "Top match",
        >= 40m => "Strong match",
        >= 20m => "Relevant",
        _ => "Possible match"
    };

    private static NormalizedContextLink NormalizeLink(ContextRecordKind sourceKind, Guid sourceId, ContextRecordKind targetKind, Guid targetId)
    {
        var swap = sourceKind > targetKind || (sourceKind == targetKind && sourceId.CompareTo(targetId) > 0);
        return swap
            ? new NormalizedContextLink(targetKind, targetId, sourceKind, sourceId)
            : new NormalizedContextLink(sourceKind, sourceId, targetKind, targetId);
    }
    private static string MapTodoLabel(TodoStatus status) => status switch
    {
        TodoStatus.InProgress => "Todo • in progress",
        TodoStatus.Pending => "Todo • pending",
        TodoStatus.Blocked => "Todo • blocked",
        TodoStatus.Done => "Todo • done",
        _ => "Todo"
    };

    private static string MapTicketLabel(TicketStatus status) => status switch
    {
        TicketStatus.InProgress => "Ticket • in progress",
        TicketStatus.New => "Ticket • new",
        TicketStatus.Blocked => "Ticket • blocked",
        TicketStatus.Completed => "Ticket • completed",
        _ => "Ticket"
    };

    private static string TrimPreview(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..(maxLength - 3)].TrimEnd() + "...";
    }

    private static decimal ScoreRecency(DateTime? updatedUtc)
    {
        if (!updatedUtc.HasValue)
        {
            return 0m;
        }

        var timestamp = updatedUtc.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(updatedUtc.Value, DateTimeKind.Utc)
            : updatedUtc.Value.ToUniversalTime();
        var age = DateTime.UtcNow - timestamp;

        return age.TotalDays switch
        {
            <= 1 => 6m,
            <= 7 => 4m,
            <= 30 => 2m,
            <= 90 => 1m,
            _ => 0m
        };
    }

    private static string NormalizePhrase(string? value) => string.Join(' ', EnumerateTokens(value, keepStopWords: true));

    private static string GetPackGoalLabel(ContextPackGoal goal) => goal switch
    {
        ContextPackGoal.Debugging => "Debugging",
        ContextPackGoal.Delivery => "Delivery",
        ContextPackGoal.Research => "Research",
        ContextPackGoal.Architecture => "Architecture",
        _ => "General"
    };

    private static decimal GoalBoost(ContextPackGoal goal, ContextRecordKind kind, SourceKind? sourceKind = null, MemoryKind memoryKind = MemoryKind.Fact)
        => goal switch
        {
            ContextPackGoal.Debugging when kind == ContextRecordKind.Memory && (sourceKind == SourceKind.DebugSession || memoryKind == MemoryKind.Incident) => 4m,
            ContextPackGoal.Debugging when kind is ContextRecordKind.Ticket or ContextRecordKind.CodeGraphFile or ContextRecordKind.CodeGraphNode => 3m,
            ContextPackGoal.Delivery when kind is ContextRecordKind.Todo or ContextRecordKind.Ticket => 4m,
            ContextPackGoal.Research when kind == ContextRecordKind.Memory && (sourceKind == SourceKind.Research || memoryKind is MemoryKind.Reference or MemoryKind.Insight) => 4m,
            ContextPackGoal.Architecture when kind == ContextRecordKind.Memory && sourceKind == SourceKind.Architecture => 4m,
            ContextPackGoal.Architecture when kind is ContextRecordKind.CodeGraphProject or ContextRecordKind.CodeGraphFile or ContextRecordKind.CodeGraphNode => 3m,
            _ => 0m
        };

    private static string GoalBoostLabel(ContextPackGoal goal, ContextRecordKind kind)
        => goal == ContextPackGoal.General ? string.Empty : $"{GetPackGoalLabel(goal)} focus";

    private static bool ShouldPreferDurableMemoryLead(string normalizedQuestion, ContextBriefInput input)
    {
        if (input.PackGoal is ContextPackGoal.Architecture or ContextPackGoal.Research)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(normalizedQuestion))
        {
            return false;
        }

        var question = normalizedQuestion.ToLowerInvariant();
        return question.StartsWith("how ", StringComparison.Ordinal)
               || question.StartsWith("why ", StringComparison.Ordinal)
               || question.Contains(" use ", StringComparison.Ordinal)
               || question.Contains(" integrate", StringComparison.Ordinal)
               || question.Contains(" oauth", StringComparison.Ordinal)
               || question.Contains(" entra", StringComparison.Ordinal)
               || question.Contains(" architecture", StringComparison.Ordinal)
               || question.Contains(" flow", StringComparison.Ordinal);
    }

    private static decimal DurableMemoryQuestionBoost(MemoryEntry memory)
    {
        decimal boost = 0m;
        if (memory.VerificationStatus == MemoryVerificationStatus.Verified)
        {
            boost += 2m;
        }

        if (memory.IsPinned)
        {
            boost += 1m;
        }

        if (memory.SourceKind is SourceKind.Architecture or SourceKind.Research or SourceKind.ManualNote)
        {
            boost += 2.5m;
        }

        if (memory.Kind is MemoryKind.Decision or MemoryKind.Reference or MemoryKind.Insight)
        {
            boost += 1.5m;
        }

        return boost;
    }

    private static decimal BuildScopedMemoryBoost(MemoryEntry memory, ContextBriefInput input)
    {
        decimal boost = 0m;
        if (input.WingId.HasValue && memory.WingId == input.WingId.Value)
        {
            boost += 2m;
        }

        if (input.RoomId.HasValue && memory.RoomId == input.RoomId.Value)
        {
            boost += 2m;
        }

        if (input.Kind.HasValue && memory.Kind == input.Kind.Value)
        {
            boost += 1.5m;
        }

        if (!string.IsNullOrWhiteSpace(input.Tag))
        {
            var tagSlug = SlugUtility.CreateSlug(input.Tag);
            if (memory.MemoryTags.Any(x => x.Tag!.Slug == tagSlug))
            {
                boost += 1.5m;
            }
        }

        return boost;
    }

    private static string BuildScopedMemoryBoostLabel(ContextBriefInput input)
    {
        return input.WingId.HasValue || input.RoomId.HasValue || input.Kind.HasValue || !string.IsNullOrWhiteSpace(input.Tag)
            ? "Scoped filter"
            : string.Empty;
    }

    private ProjectPreference BuildProjectPreference(
        CodeGraphProject project,
        IReadOnlyCollection<string> tokens,
        IReadOnlyCollection<string> semanticTokens,
        string normalizedQuestion)
    {
        var projectQueryBoost = BuildProjectQueryBoost(project, tokens, semanticTokens, normalizedQuestion);
        var repoAffinityBoost = BuildProjectRepoAffinityBoost(project, tokens, projectQueryBoost > 0m);
        return new ProjectPreference(
            projectQueryBoost,
            projectQueryBoost > 0m ? "Explicit project match" : string.Empty,
            repoAffinityBoost,
            repoAffinityBoost > 0m ? "Current project repo" : repoAffinityBoost < 0m ? "Outside current project" : string.Empty,
            projectQueryBoost >= 5m || repoAffinityBoost > 0m);
    }

    private static decimal BuildProjectQueryBoost(
        CodeGraphProject project,
        IReadOnlyCollection<string> tokens,
        IReadOnlyCollection<string> semanticTokens,
        string normalizedQuestion)
    {
        var projectTokens = Tokenize($"{project.Name} {Path.GetFileName(project.RootPath)}");
        var directMatches = tokens.Intersect(projectTokens, StringComparer.OrdinalIgnoreCase).Count();
        var semanticProjectTokens = ExpandSemanticTokens(projectTokens);
        var semanticMatches = semanticTokens.Intersect(semanticProjectTokens, StringComparer.OrdinalIgnoreCase).Count();
        if (directMatches >= 2 || semanticMatches >= 2)
        {
            return 5m;
        }

        var normalizedProjectText = NormalizePhrase($"{project.Name} {project.RootPath}");
        if (!string.IsNullOrWhiteSpace(normalizedQuestion)
            && (normalizedProjectText.Contains(normalizedQuestion, StringComparison.Ordinal)
                || ScorePhraseSimilarity(normalizedQuestion, normalizedProjectText) >= 0.84m))
        {
            return 5m;
        }

        return directMatches == 1 || semanticMatches == 1 ? 2.5m : 0m;
    }

    private decimal BuildProjectRepoAffinityBoost(CodeGraphProject project, IReadOnlyCollection<string> tokens, bool explicitProjectMatch)
    {
        if (_currentProjectContext is null)
        {
            return 0m;
        }

        var currentProjectHint = tokens.Intersect(_currentProjectContext.Tokens, StringComparer.OrdinalIgnoreCase).Any();
        if (IsWithinCurrentProject(project.RootPath))
        {
            return explicitProjectMatch || currentProjectHint ? 5m : 3m;
        }

        return currentProjectHint && !explicitProjectMatch ? -4m : 0m;
    }

    private bool IsWithinCurrentProject(string? candidatePath)
    {
        if (_currentProjectContext is null || string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        var normalizedCandidate = NormalizePath(candidatePath);
        return normalizedCandidate.StartsWith(_currentProjectContext.RootPath, StringComparison.OrdinalIgnoreCase);
    }

    private static CurrentProjectContext? ResolveCurrentProjectContext(string? contentRootPath)
    {
        if (string.IsNullOrWhiteSpace(contentRootPath))
        {
            return null;
        }

        var repositoryRoot = FindRepositoryRoot(contentRootPath) ?? Path.GetFullPath(contentRootPath);
        var tokenSource = string.Join(' ', new[]
        {
            Path.GetFileName(repositoryRoot),
            Path.GetFileName(Path.GetFullPath(contentRootPath))
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return new CurrentProjectContext(NormalizePath(repositoryRoot), Tokenize(tokenSource));
    }

    private static string? FindRepositoryRoot(string path)
    {
        var current = new DirectoryInfo(Path.GetFullPath(path));
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string NormalizePath(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    private static bool IsExternalOperationsQuery(IReadOnlyCollection<string> tokens)
        => tokens.Count > 0 && tokens.Count(token => ExternalOpsTokens.Contains(token)) >= 2;

    private static bool HasExplicitCodeIntent(IReadOnlyCollection<string> tokens)
    {
        if (tokens.Any(token => StrongCodeIntentTokens.Contains(token)))
        {
            return true;
        }

        return tokens.Any(token => token is "file" or "files")
               && tokens.Any(token => token is "repo" or "repository" or "project" or "code" or "source");
    }

    private static bool IsRepositoryArchitectureQuery(IReadOnlyCollection<string> tokens, string normalizedQuery)
        => tokens.Count(token => RepositoryArchitectureTokens.Contains(token)) >= 2
           || ContainsAnyPhrase(normalizedQuery, RepositoryArchitecturePhrases);

    private static bool IsDirectoryAdminQuery(IReadOnlyCollection<string> tokens)
        => tokens.Count(token => DirectoryAdminTokens.Contains(token)) >= 2;

    private static bool IsLocalSupportQuery(IReadOnlyCollection<string> tokens)
        => tokens.Count(token => LocalSupportTokens.Contains(token)) >= 2;

    private static bool IsRemoteLoggedInUserQuery(IReadOnlyCollection<string> tokens, string normalizedQuery)
        => (tokens.Any(token => token is "logged" or "login" or "logon" or "session" or "sessions" or "quser")
            || normalizedQuery.Contains("logged in", StringComparison.Ordinal)
            || normalizedQuery.Contains("logged into", StringComparison.Ordinal)
            || normalizedQuery.Contains("who is logged", StringComparison.Ordinal)
            || normalizedQuery.Contains("who s logged", StringComparison.Ordinal)
            || normalizedQuery.Contains("who's logged", StringComparison.Ordinal))
           && tokens.Any(token => token is "pc" or "computer" or "computers" or "network" or "remote" or "endpoint");

    private static bool IsWebUiQuery(IReadOnlyCollection<string> tokens)
        => tokens.Count(token => WebUiTokens.Contains(token)) >= 2;

    private static bool IsProductUiChangeQuery(IReadOnlyCollection<string> tokens, string normalizedQuery)
        => tokens.Count(token => ProductUiChangeTokens.Contains(token)) >= 2
           || normalizedQuery.Contains("company address", StringComparison.Ordinal)
           || normalizedQuery.Contains("add location", StringComparison.Ordinal)
           || normalizedQuery.Contains("single location", StringComparison.Ordinal)
           || normalizedQuery.Contains("create new location", StringComparison.Ordinal);

    private static bool IsCloudOpsQuery(IReadOnlyCollection<string> tokens)
        => tokens.Count(token => CloudOpsTokens.Contains(token)) >= 2;

    private static bool IsDesktopAppQuery(IReadOnlyCollection<string> tokens, string normalizedQuery)
        => tokens.Count(token => DesktopAppTokens.Contains(token)) >= 2
           || ContainsAnyPhrase(normalizedQuery, DesktopAppPhrases);

    private static decimal BuildExternalAdminMemoryBoost(MemoryEntry memory)
    {
        var text = string.Join(' ', new[]
        {
            memory.Title,
            memory.Summary,
            memory.Content,
            memory.Wing?.Name,
            memory.Room?.Name,
            string.Join(' ', memory.MemoryTags.Select(x => x.Tag?.Name))
        });
        var matches = Tokenize(text).Count(token => DirectoryDomainTokens.Contains(token));
        if (matches >= 3)
        {
            return 8m;
        }

        if (matches == 2)
        {
            return 5m;
        }

        if (matches == 1)
        {
            return 2m;
        }

        return 0m;
    }

    private static decimal BuildDirectoryAdminMismatchPenalty(MemoryEntry memory)
    {
        var domainMatches = GetDirectoryAdminDomainMatchCount(memory);
        if (domainMatches > 0)
        {
            return 0m;
        }

        var productHintText = string.Join(' ', new[] { memory.Title, memory.Summary, memory.Wing?.Name, memory.Room?.Name });
        var productTokens = Tokenize(productHintText);
        return productTokens.Contains("grey", StringComparer.OrdinalIgnoreCase) || productTokens.Contains("canary", StringComparer.OrdinalIgnoreCase)
            ? -48m
            : -18m;
    }

    private static decimal BuildProductUiFileBoost(CodeGraphFile file, IReadOnlyCollection<CodeGraphNode> nodes, bool strongProjectAffinity, IReadOnlyCollection<string> tokens)
    {
        if (!strongProjectAffinity)
        {
            return 0m;
        }

        var relativePath = file.RelativePath ?? string.Empty;
        var normalizedPath = NormalizePhrase(relativePath);
        var boost = 0m;
        var hasBusinessFormSignals = tokens.Any(token => token is "location" or "company" or "address" or "client" or "checkbox" or "slider");
        var explicitStaticSiteQuery = tokens.Any(token => token is "website" or "web" or "site" or "homepage" or "css" or "frontend" or "landing");
        var explicitBusinessFormPath =
            normalizedPath.Contains("clients", StringComparison.Ordinal)
            || normalizedPath.Contains("clientmanagement", StringComparison.Ordinal)
            || normalizedPath.Contains("customization", StringComparison.Ordinal)
            || normalizedPath.Contains("edit", StringComparison.Ordinal)
            || normalizedPath.Contains("location", StringComparison.Ordinal);

        if (relativePath.StartsWith("src/GreyCanary.Platform/", StringComparison.OrdinalIgnoreCase))
        {
            boost += 4m;
        }

        if (hasBusinessFormSignals && normalizedPath.Contains("client", StringComparison.Ordinal))
        {
            boost += 6m;
        }

        if (hasBusinessFormSignals && explicitBusinessFormPath)
        {
            boost += 10m;
        }

        if (hasBusinessFormSignals && normalizedPath.Contains("controller", StringComparison.Ordinal))
        {
            boost += 3m;
        }

        if (hasBusinessFormSignals
            && (normalizedPath.Contains("clientscontroller", StringComparison.Ordinal)
                || normalizedPath.Contains("locationscontroller", StringComparison.Ordinal)))
        {
            boost += 6m;
        }

        if (hasBusinessFormSignals
            && ((normalizedPath.Contains("clients", StringComparison.Ordinal) && normalizedPath.Contains("edit", StringComparison.Ordinal))
                || normalizedPath.Contains("location", StringComparison.Ordinal)))
        {
            boost += 4m;
        }

        if (hasBusinessFormSignals && normalizedPath.Contains("test", StringComparison.Ordinal))
        {
            boost += 4m;
        }

        if (normalizedPath.Contains("viewmodel", StringComparison.Ordinal)
            || normalizedPath.Contains("client", StringComparison.Ordinal)
            || normalizedPath.Contains("controller", StringComparison.Ordinal))
        {
            boost += 2m;
        }

        var relatedNodeText = string.Join(' ',
            nodes.Where(node => node.FileId == file.Id)
                .Take(20)
                .Select(node => $"{node.Label} {node.SecondaryLabel} {node.Metadata}"));
        var relatedNodeTokens = Tokenize(relatedNodeText);
        boost += Math.Min(tokens.Intersect(relatedNodeTokens, StringComparer.OrdinalIgnoreCase).Count() * 0.8m, 4m);

        if (relativePath.StartsWith("website/", StringComparison.OrdinalIgnoreCase))
        {
            boost -= hasBusinessFormSignals && !explicitStaticSiteQuery ? 18m : hasBusinessFormSignals ? 10m : 4m;

            if (hasBusinessFormSignals
                && !explicitStaticSiteQuery
                && (normalizedPath.Contains("assets", StringComparison.Ordinal)
                    || normalizedPath.Contains("site js", StringComparison.Ordinal)
                    || normalizedPath.Contains("package site", StringComparison.Ordinal)))
            {
                boost -= 6m;
            }
        }

        if (relativePath.StartsWith("deploy/", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            boost -= hasBusinessFormSignals && !explicitStaticSiteQuery ? 20m : hasBusinessFormSignals ? 12m : 6m;
        }

        return boost;
    }

    private static decimal BuildProductUiNodeBoost(CodeGraphNode node, bool strongProjectAffinity, IReadOnlyCollection<string> tokens)
    {
        if (!strongProjectAffinity)
        {
            return 0m;
        }

        var path = node.File?.RelativePath ?? string.Empty;
        var searchableText = NormalizePhrase($"{node.Label} {node.SecondaryLabel} {node.Metadata} {path}");
        var boost = 0m;
        var hasBusinessFormSignals = tokens.Any(token => token is "location" or "company" or "address" or "client" or "checkbox" or "slider");
        var explicitStaticSiteQuery = tokens.Any(token => token is "website" or "web" or "site" or "homepage" or "css" or "frontend" or "landing");
        var explicitBusinessFormPath =
            searchableText.Contains("clients", StringComparison.Ordinal)
            || searchableText.Contains("clientmanagement", StringComparison.Ordinal)
            || searchableText.Contains("customization", StringComparison.Ordinal)
            || searchableText.Contains("edit", StringComparison.Ordinal)
            || searchableText.Contains("location", StringComparison.Ordinal);

        if (path.StartsWith("src/GreyCanary.Platform/", StringComparison.OrdinalIgnoreCase))
        {
            boost += 4m;
        }

        boost += Math.Min(tokens.Count(token => searchableText.Contains(token, StringComparison.Ordinal)) * 0.8m, 5m);

        if (hasBusinessFormSignals && explicitBusinessFormPath)
        {
            boost += 8m;
        }

        if (hasBusinessFormSignals
            && (searchableText.Contains("client", StringComparison.Ordinal)
                || searchableText.Contains("location", StringComparison.Ordinal)
                || searchableText.Contains("address", StringComparison.Ordinal)
                || searchableText.Contains("checkbox", StringComparison.Ordinal)))
        {
            boost += 5m;
        }

        if (hasBusinessFormSignals
            && (searchableText.Contains("clientscontroller", StringComparison.Ordinal)
                || searchableText.Contains("locationscontroller", StringComparison.Ordinal)))
        {
            boost += 6m;
        }

        if (node.NodeType is CodeGraphNodeType.Type or CodeGraphNodeType.Method or CodeGraphNodeType.Property)
        {
            boost += 1.5m;
        }

        if (node.NodeType == CodeGraphNodeType.File)
        {
            boost -= 2m;
        }

        if (path.StartsWith("website/", StringComparison.OrdinalIgnoreCase))
        {
            boost -= hasBusinessFormSignals && !explicitStaticSiteQuery ? 22m : hasBusinessFormSignals ? 10m : 4m;

            if (hasBusinessFormSignals
                && !explicitStaticSiteQuery
                && (searchableText.Contains("assets", StringComparison.Ordinal)
                    || searchableText.Contains("site js", StringComparison.Ordinal)
                    || searchableText.Contains("render", StringComparison.Ordinal)))
            {
                boost -= 6m;
            }
        }

        if (path.StartsWith("deploy/", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            boost -= hasBusinessFormSignals && !explicitStaticSiteQuery ? 20m : hasBusinessFormSignals ? 12m : 6m;
        }

        return boost;
    }

    private static int GetDirectoryAdminDomainMatchCount(MemoryEntry memory)
    {
        var text = string.Join(' ', new[]
        {
            memory.Title,
            memory.Summary,
            memory.Content,
            memory.Wing?.Name,
            memory.Room?.Name,
            string.Join(' ', memory.MemoryTags.Select(x => x.Tag?.Name))
        });
        return Tokenize(text).Count(token => DirectoryAdminTokens.Contains(token));
    }

    private static bool IsDirectoryAdminRelevantMemory(MemoryEntry memory, IReadOnlyCollection<string> queryTokens, string normalizedQuery)
    {
        var highSignalText = string.Join(' ', new[]
        {
            memory.Title,
            memory.Summary,
            memory.Content,
            memory.Wing?.Name,
            memory.Room?.Name,
            string.Join(' ', memory.MemoryTags.Select(x => x.Tag?.Name))
        });
        var structuralText = string.Join(' ', new[]
        {
            memory.Title,
            memory.Summary,
            memory.Wing?.Name,
            memory.Room?.Name,
            string.Join(' ', memory.MemoryTags.Select(x => x.Tag?.Name))
        });
        var highSignalTokens = Tokenize(highSignalText);
        var structuralTokens = Tokenize(structuralText);
        var normalizedHighSignalText = NormalizePhrase(highSignalText);
        var queryHasAttributeIntent = queryTokens.Any(token => DirectoryAdminAttributeTokens.Contains(token))
                                     || ContainsAnyPhrase(normalizedQuery, DirectoryAdminAttributePhrases);
        var queryHasAttributeAuditIntent = queryHasAttributeIntent && queryTokens.Any(token => DirectoryAdminAuditActionTokens.Contains(token));
        var memoryHasAttributeSignal = highSignalTokens.Any(token => DirectoryAdminAttributeTokens.Contains(token))
                                       || ContainsAnyPhrase(normalizedHighSignalText, DirectoryAdminAttributePhrases);
        var memoryHasAttributeAuditSignal = memoryHasAttributeSignal && highSignalTokens.Any(token => DirectoryAdminAuditActionTokens.Contains(token));
        var memoryHasBroadInfraSignal = highSignalTokens.Count(token => DirectoryAdminBroadInfraTokens.Contains(token)) >= 2;
        var memoryHasTicketingSignal = structuralTokens.Any(token => TicketingTokens.Contains(token));

        if (memory.Kind == MemoryKind.Task)
        {
            return memoryHasAttributeSignal && !memoryHasTicketingSignal;
        }

        if (memoryHasTicketingSignal && !memoryHasAttributeSignal)
        {
            return false;
        }

        if (queryHasAttributeIntent && !memoryHasAttributeSignal)
        {
            return false;
        }

        if (queryHasAttributeAuditIntent && !memoryHasAttributeAuditSignal)
        {
            return false;
        }

        if (queryHasAttributeIntent && memoryHasBroadInfraSignal && !memoryHasAttributeSignal)
        {
            return false;
        }

        if (memory.Kind == MemoryKind.Task && highSignalTokens.Count(token => DirectoryAdminHighSignalTokens.Contains(token)) == 0)
        {
            return false;
        }

        return highSignalTokens.Count(token => DirectoryAdminHighSignalTokens.Contains(token)) > 0
               || structuralTokens.Count(token => DirectoryAdminTokens.Contains(token)) >= 2;
    }

    private static bool IsDirectoryAdminRelevantSkill(SkillEntry skill, IReadOnlyCollection<string> queryTokens, string normalizedQuery)
    {
        var text = string.Join(' ', new[]
        {
            skill.Name,
            skill.Summary,
            skill.WhenToUse,
            skill.Flow,
            skill.TriggerHintsText
        });
        var tokens = Tokenize(text);
        var normalizedSkillText = NormalizePhrase(text);
        var queryHasAttributeIntent = queryTokens.Any(token => DirectoryAdminAttributeTokens.Contains(token))
                                     || ContainsAnyPhrase(normalizedQuery, DirectoryAdminAttributePhrases);
        var queryHasAttributeAuditIntent = queryHasAttributeIntent && queryTokens.Any(token => DirectoryAdminAuditActionTokens.Contains(token));
        var queryExactAttributeTokens = GetDirectoryAdminExactAttributeFamilies(queryTokens, normalizedQuery);
        var skillHasAttributeSignal = tokens.Any(token => DirectoryAdminAttributeTokens.Contains(token))
                                      || ContainsAnyPhrase(normalizedSkillText, DirectoryAdminAttributePhrases);
        var skillHasAttributeAuditSignal = skillHasAttributeSignal && tokens.Any(token => DirectoryAdminAuditActionTokens.Contains(token));
        var skillExactAttributeTokens = GetDirectoryAdminExactAttributeFamilies(tokens, normalizedSkillText);

        if (queryHasAttributeIntent && !skillHasAttributeSignal)
        {
            return false;
        }

        if (queryHasAttributeAuditIntent && !skillHasAttributeAuditSignal)
        {
            return false;
        }

        if (queryExactAttributeTokens.Count > 0 && !skillExactAttributeTokens.Overlaps(queryExactAttributeTokens))
        {
            return false;
        }

        return tokens.Count(token => DirectoryAdminHighSignalTokens.Contains(token)) > 0
               || tokens.Count(token => DirectoryAdminTokens.Contains(token)) >= 2;
    }

    private static bool IsGenericAutomationRelevantMemory(MemoryEntry memory)
    {
        var text = string.Join(' ', new[]
        {
            memory.Title,
            memory.Summary,
            memory.Content,
            memory.Wing?.Name,
            memory.Room?.Name,
            string.Join(' ', memory.MemoryTags.Select(x => x.Tag?.Name))
        });
        var tokens = Tokenize(text);
        var productTokens = new[] { "grey", "canary", "focus", "sophos", "dstc" };
        return tokens.Count(token => GenericAutomationHighSignalTokens.Contains(token)) >= 2
               && productTokens.Count(token => tokens.Contains(token)) == 0;
    }

    private static bool IsFileComparisonRelevantMemory(MemoryEntry memory)
    {
        var text = string.Join(' ', new[]
        {
            memory.Title,
            memory.Summary,
            memory.Content,
            memory.Wing?.Name,
            memory.Room?.Name,
            string.Join(' ', memory.MemoryTags.Select(x => x.Tag?.Name))
        });
        var normalizedText = NormalizePhrase(text);
        var tokens = Tokenize(text);
        return HasFileComparisonIntent(tokens, normalizedText)
               || (tokens.Any(token => token is "powershell" or "script")
                   && tokens.Any(token => token is "hash" or "hashes" or "compare" or "diff" or "difference" or "differences"));
    }

    private static bool IsProjectHistoryRelevantMemory(MemoryEntry memory, IReadOnlyCollection<string> focusTokens)
    {
        var text = string.Join(' ', new[]
        {
            memory.Title,
            memory.Summary,
            memory.Content,
            memory.Wing?.Name,
            memory.Room?.Name,
            string.Join(' ', memory.MemoryTags.Select(x => x.Tag?.Name))
        });
        var tokens = Tokenize(text);
        return focusTokens.Count > 0 && focusTokens.Count(token => tokens.Contains(token)) >= 2;
    }

    private static bool HasWmiDiagnosticIntent(IReadOnlyCollection<string> tokens, string normalizedQuery)
        => (tokens.Any(token => token is "wmi" or "cim" or "winmgmt" or "winrm")
            || normalizedQuery.Contains("get ciminstance", StringComparison.Ordinal)
            || normalizedQuery.Contains("get wmiobject", StringComparison.Ordinal))
           && tokens.Any(token => token is "pc" or "computer" or "computers" or "windows" or "local");

    private static bool HasPortCheckIntent(IReadOnlyCollection<string> tokens, string normalizedQuery)
        => (tokens.Any(token => token is "port" or "ports" or "tcp" or "udp" or "listener" or "listeners" or "socket" or "sockets")
            || normalizedQuery.Contains("test netconnection", StringComparison.Ordinal)
            || normalizedQuery.Contains("netstat", StringComparison.Ordinal))
           && tokens.Any(token => token is "open" or "check" or "listen" or "listening" or "reachable" or "connect" or "connection" or "test");

    private static bool IsWmiDiagnosticRelevantMemory(MemoryEntry memory)
    {
        var text = string.Join(' ', new[]
        {
            memory.Title,
            memory.Summary,
            memory.Content,
            memory.Wing?.Name,
            memory.Room?.Name,
            string.Join(' ', memory.MemoryTags.Select(x => x.Tag?.Name))
        });
        var normalizedText = NormalizePhrase(text);
        var tokens = Tokenize(text);
        return (tokens.Any(token => token is "wmi" or "cim" or "winmgmt" or "winrm")
                || normalizedText.Contains("get ciminstance", StringComparison.Ordinal)
                || normalizedText.Contains("get wmiobject", StringComparison.Ordinal))
               && tokens.Any(token => token is "windows" or "pc" or "computer" or "computers");
    }

    private static bool IsWmiDiagnosticRelevantSkill(SkillEntry skill)
    {
        var text = string.Join(' ', new[]
        {
            skill.Name,
            skill.Summary,
            skill.WhenToUse,
            skill.Flow,
            skill.TriggerHintsText
        });
        var normalizedText = NormalizePhrase(text);
        var tokens = Tokenize(text);
        return (tokens.Any(token => token is "wmi" or "cim" or "winmgmt" or "winrm")
                || normalizedText.Contains("get ciminstance", StringComparison.Ordinal)
                || normalizedText.Contains("get wmiobject", StringComparison.Ordinal))
               && tokens.Any(token => token is "windows" or "pc" or "computer" or "computers");
    }

    private static bool IsPortCheckRelevantMemory(MemoryEntry memory)
    {
        var text = string.Join(' ', new[]
        {
            memory.Title,
            memory.Summary,
            memory.Content,
            memory.Wing?.Name,
            memory.Room?.Name,
            string.Join(' ', memory.MemoryTags.Select(x => x.Tag?.Name))
        });
        var normalizedText = NormalizePhrase(text);
        var tokens = Tokenize(text);
        var hasPortSignal =
            tokens.Any(token => token is "tcp" or "udp" or "socket" or "sockets")
            || normalizedText.Contains("test netconnection", StringComparison.Ordinal)
            || normalizedText.Contains("get nettcpconnection", StringComparison.Ordinal)
            || normalizedText.Contains("tcpclient", StringComparison.Ordinal)
            || normalizedText.Contains("system net sockets", StringComparison.Ordinal)
            || normalizedText.Contains("netstat", StringComparison.Ordinal);
        var hasCheckSemantics =
            tokens.Any(token => token is "powershell" or "script" or "check" or "test" or "reachable" or "listening" or "connection" or "connect")
            || normalizedText.Contains("test netconnection", StringComparison.Ordinal)
            || normalizedText.Contains("get nettcpconnection", StringComparison.Ordinal)
            || normalizedText.Contains("check whether", StringComparison.Ordinal);
        var hasExcludeSignals =
            tokens.Any(token => token is "admt" or "domain" or "migration" or "migrate" or "trap" or "snmp");
        return hasPortSignal && hasCheckSemantics && !hasExcludeSignals;
    }

    private static bool IsPortCheckRelevantSkill(SkillEntry skill)
    {
        var text = string.Join(' ', new[]
        {
            skill.Name,
            skill.Summary,
            skill.WhenToUse,
            skill.Flow,
            skill.TriggerHintsText
        });
        var normalizedText = NormalizePhrase(text);
        var tokens = Tokenize(text);
        return (tokens.Any(token => token is "port" or "ports" or "tcp" or "udp" or "listener" or "listeners" or "socket" or "sockets")
                || normalizedText.Contains("test netconnection", StringComparison.Ordinal)
                || normalizedText.Contains("netstat", StringComparison.Ordinal))
               && tokens.Any(token => token is "open" or "listen" or "listening" or "reachable" or "connect" or "connection" or "firewall");
    }

    private static bool IsWindowsServicingRelevantMemory(MemoryEntry memory)
    {
        var text = string.Join(' ', new[]
        {
            memory.Title,
            memory.Summary,
            memory.Content,
            memory.Wing?.Name,
            memory.Room?.Name,
            string.Join(' ', memory.MemoryTags.Select(x => x.Tag?.Name))
        });
        var normalizedText = NormalizePhrase(text);
        var tokens = Tokenize(text);
        return tokens.Any(token => token is "dism" or "sfc" or "scanhealth" or "restorehealth" or "checkhealth")
               || normalizedText.Contains("cleanup image", StringComparison.Ordinal)
               || normalizedText.Contains("component store", StringComparison.Ordinal)
               || normalizedText.Contains("image health", StringComparison.Ordinal);
    }

    private static bool IsWindowsServicingRelevantSkill(SkillEntry skill)
    {
        var text = string.Join(' ', new[]
        {
            skill.Name,
            skill.Summary,
            skill.WhenToUse,
            skill.Flow,
            skill.TriggerHintsText
        });
        var normalizedText = NormalizePhrase(text);
        var tokens = Tokenize(text);
        return tokens.Any(token => token is "dism" or "sfc" or "scanhealth" or "restorehealth" or "checkhealth")
               || normalizedText.Contains("cleanup image", StringComparison.Ordinal)
               || normalizedText.Contains("component store", StringComparison.Ordinal)
               || normalizedText.Contains("image health", StringComparison.Ordinal)
               || normalizedText.Contains("windows servicing", StringComparison.Ordinal);
    }

    private static bool IsWindowsUpdateRelevantMemory(MemoryEntry memory)
    {
        var text = string.Join(' ', new[]
        {
            memory.Title,
            memory.Summary,
            memory.Content,
            memory.Wing?.Name,
            memory.Room?.Name,
            string.Join(' ', memory.MemoryTags.Select(x => x.Tag?.Name))
        });
        var normalizedText = NormalizePhrase(text);
        var tokens = Tokenize(text);
        return tokens.Any(token => token is "pswindowsupdate" or "wuapi" or "wsus" or "kb" or "hotfix" or "hotfixes")
               || normalizedText.Contains("windows update", StringComparison.Ordinal)
               || normalizedText.Contains("microsoft update", StringComparison.Ordinal)
               || normalizedText.Contains("missing updates", StringComparison.Ordinal)
               || normalizedText.Contains("available updates", StringComparison.Ordinal)
               || normalizedText.Contains("pending updates", StringComparison.Ordinal);
    }

    private static bool IsWindowsUpdateRelevantSkill(SkillEntry skill)
    {
        var text = string.Join(' ', new[]
        {
            skill.Name,
            skill.Summary,
            skill.WhenToUse,
            skill.Flow,
            skill.TriggerHintsText
        });
        var normalizedText = NormalizePhrase(text);
        var tokens = Tokenize(text);
        return tokens.Any(token => token is "pswindowsupdate" or "wuapi" or "hotfix" or "hotfixes" or "kb" or "wsus")
               || normalizedText.Contains("windows update", StringComparison.Ordinal)
               || normalizedText.Contains("microsoft update", StringComparison.Ordinal)
               || normalizedText.Contains("missing updates", StringComparison.Ordinal)
               || normalizedText.Contains("available updates", StringComparison.Ordinal)
               || normalizedText.Contains("pending updates", StringComparison.Ordinal);
    }

    private static bool IsSoftwareInstallRelevantMemory(MemoryEntry memory)
    {
        var text = string.Join(' ', new[]
        {
            memory.Title,
            memory.Summary,
            memory.Content,
            memory.Wing?.Name,
            memory.Room?.Name,
            string.Join(' ', memory.MemoryTags.Select(x => x.Tag?.Name))
        });
        var normalizedText = NormalizePhrase(text);
        var tokens = Tokenize(text);
        var hasInstallActionSignals =
            tokens.Any(token => token is "download" or "install" or "installer" or "setup" or "msi" or "exe" or "package" or "packages")
            || normalizedText.Contains("silent install", StringComparison.Ordinal)
            || normalizedText.Contains("invoke webrequest", StringComparison.Ordinal)
            || normalizedText.Contains("start process", StringComparison.Ordinal);
        var hasInstallerDeliverySignals =
            tokens.Any(token => token is "website" or "vendor" or "url" or "silent" or "quiet")
            || normalizedText.Contains("vendor website", StringComparison.Ordinal)
            || normalizedText.Contains("download url", StringComparison.Ordinal)
            || normalizedText.Contains("quiet install", StringComparison.Ordinal);
        var hasInternalProductSignals =
            tokens.Any(token => token is "grey" or "canary" or "focus" or "endpoint" or "token" or "registration" or "callback" or "diagnostics" or "agent");
        return hasInstallActionSignals && hasInstallerDeliverySignals && !hasInternalProductSignals && !tokens.Contains("uninstall");
    }

    private static bool IsSoftwareInstallRelevantSkill(SkillEntry skill)
    {
        var text = string.Join(' ', new[]
        {
            skill.Name,
            skill.Summary,
            skill.WhenToUse,
            skill.Flow,
            skill.TriggerHintsText
        });
        var normalizedText = NormalizePhrase(text);
        var tokens = Tokenize(text);
        var hasInstallSignals =
            tokens.Any(token => token is "download" or "install" or "installer" or "setup" or "msi" or "exe" or "package" or "packages" or "software" or "application" or "website" or "vendor")
            || normalizedText.Contains("silent install", StringComparison.Ordinal)
            || normalizedText.Contains("invoke webrequest", StringComparison.Ordinal)
            || normalizedText.Contains("start process", StringComparison.Ordinal);
        var hasInternalProductSignals =
            tokens.Any(token => token is "grey" or "canary" or "focus" or "endpoint" or "token" or "registration" or "callback" or "diagnostics" or "agent");
        return hasInstallSignals && !hasInternalProductSignals && !tokens.Contains("uninstall");
    }

    private static bool IsGenericAutomationRelevantSkill(SkillEntry skill, IReadOnlyCollection<string> queryTokens, string normalizedQuery)
    {
        var text = string.Join(' ', new[]
        {
            skill.Name,
            skill.Summary,
            skill.WhenToUse,
            skill.Flow,
            skill.TriggerHintsText
        });
        var tokens = Tokenize(text);
        var queryHasFileComparisonIntent = HasFileComparisonIntent(queryTokens, normalizedQuery);
        var queryHasPortCheckIntent = HasPortCheckIntent(queryTokens, normalizedQuery);
        var skillHasFileComparisonIntent = HasFileComparisonIntent(tokens, NormalizePhrase(text));
        var skillHasPortCheckIntent = HasPortCheckIntent(tokens, NormalizePhrase(text));
        if (queryHasFileComparisonIntent && !skillHasFileComparisonIntent)
        {
            return false;
        }

        if (queryHasPortCheckIntent && !skillHasPortCheckIntent)
        {
            return false;
        }

        return skillHasFileComparisonIntent
            || skillHasPortCheckIntent
            || tokens.Count(token => GenericAutomationHighSignalTokens.Contains(token)) >= 2;
    }

    private static bool IsLocalSupportRelevantMemory(MemoryEntry memory)
    {
        var text = string.Join(' ', new[]
        {
            memory.Title,
            memory.Summary,
            memory.Content,
            memory.Wing?.Name,
            memory.Room?.Name,
            string.Join(' ', memory.MemoryTags.Select(x => x.Tag?.Name))
        });
        var tokens = Tokenize(text);
        return tokens.Count(token => LocalSupportTokens.Contains(token)) >= 2
               && tokens.Any(token => LocalSupportHighSignalTokens.Contains(token));
    }

    private static bool IsLocalSupportRelevantSkill(SkillEntry skill)
    {
        var text = string.Join(' ', new[]
        {
            skill.Name,
            skill.Summary,
            skill.WhenToUse,
            skill.Flow,
            skill.TriggerHintsText
        });
        var tokens = Tokenize(text);
        return tokens.Count(token => LocalSupportTokens.Contains(token)) >= 2
               && tokens.Any(token => LocalSupportHighSignalTokens.Contains(token));
    }

    private static bool IsWebUiRelevantMemory(MemoryEntry memory)
    {
        if (memory.Kind == MemoryKind.Task)
        {
            return false;
        }

        var text = string.Join(' ', new[]
        {
            memory.Title,
            memory.Summary,
            memory.Content,
            memory.Wing?.Name,
            memory.Room?.Name,
            string.Join(' ', memory.MemoryTags.Select(x => x.Tag?.Name))
        });
        var tokens = Tokenize(text);
        return tokens.Count(token => WebUiHighSignalTokens.Contains(token)) >= 2;
    }

    private static bool IsWebUiRelevantSkill(SkillEntry skill)
    {
        var text = string.Join(' ', new[]
        {
            skill.Name,
            skill.Summary,
            skill.WhenToUse,
            skill.Flow,
            skill.TriggerHintsText
        });
        var tokens = Tokenize(text);
        return tokens.Count(token => WebUiHighSignalTokens.Contains(token)) >= 2;
    }

    private static bool IsProductUiRelevantSkill(SkillEntry skill)
    {
        var text = string.Join(' ', new[]
        {
            skill.Name,
            skill.Summary,
            skill.WhenToUse,
            skill.Flow,
            skill.TriggerHintsText
        });
        var tokens = Tokenize(text);
        var normalizedText = NormalizePhrase(text);
        var hasUiSignals = tokens.Count(token => WebUiHighSignalTokens.Contains(token)) >= 1
                           || tokens.Count(token => ProductUiChangeTokens.Contains(token)) >= 2
                           || normalizedText.Contains("web application", StringComparison.Ordinal);
        var hasOpsNoise = tokens.Any(token => token is "install" or "installer" or "windows" or "update" or "updates" or "telemetry" or "azure" or "cloud");
        return hasUiSignals && !hasOpsNoise;
    }

    private static bool IsCloudOpsRelevantMemory(MemoryEntry memory)
    {
        var text = string.Join(' ', new[]
        {
            memory.Title,
            memory.Summary,
            memory.Content,
            memory.Wing?.Name,
            memory.Room?.Name,
            string.Join(' ', memory.MemoryTags.Select(x => x.Tag?.Name))
        });
        var tokens = Tokenize(text);
        return tokens.Count(token => CloudOpsHighSignalTokens.Contains(token)) >= 2;
    }

    private static bool IsCloudOpsRelevantSkill(SkillEntry skill)
    {
        var text = string.Join(' ', new[]
        {
            skill.Name,
            skill.Summary,
            skill.WhenToUse,
            skill.Flow,
            skill.TriggerHintsText
        });
        var tokens = Tokenize(text);
        return tokens.Count(token => CloudOpsSkillTokens.Contains(token)) >= 2;
    }

    private static bool IsDesktopAppRelevantMemory(MemoryEntry memory)
    {
        var text = string.Join(' ', new[]
        {
            memory.Title,
            memory.Summary,
            memory.Content,
            memory.Wing?.Name,
            memory.Room?.Name,
            string.Join(' ', memory.MemoryTags.Select(x => x.Tag?.Name))
        });
        var tokens = Tokenize(text);
        var normalizedText = NormalizePhrase(text);
        return tokens.Count(token => DesktopAppSkillTokens.Contains(token)) >= 2
               || ContainsAnyPhrase(normalizedText, DesktopAppPhrases);
    }

    private static bool IsDesktopAppRelevantSkill(SkillEntry skill)
    {
        var text = string.Join(' ', new[]
        {
            skill.Name,
            skill.Summary,
            skill.WhenToUse,
            skill.Flow,
            skill.TriggerHintsText
        });
        var tokens = Tokenize(text);
        var normalizedText = NormalizePhrase(text);
        return tokens.Count(token => DesktopAppSkillTokens.Contains(token)) >= 2
               || ContainsAnyPhrase(normalizedText, DesktopAppPhrases);
    }

    private static bool IsRepositoryArchitectureRelevantMemory(MemoryEntry memory)
    {
        var text = string.Join(' ', new[]
        {
            memory.Title,
            memory.Summary,
            memory.Content,
            memory.Wing?.Name,
            memory.Room?.Name,
            string.Join(' ', memory.MemoryTags.Select(x => x.Tag?.Name))
        });
        var tokens = Tokenize(text);
        return tokens.Count(token => RepositoryArchitectureTokens.Contains(token)) >= 2;
    }

    private static bool IsRepositoryArchitectureRelevantSkill(SkillEntry skill)
    {
        var text = string.Join(' ', new[]
        {
            skill.Name,
            skill.Summary,
            skill.WhenToUse,
            skill.Flow,
            skill.TriggerHintsText
        });
        var tokens = Tokenize(text);
        return tokens.Count(token => RepositoryArchitectureTokens.Contains(token)) >= 2;
    }

    private bool IsCurrentProjectRelevantMemory(MemoryEntry memory)
    {
        if (_currentProjectContext is null)
        {
            return false;
        }

        var text = string.Join(' ', new[]
        {
            memory.Title,
            memory.Summary,
            memory.Content,
            memory.Wing?.Name,
            memory.Room?.Name,
            string.Join(' ', memory.MemoryTags.Select(x => x.Tag?.Name))
        });
        var tokens = Tokenize(text);
        return _currentProjectContext.Tokens.Count(token => tokens.Contains(token)) >= 2;
    }

    private bool HasCurrentProjectHint(IReadOnlyCollection<string> tokens)
    {
        if (_currentProjectContext is null)
        {
            return false;
        }

        return _currentProjectContext.Tokens.Count(token => tokens.Contains(token)) >= 1;
    }

    private static bool IsCodeRelevantMemory(MemoryEntry memory)
    {
        var text = string.Join(' ', new[]
        {
            memory.Title,
            memory.Summary,
            memory.Wing?.Name,
            memory.Room?.Name,
            string.Join(' ', memory.MemoryTags.Select(x => x.Tag?.Name))
        });
        var tokens = Tokenize(text);
        return tokens.Count(token => CodeMemoryTokens.Contains(token)) >= 2;
    }

    private static HashSet<string> Tokenize(string? value, bool keepStopWords = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var tokens = EnumerateTokens(value, keepStopWords)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return tokens.Count == 0 && !keepStopWords
            ? Tokenize(value, keepStopWords: true)
            : tokens;
    }

    private static HashSet<string> GetDirectoryAdminExactAttributeFamilies(IReadOnlyCollection<string> tokens, string normalizedText)
    {
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (tokens.Any(token => token.Equals("title", StringComparison.OrdinalIgnoreCase)))
        {
            families.Add("title");
        }

        if (tokens.Any(token => token.Equals("department", StringComparison.OrdinalIgnoreCase)))
        {
            families.Add("department");
        }

        if (tokens.Any(token => token.Equals("phone", StringComparison.OrdinalIgnoreCase) || token.Equals("telephone", StringComparison.OrdinalIgnoreCase)))
        {
            families.Add("phone");
        }

        if (tokens.Any(token => token.Equals("proxyaddresses", StringComparison.OrdinalIgnoreCase))
            || normalizedText.Contains("proxy addresses", StringComparison.Ordinal))
        {
            families.Add("proxyaddresses");
        }

        if (tokens.Any(token => token.Equals("upn", StringComparison.OrdinalIgnoreCase) || token.Equals("userprincipalname", StringComparison.OrdinalIgnoreCase))
            || normalizedText.Contains("user principal name", StringComparison.Ordinal))
        {
            families.Add("upn");
        }

        if (tokens.Any(token => token.Equals("mailnickname", StringComparison.OrdinalIgnoreCase))
            || normalizedText.Contains("mail nickname", StringComparison.Ordinal))
        {
            families.Add("mailnickname");
        }

        if (tokens.Any(token => token.Equals("password", StringComparison.OrdinalIgnoreCase)
                                || token.Equals("passwords", StringComparison.OrdinalIgnoreCase)
                                || token.Equals("expiry", StringComparison.OrdinalIgnoreCase)
                                || token.Equals("expiring", StringComparison.OrdinalIgnoreCase)
                                || token.Equals("expires", StringComparison.OrdinalIgnoreCase)
                                || token.Equals("expired", StringComparison.OrdinalIgnoreCase))
            || normalizedText.Contains("password expiry", StringComparison.Ordinal)
            || normalizedText.Contains("password expires", StringComparison.Ordinal)
            || normalizedText.Contains("password expiring", StringComparison.Ordinal)
            || normalizedText.Contains("msds userpasswordexpirytimecomputed", StringComparison.Ordinal))
        {
            families.Add("password-expiry");
        }

        return families;
    }

    private static bool HasFileComparisonIntent(IReadOnlyCollection<string> tokens, string normalizedText)
    {
        var hasComparisonSignal = tokens.Any(token => token is "compare" or "diff" or "difference" or "differences")
                                  || normalizedText.Contains("compare object", StringComparison.Ordinal)
                                  || normalizedText.Contains("compare two folders", StringComparison.Ordinal)
                                  || normalizedText.Contains("folder diff", StringComparison.Ordinal)
                                  || normalizedText.Contains("file diff", StringComparison.Ordinal);
        var hasFileSystemSignal = tokens.Any(token => token is "file" or "files" or "folder" or "folders" or "directory" or "directories");
        return hasComparisonSignal && hasFileSystemSignal;
    }

    private static IEnumerable<string> EnumerateTokens(string? value, bool keepStopWords = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return WordRegex()
            .Matches(value.ToLowerInvariant())
            .Select(static x => x.Value)
            .Where(token => token.Length > 2 || PreservedShortTokens.Contains(token))
            .Where(token => keepStopWords || !StopWords.Contains(token));
    }

    private static HashSet<string> ExpandSemanticTokens(IEnumerable<string> tokens)
    {
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            var normalized = StemToken(token);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            expanded.Add(normalized);
            if (!SemanticAliases.TryGetValue(normalized, out var aliases))
            {
                continue;
            }

            foreach (var alias in aliases)
            {
                expanded.Add(StemToken(alias));
            }
        }

        return expanded;
    }

    private static SemanticQueryProfile BuildSemanticQueryProfile(
        string question,
        IReadOnlyCollection<string> tokens,
        IReadOnlyCollection<string> semanticTokens)
    {
        var normalizedQuestion = NormalizePhrase(question);
        var conceptTokens = BuildSemanticConceptTokens(normalizedQuestion, semanticTokens);
        var features = BuildSemanticFeatureVector(normalizedQuestion, tokens, semanticTokens, conceptTokens);
        return new SemanticQueryProfile(
            normalizedQuestion,
            conceptTokens,
            features,
            ComputeVectorMagnitude(features));
    }

    private static SemanticFieldSignal ScoreSemanticField(
        string normalizedField,
        IReadOnlyCollection<string> fieldTokens,
        IReadOnlyCollection<string> fieldSemanticTokens,
        SemanticQueryProfile semanticProfile)
    {
        if (string.IsNullOrWhiteSpace(normalizedField))
        {
            return SemanticFieldSignal.None;
        }

        var fieldConceptTokens = BuildSemanticConceptTokens(normalizedField, fieldSemanticTokens);
        var sharedQueryTerms = semanticProfile.ConceptTokens
            .Intersect(fieldConceptTokens, StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();

        var features = BuildSemanticFeatureVector(normalizedField, fieldTokens, fieldSemanticTokens, fieldConceptTokens);
        var magnitude = ComputeVectorMagnitude(features);
        if (magnitude <= 0d)
        {
            return sharedQueryTerms.Length == 0
                ? SemanticFieldSignal.None
                : new SemanticFieldSignal(0m, sharedQueryTerms);
        }

        var cosineSimilarity = ComputeCosineSimilarity(semanticProfile.Features, semanticProfile.Magnitude, features, magnitude);
        var conceptCoverage = semanticProfile.ConceptTokens.Count == 0
            ? 0d
            : (double)sharedQueryTerms.Length / Math.Min(6, semanticProfile.ConceptTokens.Count);
        var phrasing = (double)ScorePhraseSimilarity(semanticProfile.NormalizedQuestion, normalizedField);
        var similarity = Math.Round((decimal)Math.Min(1d, (cosineSimilarity * 0.65d) + (conceptCoverage * 0.20d) + (phrasing * 0.15d)), 4);
        return similarity <= 0m && sharedQueryTerms.Length == 0
            ? SemanticFieldSignal.None
            : new SemanticFieldSignal(similarity, sharedQueryTerms);
    }

    private static IReadOnlyCollection<string> BuildSemanticConceptTokens(string normalizedText, IEnumerable<string> semanticTokens)
    {
        var concepts = new HashSet<string>(semanticTokens.Select(StemToken), StringComparer.OrdinalIgnoreCase);
        foreach (var bigram in EnumerateSemanticBigrams(normalizedText))
        {
            concepts.Add(bigram);
        }

        return concepts;
    }

    private static IReadOnlyDictionary<int, double> BuildSemanticFeatureVector(
        string normalizedText,
        IEnumerable<string> tokens,
        IEnumerable<string> semanticTokens,
        IReadOnlyCollection<string> conceptTokens)
    {
        var features = new Dictionary<int, double>();
        foreach (var token in tokens.Select(StemToken))
        {
            AddSemanticFeature(features, $"tok:{token}", 1d);
        }

        foreach (var token in semanticTokens.Select(StemToken))
        {
            AddSemanticFeature(features, $"sem:{token}", 0.65d);
        }

        foreach (var concept in conceptTokens)
        {
            AddSemanticFeature(features, $"concept:{concept}", 0.55d);
        }

        foreach (var trigram in EnumerateCharacterTrigrams(normalizedText))
        {
            AddSemanticFeature(features, $"tri:{trigram}", 0.14d);
        }

        return features;
    }

    private static void AddSemanticFeature(IDictionary<int, double> features, string value, double weight)
    {
        if (string.IsNullOrWhiteSpace(value) || weight == 0d)
        {
            return;
        }

        var key = (int)((uint)GetStableHash(value) % 384u);
        features[key] = features.TryGetValue(key, out var existing)
            ? existing + weight
            : weight;
    }

    private static double ComputeVectorMagnitude(IReadOnlyDictionary<int, double> vector)
        => Math.Sqrt(vector.Values.Sum(value => value * value));

    private static double ComputeCosineSimilarity(
        IReadOnlyDictionary<int, double> left,
        double leftMagnitude,
        IReadOnlyDictionary<int, double> right,
        double rightMagnitude)
    {
        if (leftMagnitude <= 0d || rightMagnitude <= 0d)
        {
            return 0d;
        }

        var dotProduct = 0d;
        var (smaller, larger) = left.Count <= right.Count ? (left, right) : (right, left);
        foreach (var pair in smaller)
        {
            if (larger.TryGetValue(pair.Key, out var other))
            {
                dotProduct += pair.Value * other;
            }
        }

        return dotProduct / (leftMagnitude * rightMagnitude);
    }

    private static IEnumerable<string> EnumerateSemanticBigrams(string normalizedText)
    {
        var terms = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < terms.Length - 1; index++)
        {
            yield return $"{StemToken(terms[index])}_{StemToken(terms[index + 1])}";
        }
    }

    private static IEnumerable<string> EnumerateCharacterTrigrams(string normalizedText)
    {
        var compactText = normalizedText.Replace(" ", string.Empty, StringComparison.Ordinal);
        for (var index = 0; index <= compactText.Length - 3; index++)
        {
            yield return compactText.Substring(index, 3);
        }
    }

    private static int GetStableHash(string value)
    {
        unchecked
        {
            var hash = (int)2166136261;
            foreach (var character in value)
            {
                hash ^= character;
                hash *= 16777619;
            }

            return hash;
        }
    }

    private static SemanticScoringPolicy BuildMemorySemanticPolicy(
        ContextPackGoal packGoal,
        bool preferDurableMemoryLead,
        bool repositoryArchitectureQuery,
        bool projectHistoryQuery)
    {
        var preferBroaderSemanticBoost = packGoal is ContextPackGoal.Architecture or ContextPackGoal.Research
            || preferDurableMemoryLead
            || repositoryArchitectureQuery
            || projectHistoryQuery;
        return preferBroaderSemanticBoost
            ? PreferMemorySemanticPolicy
            : ConservativeSemanticPolicy;
    }

    private static SemanticScoringPolicy BuildProjectSemanticPolicy(bool strongAffinity)
        => strongAffinity
            ? StrongAffinitySemanticPolicy
            : ConservativeSemanticPolicy;

    private static string StemToken(string token)
    {
        var normalized = token.Trim().ToLowerInvariant();
        if (normalized.Length > 5 && normalized.EndsWith("ing", StringComparison.Ordinal))
        {
            return normalized[..^3];
        }

        if (normalized.Length > 4 && (normalized.EndsWith("ed", StringComparison.Ordinal) || normalized.EndsWith("es", StringComparison.Ordinal)))
        {
            return normalized[..^2];
        }

        if (normalized.Length > 3 && normalized.EndsWith("s", StringComparison.Ordinal))
        {
            return normalized[..^1];
        }

        return normalized;
    }

    private static decimal ScorePhraseSimilarity(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return 0m;
        }

        var leftTerms = left.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(StemToken)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rightTerms = right.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(StemToken)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (leftTerms.Count == 0 || rightTerms.Count == 0)
        {
            return 0m;
        }

        var overlap = leftTerms.Intersect(rightTerms, StringComparer.OrdinalIgnoreCase).Count();
        var denominator = Math.Max(leftTerms.Count, rightTerms.Count);
        return denominator == 0 ? 0m : (decimal)overlap / denominator;
    }

    private static bool ContainsAnyPhrase(string? normalizedText, IEnumerable<string> phrases)
        => !string.IsNullOrWhiteSpace(normalizedText)
           && phrases.Any(phrase => normalizedText.Contains(phrase, StringComparison.Ordinal));

    [GeneratedRegex("[a-z0-9]+", RegexOptions.Compiled)]
    private static partial Regex WordRegex();

    private static readonly SemanticScoringPolicy ConservativeSemanticPolicy = new(0.68m, 0.45m, 10m, 2);
    private static readonly SemanticScoringPolicy PreferMemorySemanticPolicy = new(0.66m, 0.50m, 12m, 2);
    private static readonly SemanticScoringPolicy StrongAffinitySemanticPolicy = new(0.70m, 0.55m, 12m, 2);

    private sealed record WeightedField(string? Text, string Key, string Label, decimal PerTokenWeight, string ExactReason, string PartialReason);
    private sealed record CurrentProjectContext(string RootPath, IReadOnlyCollection<string> Tokens);
    private sealed record ProjectPreference(decimal ProjectQueryBoost, string ProjectQueryBoostLabel, decimal RepoAffinityBoost, string RepoAffinityBoostLabel, bool HasStrongAffinity);
    private sealed record SemanticQueryProfile(
        string NormalizedQuestion,
        IReadOnlyCollection<string> ConceptTokens,
        IReadOnlyDictionary<int, double> Features,
        double Magnitude);
    private sealed record SemanticFieldSignal(decimal Similarity, IReadOnlyCollection<string> SharedQueryTerms)
    {
        public static SemanticFieldSignal None { get; } = new(0m, Array.Empty<string>());
    }
    private sealed record SemanticScoringPolicy(
        decimal MinimumSimilarity,
        decimal BoostMultiplier,
        decimal MaxBoost,
        int MinimumSharedTerms);
    private sealed record ContextScoreOutcome(
        decimal Score,
        decimal SemanticScore,
        string Reason,
        IReadOnlyCollection<string> MatchedTokens,
        IReadOnlyCollection<ContextMatchFieldHitViewModel> FieldHits,
        IReadOnlyCollection<ContextMatchBoostViewModel> Boosts,
        bool ExactPhraseMatched)
    {
        public static ContextScoreOutcome None { get; } = new(0m, 0m, string.Empty, Array.Empty<string>(), Array.Empty<ContextMatchFieldHitViewModel>(), Array.Empty<ContextMatchBoostViewModel>(), false);
    }
    private sealed record ContextRecordKey(ContextRecordKind Kind, Guid Id);
    private sealed record LinkedContextRef(Guid LinkId, string Label, ContextRecordKind Kind, Guid TargetId);
    private sealed record NormalizedContextLink(ContextRecordKind SourceKind, Guid SourceId, ContextRecordKind TargetKind, Guid TargetId);
}
