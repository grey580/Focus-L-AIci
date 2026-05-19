using System.Text;
using System.Data;
using FocusLAIci.Web.Data;
using FocusLAIci.Web.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FocusLAIci.Web.Services;

public sealed class PalaceService
{
    private static readonly HashSet<string> IgnoredSuggestedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "that",
        "this",
        "with",
        "from",
        "have",
        "were",
        "into",
        "about",
        "after",
        "before",
        "there",
        "their",
        "focus"
    };

    private static readonly IReadOnlyDictionary<string, string> SuggestedTagAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["authentication"] = "auth",
        ["authorize"] = "auth",
        ["authorized"] = "auth",
        ["incident"] = "incidents",
        ["incidents"] = "incidents",
        ["deploy"] = "deployment",
        ["deployment"] = "deployment",
        ["errors"] = "errors",
        ["error"] = "errors",
        ["logging"] = "logs",
        ["logged"] = "logs"
    };

    private readonly FocusMemoryContext _dbContext;
    private readonly ContextService _contextService;
    private readonly IFocusEventPublisher _eventPublisher;
    private readonly FocusDatabaseTargetService? _databaseTargetService;
    private readonly FocusAgentCatalogService _agentCatalogService;
    private readonly RepoSkillCatalogService? _repoSkillCatalogService;
    private readonly ExternalSkillSuggestionService? _externalSkillSuggestionService;
    private readonly PackBuildArchiveService? _packBuildArchiveService;

    public PalaceService(FocusMemoryContext dbContext)
        : this(dbContext, new ContextService(dbContext), NullFocusEventPublisher.Instance, null, null, null, null, null)
    {
    }

    public PalaceService(FocusMemoryContext dbContext, ContextService contextService)
        : this(dbContext, contextService, NullFocusEventPublisher.Instance, null, null, null, null, null)
    {
    }

    public PalaceService(FocusMemoryContext dbContext, ContextService contextService, IFocusEventPublisher eventPublisher)
        : this(dbContext, contextService, eventPublisher, null, null, null, null, null)
    {
    }

    public PalaceService(
        FocusMemoryContext dbContext,
        ContextService contextService,
        IFocusEventPublisher eventPublisher,
        FocusDatabaseTargetService? databaseTargetService,
        FocusAgentCatalogService? agentCatalogService,
        RepoSkillCatalogService? repoSkillCatalogService = null,
        ExternalSkillSuggestionService? externalSkillSuggestionService = null,
        PackBuildArchiveService? packBuildArchiveService = null)
    {
        _dbContext = dbContext;
        _contextService = contextService;
        _eventPublisher = eventPublisher;
        _databaseTargetService = databaseTargetService;
        _agentCatalogService = agentCatalogService ?? new FocusAgentCatalogService();
        _repoSkillCatalogService = repoSkillCatalogService;
        _externalSkillSuggestionService = externalSkillSuggestionService;
        _packBuildArchiveService = packBuildArchiveService;
    }

    public Task<DashboardViewModel> GetDashboardAsync(CancellationToken cancellationToken)
        => GetDashboardAsync((ContextBriefInput?)null, buildContextPack: true, cancellationToken);

    public Task<DashboardViewModel> GetDashboardShellAsync(CancellationToken cancellationToken)
        => GetDashboardAsync((ContextBriefInput?)null, buildContextPack: false, cancellationToken);

    public async Task<DashboardViewModel> GetDashboardAsync(string? contextQuestion, CancellationToken cancellationToken)
        => await GetDashboardAsync(
            string.IsNullOrWhiteSpace(contextQuestion)
                ? null
                : new ContextBriefInput
                {
                    Question = contextQuestion.Trim()
                },
            buildContextPack: true,
            cancellationToken);

    public async Task<DashboardViewModel> GetDashboardAsync(ContextBriefInput? contextInput, CancellationToken cancellationToken)
        => await GetDashboardAsync(contextInput, buildContextPack: true, cancellationToken);

    private async Task<DashboardViewModel> GetDashboardAsync(ContextBriefInput? contextInput, bool buildContextPack, CancellationToken cancellationToken)
    {
        var effectiveContextInput = contextInput ?? new ContextBriefInput();
        var memories = await BuildMemoryQuery()
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.UpdatedUtc)
            .Take(12)
            .ToListAsync(cancellationToken);
        var resurfacingCandidates = await BuildMemoryQuery()
            .OrderByDescending(x => x.ReferenceCount)
            .ThenBy(x => x.LastReferencedUtc ?? DateTime.MinValue)
            .ThenBy(x => x.UpdatedUtc)
            .Take(24)
            .ToListAsync(cancellationToken);
        var currentTodos = await BuildTodoQuery(includeDone: false)
            .Take(5)
            .ToListAsync(cancellationToken);
        var activeTickets = await _dbContext.Tickets
            .AsNoTracking()
            .Include(x => x.SubTickets)
            .Include(x => x.TimeLogs)
            .AsSplitQuery()
            .Where(x => x.ParentTicketId == null && x.Status != TicketStatus.Completed)
            .OrderBy(x => x.Status == TicketStatus.InProgress ? 0 : x.Status == TicketStatus.New ? 1 : 2)
            .ThenByDescending(x => x.UpdatedUtc)
            .Take(5)
            .ToListAsync(cancellationToken);

        var recentMemoryActivity = await _dbContext.Memories
            .AsNoTracking()
            .Where(x => x.LifecycleState == MemoryLifecycleState.Active)
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(4)
            .Select(x => new DashboardActivityViewModel
            {
                Label = "Memory",
                Title = x.Title,
                Detail = x.Summary,
                Url = $"/Palace/Memory/{x.Id}",
                OccurredUtc = x.UpdatedUtc
            })
            .ToListAsync(cancellationToken);

        var recentTodoActivity = await _dbContext.Todos
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(4)
            .Select(x => new DashboardActivityViewModel
            {
                Label = "Todo",
                Title = x.Title,
                Detail = x.Status == TodoStatus.Done ? "Completed work item" : $"Status: {MapDashboardTodoStatusLabel(x.Status)}",
                Url = $"/Todos/Details/{x.Id}",
                OccurredUtc = x.UpdatedUtc
            })
            .ToListAsync(cancellationToken);

        var recentTicketActivity = await _dbContext.TicketActivities
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedUtc)
            .Take(6)
            .Select(x => new DashboardActivityViewModel
            {
                Label = "Ticket activity",
                Title = x.Message,
                Detail = string.IsNullOrWhiteSpace(x.Metadata) ? x.ActivityType : $"{x.ActivityType} • {x.Metadata}",
                Url = $"/Tickets/Details/{x.TicketId}",
                OccurredUtc = x.CreatedUtc
            })
            .ToListAsync(cancellationToken);

        var wings = await BuildWingSummariesAsync(cancellationToken);
        var featuredSkills = await GetFeaturedSkillsAsync(6, cancellationToken);
        var mappedCurrentTodos = currentTodos.Select(MapTodo).ToArray();
        var mappedActiveTickets = activeTickets.Select(MapDashboardTicketSummary).ToArray();
        var mappedRecentMemories = memories.Select(MapCard).ToArray();
        var mappedPinnedMemories = mappedRecentMemories.Where(x => x.IsPinned).ToArray();
        var recentActivity = recentTicketActivity
            .Concat(recentMemoryActivity)
            .Concat(recentTodoActivity)
            .OrderByDescending(x => x.OccurredUtc)
            .Take(8)
            .ToArray();
        var fallbackContext = BuildDashboardFallbackContext(
            effectiveContextInput,
            mappedCurrentTodos,
            mappedActiveTickets,
            recentActivity,
            mappedPinnedMemories,
            mappedRecentMemories);
        var resolvedContextInput = ResolveDashboardContextInput(effectiveContextInput, fallbackContext);
        var contextPack = default(ContextPackViewModel);
        var recommendedAgents = Array.Empty<AgentCardViewModel>();
        var recommendedSkills = Array.Empty<SkillCardViewModel>();

        if (buildContextPack && !string.IsNullOrWhiteSpace(resolvedContextInput.Question))
        {
            var contextPanel = await BuildDashboardContextPanelAsync(resolvedContextInput, cancellationToken);
            contextPack = contextPanel?.ContextPack;
            recommendedAgents = contextPanel?.RecommendedAgents?.ToArray() ?? Array.Empty<AgentCardViewModel>();
            recommendedSkills = contextPanel?.RecommendedSkills?.ToArray() ?? Array.Empty<SkillCardViewModel>();
        }

        var warningItems = BuildDashboardWarnings(
            effectiveContextInput,
            resolvedContextInput,
            fallbackContext,
            contextPack,
            mappedCurrentTodos,
            mappedActiveTickets,
            recentActivity,
            mappedPinnedMemories,
            mappedRecentMemories,
            wings);

        return new DashboardViewModel
        {
            Stats = await BuildStatsAsync(cancellationToken),
            ContextInput = effectiveContextInput,
            ContextPack = contextPack,
            FallbackContext = fallbackContext,
            QuickCaptureInput = new QuickCaptureInput(),
            ActiveTickets = mappedActiveTickets,
            RecentActivity = recentActivity,
            Wings = wings,
            RecentMemories = mappedRecentMemories,
            PinnedMemories = mappedPinnedMemories,
            ResurfacingMemories = BuildResurfacingMemories(resurfacingCandidates, contextPack),
            RecommendedAgents = recommendedAgents,
            FeaturedAgents = _agentCatalogService.GetFeaturedAgents(),
            RecommendedSkills = recommendedSkills,
            FeaturedSkills = featuredSkills,
            CurrentTodos = mappedCurrentTodos,
            MissingContextWarnings = warningItems.Select(x => x.Message).ToArray(),
            MissingContextWarningItems = warningItems,
            SearchExamples =
            [
                "installer reliability",
                "why did we choose local-first memory",
                "frontend browse patterns"
            ]
        };
    }

    public async Task<DashboardContextPanelViewModel?> BuildDashboardContextPanelAsync(ContextBriefInput contextInput, CancellationToken cancellationToken)
    {
        var normalizedInput = CloneContextBriefInput(contextInput);
        if (string.IsNullOrWhiteSpace(normalizedInput.Question))
        {
            return null;
        }

        var contextPack = await _contextService.BuildContextPackAsync(normalizedInput, cancellationToken);
        if (contextPack is null)
        {
            return null;
        }

        if (_externalSkillSuggestionService is not null)
        {
            var externalSkillAlert = await _externalSkillSuggestionService.BuildAlertAsync(contextPack, cancellationToken);
            if (_packBuildArchiveService is not null && contextPack.ArchivedBuildId.HasValue)
            {
                await _packBuildArchiveService.UpdateSuggestedExternalSkillCountAsync(
                    contextPack.ArchivedBuildId.Value,
                    externalSkillAlert.Suggestions.Count,
                    cancellationToken);
            }

            contextPack = new ContextPackViewModel
            {
                ArchivedBuildId = contextPack.ArchivedBuildId,
                Question = contextPack.Question,
                Summary = contextPack.Summary,
                GoalLabel = contextPack.GoalLabel,
                NeedsMoreContext = contextPack.NeedsMoreContext,
                Input = contextPack.Input,
                SearchTokens = contextPack.SearchTokens,
                DetectedGapItems = contextPack.DetectedGapItems,
                ClarifyingQuestions = contextPack.ClarifyingQuestions,
                TopMatches = contextPack.TopMatches,
                Memories = contextPack.Memories,
                Todos = contextPack.Todos,
                Tickets = contextPack.Tickets,
                CodeGraphProjects = contextPack.CodeGraphProjects,
                CodeGraphFiles = contextPack.CodeGraphFiles,
                CodeGraphNodes = contextPack.CodeGraphNodes,
                RecommendedSkills = contextPack.RecommendedSkills,
                ExternalSkillAlert = externalSkillAlert,
                ExportText = contextPack.ExportText
            };
        }

        var recommendedAgents = _agentCatalogService.RecommendAgents(normalizedInput.Question, normalizedInput.PackGoal, 4);
        var fallbackRecommendedSkills = await RecommendSkillsAsync(normalizedInput.Question, normalizedInput.WingId, null, 4, cancellationToken);

        return new DashboardContextPanelViewModel
        {
            ContextInput = normalizedInput,
            ContextPack = contextPack,
            RecommendedAgents = recommendedAgents,
            RecommendedSkills = MergeSkillCards(contextPack.RecommendedSkills, fallbackRecommendedSkills, 4),
            ExtraCommandSuggestions = Array.Empty<string>()
        };
    }

    public Task<AgentBrowseViewModel> GetAgentCatalogAsync(
        string? query,
        ContextPackGoal? goal,
        bool supportsWriteActionsOnly,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AgentBrowseViewModel
        {
            Query = query?.Trim() ?? string.Empty,
            Goal = goal,
            SupportsWriteActionsOnly = supportsWriteActionsOnly,
            Agents = _agentCatalogService.GetCatalog(query, goal, supportsWriteActionsOnly)
        });
    }

    public async Task<AgentDetailViewModel?> GetAgentAsync(string slug, CancellationToken cancellationToken)
        => await BuildAgentDetailAsync(slug, null, false, cancellationToken);

    public async Task<AgentDetailViewModel?> RunAgentAsync(string slug, AgentRunInput input, CancellationToken cancellationToken)
        => await BuildAgentDetailAsync(slug, input, true, cancellationToken);

    private async Task<AgentDetailViewModel?> BuildAgentDetailAsync(string slug, AgentRunInput? runInput, bool buildRun, CancellationToken cancellationToken)
    {
        var detail = _agentCatalogService.GetAgent(slug);
        if (detail is null)
        {
            return null;
        }

        var relatedContext = await _contextService.BuildContextPackAsync(new ContextBriefInput
        {
            Question = detail.SuggestedQuestion,
            IncludeCompletedWork = true,
            ExpandHistory = true,
            ResultsPerSection = 3,
            PackGoal = detail.Agent.DefaultGoal,
            PreferRecentChanges = true
        }, cancellationToken);
        var companionSkills = await RecommendSkillsAsync(
            $"{detail.Agent.Name} {detail.SuggestedQuestion} {detail.SuggestedPrompt}",
            null,
            null,
            4,
            cancellationToken);
        var peerAgents = _agentCatalogService.RecommendAgents(
                $"{detail.Agent.Name} {detail.SuggestedQuestion}",
                detail.Agent.DefaultGoal,
                4)
            .Where(agent => !string.Equals(agent.Slug, slug, StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToArray();
        var effectiveRunInput = runInput ?? new AgentRunInput
        {
            Question = detail.SuggestedQuestion
        };
        AgentRunViewModel? runResult = null;
        if (buildRun && !string.IsNullOrWhiteSpace(effectiveRunInput.Question))
        {
            var runContext = await _contextService.BuildContextPackAsync(new ContextBriefInput
            {
                Question = effectiveRunInput.Question.Trim(),
                IncludeCompletedWork = effectiveRunInput.IncludeCompletedWork,
                ExpandHistory = effectiveRunInput.ExpandHistory,
                ResultsPerSection = Math.Clamp(effectiveRunInput.ResultsPerSection, 3, 10),
                PackGoal = detail.Agent.DefaultGoal,
                PreferRecentChanges = true
            }, cancellationToken);
            var runSkills = await RecommendSkillsAsync(
                $"{detail.Agent.Name} {effectiveRunInput.Question} {detail.SuggestedPrompt}",
                null,
                null,
                4,
                cancellationToken);
            var runPeers = _agentCatalogService.RecommendAgents(
                    effectiveRunInput.Question,
                    detail.Agent.DefaultGoal,
                    4)
                .Where(agent => !string.Equals(agent.Slug, slug, StringComparison.OrdinalIgnoreCase))
                .Take(3)
                .ToArray();
            runResult = BuildAgentRunViewModel(detail, effectiveRunInput, runContext, runSkills, runPeers);
        }

        return new AgentDetailViewModel
        {
            Agent = detail.Agent,
            Inputs = detail.Inputs,
            Outputs = detail.Outputs,
            SuggestedPrompt = detail.SuggestedPrompt,
            SuggestedQuestion = detail.SuggestedQuestion,
            RelatedContext = relatedContext,
            CompanionSkills = companionSkills,
            RecommendedPeers = peerAgents,
            RunInput = effectiveRunInput,
            RunResult = runResult
        };
    }

    public async Task<DashboardDiagnosticsViewModel> GetDashboardDiagnosticsAsync(ContextBriefInput? contextInput, CancellationToken cancellationToken)
    {
        var dashboard = await GetDashboardAsync(contextInput, cancellationToken);
        var recentChanges = await GetRecentChangesAsync(16, cancellationToken);

        return new DashboardDiagnosticsViewModel
        {
            GeneratedUtc = DateTime.UtcNow,
            Stats = dashboard.Stats,
            ContextInput = dashboard.ContextInput,
            FallbackContext = dashboard.FallbackContext,
            ContextSummary = dashboard.ContextPack?.Summary ?? string.Empty,
            TopMatchCount = dashboard.ContextPack?.TopMatches.Count ?? 0,
            DetectedGaps = dashboard.MissingContextWarnings,
            DetectedGapItems = dashboard.MissingContextWarningItems,
            RecentChanges = recentChanges,
            Sections =
            [
                new DashboardSectionSnapshotViewModel
                {
                    Key = "current-todos",
                    Title = "Current todos",
                    Count = dashboard.CurrentTodos.Count,
                    IsEmpty = dashboard.CurrentTodos.Count == 0,
                    Items = dashboard.CurrentTodos.Select(todo => new DashboardDiagnosticRecordViewModel
                    {
                        Id = todo.Id.ToString(),
                        Title = todo.Title,
                        Subtitle = todo.StatusLabel,
                        Detail = todo.PreviewDetails,
                        Url = $"/Todos/Details/{todo.Id}"
                    }).ToArray()
                },
                new DashboardSectionSnapshotViewModel
                {
                    Key = "active-tickets",
                    Title = "Active tickets",
                    Count = dashboard.ActiveTickets.Count,
                    IsEmpty = dashboard.ActiveTickets.Count == 0,
                    Items = dashboard.ActiveTickets.Select(ticket => new DashboardDiagnosticRecordViewModel
                    {
                        Id = ticket.Id.ToString(),
                        Title = $"{ticket.TicketNumber} - {ticket.Title}",
                        Subtitle = $"{ticket.StatusLabel} • {ticket.PriorityLabel}",
                        Detail = ticket.PreviewDescription,
                        Url = $"/Tickets/Details/{ticket.Id}"
                    }).ToArray()
                },
                new DashboardSectionSnapshotViewModel
                {
                    Key = "recent-activity",
                    Title = "Recent activity",
                    Count = dashboard.RecentActivity.Count,
                    IsEmpty = dashboard.RecentActivity.Count == 0,
                    Items = dashboard.RecentActivity.Select(activity => new DashboardDiagnosticRecordViewModel
                    {
                        Id = activity.Url,
                        Title = activity.Title,
                        Subtitle = $"{activity.Label} • {activity.OccurredUtc:O}",
                        Detail = activity.Detail,
                        Url = activity.Url
                    }).ToArray()
                },
                new DashboardSectionSnapshotViewModel
                {
                    Key = "pinned-memories",
                    Title = "Pinned memories",
                    Count = dashboard.PinnedMemories.Count,
                    IsEmpty = dashboard.PinnedMemories.Count == 0,
                    Items = dashboard.PinnedMemories.Select(memory => new DashboardDiagnosticRecordViewModel
                    {
                        Id = memory.Id.ToString(),
                        Title = memory.Title,
                        Subtitle = $"{memory.Kind} • {memory.WingName} / {memory.RoomName}",
                        Detail = memory.Summary,
                        Url = $"/Palace/Memory/{memory.Id}"
                    }).ToArray()
                },
                new DashboardSectionSnapshotViewModel
                {
                    Key = "recent-memories",
                    Title = "Recent memories",
                    Count = dashboard.RecentMemories.Count,
                    IsEmpty = dashboard.RecentMemories.Count == 0,
                    Items = dashboard.RecentMemories.Select(memory => new DashboardDiagnosticRecordViewModel
                    {
                        Id = memory.Id.ToString(),
                        Title = memory.Title,
                        Subtitle = $"{memory.Kind} • {memory.SourceKind}",
                        Detail = memory.Summary,
                        Url = $"/Palace/Memory/{memory.Id}"
                    }).ToArray()
                },
                new DashboardSectionSnapshotViewModel
                {
                    Key = "wings",
                    Title = "Wings",
                    Count = dashboard.Wings.Count,
                    IsEmpty = dashboard.Wings.Count == 0,
                    Items = dashboard.Wings.Select(wing => new DashboardDiagnosticRecordViewModel
                    {
                        Id = wing.Id.ToString(),
                        Title = wing.Name,
                        Subtitle = $"{wing.RoomCount} rooms • {wing.MemoryCount} memories",
                        Detail = wing.Description,
                        Url = $"/Palace/Wing/{wing.Slug}"
                    }).ToArray()
                },
                new DashboardSectionSnapshotViewModel
                {
                    Key = "top-context-matches",
                    Title = "Top context matches",
                    Count = dashboard.ContextPack?.TopMatches.Count ?? 0,
                    IsEmpty = dashboard.ContextPack is null || dashboard.ContextPack.TopMatches.Count == 0,
                    Items = dashboard.ContextPack?.TopMatches.Select(match => new DashboardDiagnosticRecordViewModel
                    {
                        Id = match.Url,
                        Title = match.Title,
                        Subtitle = $"{match.KindLabel} • {match.ScoreLabel}",
                        Detail = string.IsNullOrWhiteSpace(match.MatchReason) ? match.Subtitle : match.MatchReason,
                        Url = match.Url
                    }).ToArray() ?? Array.Empty<DashboardDiagnosticRecordViewModel>()
                }
            ]
        };
    }

    public async Task<WorkspaceExportViewModel> GetWorkspaceExportAsync(CancellationToken cancellationToken)
    {
        var pinnedMemories = await BuildMemoryQuery()
            .Where(x => x.IsPinned)
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(12)
            .ToListAsync(cancellationToken);

        var activeTodos = await BuildTodoQuery(includeDone: false)
            .Take(12)
            .ToListAsync(cancellationToken);

        var activeTickets = await _dbContext.Tickets
            .AsNoTracking()
            .Include(x => x.SubTickets)
            .Include(x => x.TimeLogs)
            .AsSplitQuery()
            .Where(x => x.ParentTicketId == null && x.Status != TicketStatus.Completed)
            .OrderBy(x => x.Status == TicketStatus.InProgress ? 0 : x.Status == TicketStatus.New ? 1 : 2)
            .ThenByDescending(x => x.UpdatedUtc)
            .Take(12)
            .ToListAsync(cancellationToken);

        var codeGraphProjects = await _dbContext.CodeGraphProjects
            .AsNoTracking()
            .OrderByDescending(x => x.LastScannedUtc ?? x.UpdatedUtc)
            .Take(8)
            .Select(x => new CodeGraphProjectCardViewModel
            {
                Id = x.Id,
                Name = x.Name,
                RootPath = x.RootPath,
                Description = x.Description,
                Summary = x.Summary,
                FileCount = x.FileCount,
                SymbolCount = x.SymbolCount,
                RelationshipCount = x.RelationshipCount,
                CreatedUtc = x.CreatedUtc,
                UpdatedUtc = x.UpdatedUtc,
                LastScannedUtc = x.LastScannedUtc
            })
            .ToListAsync(cancellationToken);

        var recentChanges = await GetRecentChangesAsync(12, cancellationToken);
        var mappedPinned = pinnedMemories.Select(MapCard).ToArray();
        var mappedTodos = activeTodos.Select(MapTodo).ToArray();
        var mappedTickets = activeTickets.Select(MapDashboardTicketSummary).ToArray();
        var recommendedAgents = _agentCatalogService.RecommendAgents(null, ContextPackGoal.General, 4);
        var recommendedSkills = await RecommendSkillsAsync(null, null, null, 6, cancellationToken);

        return new WorkspaceExportViewModel
        {
            GeneratedUtc = DateTime.UtcNow,
            DatabaseTarget = _databaseTargetService?.GetCurrentTarget() ?? new FocusDatabaseTargetSnapshot(),
            Stats = await BuildStatsAsync(cancellationToken),
            ExportText = BuildWorkspaceExportText(recommendedAgents, recommendedSkills, mappedPinned, mappedTodos, mappedTickets, codeGraphProjects, recentChanges),
            RecommendedAgents = recommendedAgents,
            RecommendedSkills = recommendedSkills,
            PinnedMemories = mappedPinned,
            ActiveTodos = mappedTodos,
            ActiveTickets = mappedTickets,
            CodeGraphProjects = codeGraphProjects,
            RecentChanges = recentChanges
        };
    }

    public async Task<WorkspaceBootstrapViewModel> GetWorkspaceBootstrapAsync(string? profile, CancellationToken cancellationToken)
    {
        var workspace = await GetWorkspaceExportAsync(cancellationToken);
        var wings = (await BuildWingSummariesAsync(cancellationToken)).Take(10).ToArray();
        var featuredSkills = await GetFeaturedSkillsAsync(6, cancellationToken);
        var normalizedProfile = NormalizeBootstrapProfile(profile);

        var suggestedTaskPrompt = normalizedProfile switch
        {
            "operator" => "Start with Focus. Read the bootstrap, inspect recent changes, check governance and active tickets, then act.",
            "incident-response" => "Start with Focus. Inspect recent changes, search incident memories, build a debugging context pack, then respond.",
            _ => "Start with Focus. Search memories, build a context pack, review recent changes or tickets if relevant, then work."
        };

        IReadOnlyCollection<string> recommendedFlow = normalizedProfile switch
        {
            "operator" => new[]
            {
                "Read the bootstrap summary and recent changes first.",
                "Check governance and active tickets before changing durable project memory.",
                "Use wing and room discovery to route updates into the right project area.",
                "Write back verified operational findings after the work is complete."
            },
            "incident-response" => new[]
            {
                "Search memories for the affected service, trap, or subsystem.",
                "Build a debugging-focused context pack with recent-change bias.",
                "Review recent changes, active tickets, and incident memories before editing anything.",
                "Capture remediation and verification details in Focus once the issue is contained."
            },
            _ => new[]
            {
                "Search memories for the project, feature, or system you are about to touch.",
                "Build a context pack for the exact task so recent memories, todos, tickets, and code graph hits get ranked together.",
                "Check recent changes and active work before writing new code or capturing new memories.",
                "Use wing and room discovery when you need the right destination for a new memory or update."
            }
        };

        IReadOnlyCollection<string> suggestedQuestions = normalizedProfile switch
        {
            "operator" => new[]
            {
                "What changed recently that could affect operations?",
                "Which tickets or governance items need attention first?",
                "Where should I record the operational outcome?"
            },
            "incident-response" => new[]
            {
                "Which incident or debugging memories match this signal?",
                "What changed recently in the impacted area?",
                "What is the current canonical memory for this subsystem?"
            },
            _ => new[]
            {
                "What has changed recently for this project?",
                "Which memories should I read before I modify this area?",
                "Which wing and room best fit this new work?"
            }
        };
        var recommendedSkills = await RecommendSkillsAsync(
            $"{suggestedTaskPrompt} {string.Join(' ', suggestedQuestions)}",
            null,
            null,
            6,
            cancellationToken);
        var recommendedAgents = _agentCatalogService.RecommendAgents(
            $"{suggestedTaskPrompt} {string.Join(' ', suggestedQuestions)}",
            normalizedProfile == "incident-response" ? ContextPackGoal.Debugging : ContextPackGoal.General,
            4);

        return new WorkspaceBootstrapViewModel
        {
            GeneratedUtc = workspace.GeneratedUtc,
            Profile = normalizedProfile,
            DatabaseTarget = workspace.DatabaseTarget,
            SuggestedTaskPrompt = suggestedTaskPrompt,
            BootstrapSummary = BuildWorkspaceBootstrapSummary(workspace, wings, normalizedProfile),
            RecommendedFlow = recommendedFlow,
            SuggestedQuestions = suggestedQuestions,
            RecommendedAgents = recommendedAgents,
            FeaturedAgents = _agentCatalogService.GetFeaturedAgents(),
            RecommendedSkills = recommendedSkills,
            FeaturedSkills = featuredSkills,
            Wings = wings,
            Workspace = workspace
        };
    }

    public async Task<IReadOnlyCollection<RecentChangeItemViewModel>> GetRecentChangesAsync(int limit, CancellationToken cancellationToken)
    {
        var effectiveLimit = Math.Clamp(limit, 1, 50);
        var memoryChanges = await _dbContext.Memories
            .AsNoTracking()
            .Where(x => x.LifecycleState == MemoryLifecycleState.Active)
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(effectiveLimit)
            .Select(x => new RecentChangeItemViewModel
            {
                Kind = "Memory",
                Title = x.Title,
                Detail = x.Summary,
                Url = $"/Palace/Memory/{x.Id}",
                ChangedUtc = x.UpdatedUtc
            })
            .ToListAsync(cancellationToken);

        var todoChanges = await _dbContext.Todos
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(effectiveLimit)
            .Select(x => new RecentChangeItemViewModel
            {
                Kind = "Todo",
                Title = x.Title,
                Detail = x.Status == TodoStatus.Done ? "Completed work item" : $"Status: {MapDashboardTodoStatusLabel(x.Status)}",
                Url = $"/Todos/Details/{x.Id}",
                ChangedUtc = x.UpdatedUtc
            })
            .ToListAsync(cancellationToken);

        var ticketChanges = await _dbContext.Tickets
            .AsNoTracking()
            .Where(x => x.ParentTicketId == null)
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(effectiveLimit)
            .Select(x => new RecentChangeItemViewModel
            {
                Kind = "Ticket",
                Title = $"{x.TicketNumber} - {x.Title}",
                Detail = x.Status == TicketStatus.Completed ? "Completed ticket" : $"Status: {MapTicketStatusLabel(x.Status)}",
                Url = $"/Tickets/Details/{x.Id}",
                ChangedUtc = x.UpdatedUtc
            })
            .ToListAsync(cancellationToken);

        var codeGraphChanges = await _dbContext.CodeGraphProjects
            .AsNoTracking()
            .OrderByDescending(x => x.LastScannedUtc ?? x.UpdatedUtc)
            .Take(effectiveLimit)
            .Select(x => new RecentChangeItemViewModel
            {
                Kind = "Code graph",
                Title = x.Name,
                Detail = x.LastScannedUtc.HasValue
                    ? $"Scanned {x.FileCount} files and {x.SymbolCount} symbols."
                    : "Project created but not scanned yet.",
                Url = $"/CodeGraph/Project/{x.Id}",
                ChangedUtc = x.LastScannedUtc ?? x.UpdatedUtc
            })
            .ToListAsync(cancellationToken);

        return memoryChanges
            .Concat(todoChanges)
            .Concat(ticketChanges)
            .Concat(codeGraphChanges)
            .OrderByDescending(x => x.ChangedUtc)
            .Take(effectiveLimit)
            .ToArray();
    }

    public async Task<InspectorViewModel> GetInspectorAsync(ContextBriefInput? contextInput, CancellationToken cancellationToken)
    {
        var diagnostics = await GetDashboardDiagnosticsAsync(contextInput, cancellationToken);
        return new InspectorViewModel
        {
            Diagnostics = diagnostics,
            RecentChanges = diagnostics.RecentChanges,
            GovernanceQueue = await BuildGovernanceQueueAsync(cancellationToken)
        };
    }

    public async Task<TodoBoardViewModel> GetTodoBoardAsync(CancellationToken cancellationToken)
    {
        var todos = await BuildTodoQuery(includeDone: true).ToListAsync(cancellationToken);

        return new TodoBoardViewModel
        {
            Stats = await BuildStatsAsync(cancellationToken),
            InProgressTodos = todos.Where(x => x.Status == TodoStatus.InProgress).Select(MapTodo).ToArray(),
            PendingTodos = todos.Where(x => x.Status == TodoStatus.Pending).Select(MapTodo).ToArray(),
            BlockedTodos = todos.Where(x => x.Status == TodoStatus.Blocked).Select(MapTodo).ToArray(),
            DoneTodos = todos.Where(x => x.Status == TodoStatus.Done).Select(MapTodo).ToArray()
        };
    }

    public async Task<TodoDetailsViewModel> GetTodoDetailsAsync(Guid id, CancellationToken cancellationToken)
    {
        var todo = await _dbContext.Todos.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (todo is null)
        {
            throw new InvalidOperationException("That todo no longer exists.");
        }

        return new TodoDetailsViewModel
        {
            Todo = MapTodo(todo),
            Input = new TodoEditorInput
            {
                Title = todo.Title,
                Details = todo.Details,
                Status = todo.Status
            },
            ContextLinks = await _contextService.BuildLinksPanelAsync(
                ContextRecordKind.Todo,
                todo.Id,
                todo.Title,
                $"{todo.Title} {todo.Details}",
                $"/Todos/Details/{todo.Id}",
                cancellationToken)
        };
    }

    public async Task<WingBrowseViewModel> GetWingCatalogAsync(CancellationToken cancellationToken)
    {
        return new WingBrowseViewModel
        {
            Wings = await BuildWingSummariesAsync(cancellationToken)
        };
    }

    public async Task<RoomBrowseViewModel> GetRoomCatalogAsync(CancellationToken cancellationToken)
    {
        var rooms = await _dbContext.Rooms
            .AsNoTracking()
            .Include(x => x.Wing)
            .Include(x => x.Memories)
            .OrderBy(x => x.Wing!.Name)
            .ThenBy(x => x.Name)
            .Select(x => new RoomBrowseItemViewModel
            {
                RoomId = x.Id,
                WingId = x.WingId,
                WingName = x.Wing!.Name,
                WingSlug = x.Wing.Slug,
                RoomName = x.Name,
                RoomDescription = x.Description,
                MemoryCount = x.Memories.Count
            })
            .ToListAsync(cancellationToken);

        return new RoomBrowseViewModel
        {
            Rooms = rooms
        };
    }

    public async Task<TagBrowseViewModel> GetTagCatalogAsync(CancellationToken cancellationToken)
    {
        var tags = await _dbContext.Tags
            .AsNoTracking()
            .Include(x => x.MemoryTags)
            .OrderBy(x => x.Name)
            .Select(x => new TagBrowseItemViewModel
            {
                Name = x.Name,
                Slug = x.Slug,
                MemoryCount = x.MemoryTags.Count
            })
            .ToListAsync(cancellationToken);

        return new TagBrowseViewModel
        {
            Tags = tags
        };
    }

    public async Task<PalaceVisualizerViewModel> GetVisualizerAsync(CancellationToken cancellationToken)
    {
        await NormalizeWingLevelMemoriesIntoGeneralRoomsAsync(cancellationToken);

        var wings = await _dbContext.Wings
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var rooms = await _dbContext.Rooms
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        var memories = await _dbContext.Memories
            .AsNoTracking()
            .Where(x => x.LifecycleState == MemoryLifecycleState.Active)
            .Include(x => x.MemoryTags)
                .ThenInclude(x => x.Tag)
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.UpdatedUtc)
            .ToListAsync(cancellationToken);
        var activeTodos = await BuildTodoQuery(includeDone: false).ToListAsync(cancellationToken);

        var activeMemoryIds = memories.Select(x => x.Id).ToArray();
        var memoryLinks = await _dbContext.MemoryLinks
            .AsNoTracking()
            .Where(x => activeMemoryIds.Contains(x.FromMemoryEntryId) && activeMemoryIds.Contains(x.ToMemoryEntryId))
            .ToListAsync(cancellationToken);

        var visualMemories = memories
            .Select(memory => new
            {
                memory.WingId,
                memory.RoomId,
                Item = new PalaceVisualizerMemoryViewModel
                {
                    Id = memory.Id,
                    Title = memory.Title,
                    Importance = memory.Importance,
                    IsPinned = memory.IsPinned,
                    TagNames = memory.MemoryTags
                        .Select(x => x.Tag!.Name)
                        .OrderBy(x => x)
                        .ToArray(),
                    TagSlugs = memory.MemoryTags
                        .Select(x => x.Tag!.Slug)
                        .OrderBy(x => x)
                        .ToArray()
                }
                })
                .ToList();

        var tagStats = await _dbContext.Tags
            .AsNoTracking()
            .Include(x => x.MemoryTags)
            .OrderBy(x => x.Name)
            .Select(x => new TagCloudItemViewModel
            {
                Name = x.Name,
                Slug = x.Slug,
                MemoryCount = x.MemoryTags.Count
            })
            .ToListAsync(cancellationToken);

        var maxTagCount = tagStats.Count == 0 ? 1 : tagStats.Max(x => x.MemoryCount);
        var minTagCount = tagStats.Count == 0 ? 1 : tagStats.Min(x => x.MemoryCount);

        return new PalaceVisualizerViewModel
        {
            Wings = wings
                .Select(wing => new PalaceVisualizerWingViewModel
                {
                    WingId = wing.Id,
                    Name = wing.Name,
                    Slug = wing.Slug,
                    Description = wing.Description,
                    Rooms = rooms
                        .Where(room => room.WingId == wing.Id)
                        .OrderBy(room => room.Name)
                        .Select(room => new PalaceVisualizerRoomViewModel
                        {
                            RoomId = room.Id,
                            Name = room.Name,
                            Description = room.Description,
                            Memories = visualMemories
                                .Where(memory => memory.RoomId == room.Id)
                                .Select(memory => memory.Item)
                                .ToArray()
                        })
                        .ToArray(),
                    GeneralMemories = Array.Empty<PalaceVisualizerMemoryViewModel>()
                })
                .ToArray(),
            UnsortedMemories = visualMemories
                .Where(memory => memory.WingId is null)
                .Select(memory => memory.Item)
                .ToArray(),
            ActiveTodos = activeTodos.Select(MapTodo).ToArray(),
            Tags = tagStats
                .Select(tag => new TagCloudItemViewModel
                {
                    Name = tag.Name,
                    Slug = tag.Slug,
                    MemoryCount = tag.MemoryCount,
                    Weight = maxTagCount == minTagCount
                        ? 3
                        : 1 + (int)Math.Round(((double)(tag.MemoryCount - minTagCount) / (maxTagCount - minTagCount)) * 4d, MidpointRounding.AwayFromZero)
                })
                .ToArray(),
            Scene = BuildPalaceScene(wings, rooms, memories, memoryLinks, activeTodos)
        };
    }

    public async Task<ExploreViewModel> GetExploreAsync(string? query, Guid? wingId, Guid? roomId, MemoryKind? kind, string? tag, CancellationToken cancellationToken)
    {
        var memories = await BuildMemoryQuery(query, wingId, roomId, kind, tag)
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.UpdatedUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        return new ExploreViewModel
        {
            Query = query?.Trim() ?? string.Empty,
            WingId = wingId,
            RoomId = roomId,
            Kind = kind,
            Tag = tag?.Trim() ?? string.Empty,
            Memories = memories.Select(MapCard).ToArray(),
            WingOptions = await BuildWingOptionsAsync(cancellationToken),
            RoomOptions = await BuildRoomOptionsAsync(wingId, cancellationToken)
        };
    }

    public async Task<SkillBrowseViewModel> GetSkillCatalogAsync(
        string? query,
        SkillCategory? category,
        Guid? wingId,
        bool pinnedOnly,
        bool needsReviewOnly,
        CancellationToken cancellationToken)
    {
        var skills = await GetMergedSkillsAsync(query, category, wingId, pinnedOnly, needsReviewOnly, cancellationToken);

        return new SkillBrowseViewModel
        {
            Query = query?.Trim() ?? string.Empty,
            Category = category,
            WingId = wingId,
            PinnedOnly = pinnedOnly,
            NeedsReviewOnly = needsReviewOnly,
            WingOptions = await BuildWingOptionsAsync(cancellationToken),
            Skills = skills
                .OrderByDescending(x => x.Skill.IsPinned)
                .ThenByDescending(x => x.Skill.UseCount)
                .ThenBy(x => x.Skill.ReviewAfterUtc == null ? 1 : 0)
                .ThenBy(x => x.Skill.ReviewAfterUtc)
                .ThenBy(x => x.Skill.Category)
                .ThenBy(x => x.Skill.Name)
                .Select(MapSkillCard)
                .ToArray()
        };
    }

    public async Task<SkillDetailViewModel?> GetSkillAsync(string slug, CancellationToken cancellationToken, bool trackUsage = false)
    {
        var skillQuery = _dbContext.Skills
            .Include(x => x.Wing)
            .AsQueryable();
        if (!trackUsage)
        {
            skillQuery = skillQuery.AsNoTracking();
        }

        var skill = await skillQuery.FirstOrDefaultAsync(x => x.Slug == slug, cancellationToken);
        if (skill is null)
        {
            var repoSkill = await GetRepoSkillAsync(slug, cancellationToken);
            if (repoSkill is null)
            {
                return null;
            }

            var repoContext = await _contextService.BuildContextPackAsync(new ContextBriefInput
            {
                Question = SkillRecommendationEngine.BuildSuggestedQuestion(repoSkill.Skill),
                WingId = repoSkill.Skill.WingId,
                IncludeCompletedWork = true,
                ExpandHistory = true,
                ResultsPerSection = 3,
                PackGoal = MapSkillPackGoal(repoSkill.Skill.Category),
                PreferRecentChanges = true
            }, cancellationToken);

            return MapSkillDetail(repoSkill.Skill, repoContext, isReadOnly: true, sourceLabel: repoSkill.SourceLabel, sourcePath: repoSkill.SourcePath);
        }

        if (trackUsage)
        {
            await TouchSkillUsageAsync(skill, cancellationToken);
        }

        var relatedContext = await _contextService.BuildContextPackAsync(new ContextBriefInput
        {
            Question = SkillRecommendationEngine.BuildSuggestedQuestion(skill),
            WingId = skill.WingId,
            IncludeCompletedWork = true,
            ExpandHistory = true,
            ResultsPerSection = 3,
            PackGoal = MapSkillPackGoal(skill.Category),
            PreferRecentChanges = true
        }, cancellationToken);

        return MapSkillDetail(skill, relatedContext);
    }

    public async Task<SkillEditorViewModel> BuildSkillEditorAsync(Guid? id, CancellationToken cancellationToken)
    {
        SkillEditorInput input;
        string heading;
        string submitLabel;

        if (id.HasValue)
        {
            var skill = await _dbContext.Skills
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id.Value, cancellationToken)
                ?? throw new InvalidOperationException("That skill no longer exists.");

            input = new SkillEditorInput
            {
                Id = skill.Id,
                Name = skill.Name,
                Summary = skill.Summary,
                Category = skill.Category,
                WhenToUse = skill.WhenToUse,
                Flow = skill.Flow,
                ExamplesText = skill.ExamplesText,
                TriggerHintsText = skill.TriggerHintsText,
                WingId = skill.WingId,
                IsPinned = skill.IsPinned
            };
            heading = "Edit skill";
            submitLabel = "Save Skill";
        }
        else
        {
            input = new SkillEditorInput();
            heading = "New skill";
            submitLabel = "Create Skill";
        }

        return new SkillEditorViewModel
        {
            Heading = heading,
            SubmitLabel = submitLabel,
            Input = input,
            WingOptions = await BuildWingOptionsAsync(cancellationToken)
        };
    }

    public async Task<Guid> SaveSkillAsync(SkillEditorInput input, CancellationToken cancellationToken)
    {
        var normalizedName = (input.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw new InvalidOperationException("Provide a skill name.");
        }

        var slug = SlugUtility.CreateSlug(normalizedName);
        var duplicate = await _dbContext.Skills
            .FirstOrDefaultAsync(x => x.Slug == slug && x.Id != input.Id, cancellationToken);
        if (duplicate is not null)
        {
            throw new InvalidOperationException("A skill with that name already exists.");
        }

        SkillEntry skill;
        if (input.Id.HasValue)
        {
            skill = await _dbContext.Skills.FirstOrDefaultAsync(x => x.Id == input.Id.Value, cancellationToken)
                ?? throw new InvalidOperationException("That skill no longer exists.");
        }
        else
        {
            skill = new SkillEntry
            {
                CreatedUtc = DateTime.UtcNow
            };
            _dbContext.Skills.Add(skill);
        }

        var now = DateTime.UtcNow;
        skill.Name = normalizedName;
        skill.Slug = slug;
        skill.Summary = (input.Summary ?? string.Empty).Trim();
        skill.Category = input.Category;
        skill.WhenToUse = (input.WhenToUse ?? string.Empty).Trim();
        skill.Flow = (input.Flow ?? string.Empty).Trim();
        skill.ExamplesText = (input.ExamplesText ?? string.Empty).Trim();
        skill.TriggerHintsText = (input.TriggerHintsText ?? string.Empty).Trim();
        skill.WingId = input.WingId;
        skill.IsPinned = input.IsPinned;
        skill.LastReviewedUtc = now;
        skill.ReviewAfterUtc = now.AddDays(SkillRecommendationEngine.DefaultReviewWindowDays);
        skill.UpdatedUtc = now;

        await _dbContext.SaveChangesAsync(cancellationToken);
        return skill.Id;
    }

    public async Task DeleteSkillAsync(Guid id, CancellationToken cancellationToken)
    {
        var skill = await _dbContext.Skills.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("That skill no longer exists.");

        _dbContext.Skills.Remove(skill);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<SkillCardViewModel>> GetSkillSummariesAsync(
        string? query,
        SkillCategory? category,
        CancellationToken cancellationToken,
        Guid? wingId = null,
        bool pinnedOnly = false,
        bool needsReviewOnly = false)
    {
        var skills = await GetMergedSkillsAsync(query, category, wingId, pinnedOnly, needsReviewOnly, cancellationToken);
        return skills
            .OrderByDescending(x => x.Skill.IsPinned)
            .ThenByDescending(x => x.Skill.UseCount)
            .ThenBy(x => x.Skill.Category)
            .ThenBy(x => x.Skill.Name)
            .Select(MapSkillCard)
            .ToArray();
    }

    public async Task<IReadOnlyCollection<SkillCardViewModel>> RecommendSkillsAsync(
        string? question,
        Guid? wingId,
        SkillCategory? category,
        int limit,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(question) && category is null)
        {
            var pack = await _contextService.BuildContextPackAsync(new ContextBriefInput
            {
                Question = question.Trim(),
                WingId = wingId,
                ResultsPerSection = Math.Clamp(limit, 3, 10)
            }, cancellationToken);

            if (pack?.RecommendedSkills.Count > 0)
            {
                return pack.RecommendedSkills
                    .Take(limit)
                    .ToArray();
            }
        }

        var dbSkills = await _dbContext.Skills
            .AsNoTracking()
            .Include(x => x.Wing)
            .ToListAsync(cancellationToken);
        var skillRecords = dbSkills.Select(x => new ResolvedSkillRecord(x))
            .Concat((await GetRepoSkillsAsync(cancellationToken)).Where(x => dbSkills.All(dbSkill => !string.Equals(dbSkill.Slug, x.Skill.Slug, StringComparison.OrdinalIgnoreCase))))
            .ToArray();

        return SkillRecommendationEngine.Recommend(skillRecords.Select(x => x.Skill), question, wingId, category, limit)
            .Select(match =>
            {
                var record = skillRecords.First(x => string.Equals(x.Skill.Slug, match.Skill.Slug, StringComparison.OrdinalIgnoreCase));
                return MapSkillCard(record.Skill, match.Reason, match.Score, record.IsReadOnly, record.SourceLabel, record.SourcePath);
            })
            .ToArray();
    }

    public async Task<WingDetailViewModel?> GetWingAsync(string slug, string? selectedRoomSlug, CancellationToken cancellationToken)
    {
        var wing = await _dbContext.Wings
            .AsNoTracking()
            .Include(x => x.Rooms)
            .Include(x => x.Memories)
                .ThenInclude(x => x.SupersededByMemory)
            .Include(x => x.Memories)
                .ThenInclude(x => x.MemoryTags)
                    .ThenInclude(x => x.Tag)
            .FirstOrDefaultAsync(x => x.Slug == slug, cancellationToken);

        if (wing is null)
        {
            return null;
        }

        var selectedRoom = !string.IsNullOrWhiteSpace(selectedRoomSlug)
            ? wing.Rooms.FirstOrDefault(room => string.Equals(room.Slug, selectedRoomSlug, StringComparison.OrdinalIgnoreCase))
            : null;

        var activeMemories = wing.Memories
            .Where(MemoryTrustHelper.IsActive)
            .Where(memory => selectedRoom is null || memory.RoomId == selectedRoom.Id)
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.UpdatedUtc)
            .Select(MapCard)
            .ToArray();

        return new WingDetailViewModel
        {
            Id = wing.Id,
            Slug = wing.Slug,
            Name = wing.Name,
            Description = wing.Description,
            SelectedRoomSlug = selectedRoom?.Slug ?? string.Empty,
            SelectedRoom = selectedRoom is null
                ? null
                : new RoomDetailPanelViewModel
                {
                    Id = selectedRoom.Id,
                    Slug = selectedRoom.Slug,
                    Name = selectedRoom.Name,
                    Description = selectedRoom.Description,
                    MemoryCount = wing.Memories.Count(m => m.RoomId == selectedRoom.Id && m.LifecycleState == MemoryLifecycleState.Active)
                },
            Rooms = wing.Rooms
                .OrderBy(x => x.Name)
                .Select(x => new RoomSummaryViewModel
                {
                    Id = x.Id,
                    Slug = x.Slug,
                    Name = x.Name,
                    Description = x.Description,
                    MemoryCount = wing.Memories.Count(m => m.RoomId == x.Id && m.LifecycleState == MemoryLifecycleState.Active)
                })
                .ToArray(),
            Memories = activeMemories
        };
    }

    public async Task<MemoryDetailViewModel?> GetMemoryAsync(Guid id, CancellationToken cancellationToken)
    {
        await TouchMemoryReferencesAsync([id], cancellationToken);

        var query = _dbContext.Memories
            .AsNoTracking()
            .Include(x => x.Wing)
            .Include(x => x.Room)
            .Include(x => x.SupersededByMemory)
            .Include(x => x.MemoryTags)
                .ThenInclude(x => x.Tag)
            .Include(x => x.OutgoingLinks)
                .ThenInclude(x => x.ToMemoryEntry)
            .Include(x => x.IncomingLinks)
                .ThenInclude(x => x.FromMemoryEntry);

        var memory = await FindMemoryAsync(query, id, cancellationToken);

        if (memory is null)
        {
            return null;
        }

        return new MemoryDetailViewModel
        {
            Memory = MapCard(memory),
            Content = memory.Content,
            SourceReference = memory.SourceReference,
            CreatedUtc = memory.CreatedUtc,
            OccurredUtc = memory.OccurredUtc,
            SupersedeTargetOptions = await BuildSupersedeTargetOptionsAsync(memory.Id, memory.WingId, cancellationToken),
            OutgoingLinks = memory.OutgoingLinks
                .OrderBy(x => x.Label)
                .Select(x => new MemoryRelationshipViewModel
                {
                    MemoryId = x.ToMemoryEntryId,
                    Title = x.ToMemoryEntry?.Title ?? "Related memory",
                    Label = x.Label
                })
                .ToArray(),
            IncomingLinks = memory.IncomingLinks
                .OrderBy(x => x.Label)
                .Select(x => new MemoryRelationshipViewModel
                {
                    MemoryId = x.FromMemoryEntryId,
                    Title = x.FromMemoryEntry?.Title ?? "Related memory",
                    Label = x.Label
                })
                .ToArray(),
            ContextLinks = await _contextService.BuildLinksPanelAsync(
                ContextRecordKind.Memory,
                memory.Id,
                memory.Title,
                $"{memory.Title} {memory.Summary} {memory.Content} {string.Join(' ', memory.MemoryTags.Select(x => x.Tag!.Name))}",
                $"/Palace/Memory/{memory.Id}",
                cancellationToken)
        };
    }

    public async Task<(MemoryCardViewModel Canonical, IReadOnlyCollection<MemoryCardViewModel> Trail)> ResolveCanonicalMemoryAsync(Guid id, CancellationToken cancellationToken)
    {
        var query = _dbContext.Memories
            .AsNoTracking()
            .Include(x => x.Wing)
            .Include(x => x.Room)
            .Include(x => x.SupersededByMemory)
            .Include(x => x.MemoryTags)
                .ThenInclude(x => x.Tag);

        var current = await FindMemoryAsync(query, id, cancellationToken)
            ?? throw new InvalidOperationException("Memory entry not found.");
        var trail = new List<MemoryCardViewModel>();
        var visited = new HashSet<Guid>();

        for (var depth = 0; depth < 50 && visited.Add(current.Id); depth++)
        {
            trail.Add(MapCard(current));
            if (!current.SupersededByMemoryId.HasValue)
            {
                return (MapCard(current), trail);
            }

            current = await FindMemoryAsync(query, current.SupersededByMemoryId.Value, cancellationToken)
                ?? throw new InvalidOperationException("Canonical memory chain is broken.");
        }

        throw new InvalidOperationException("Canonical memory chain exceeded the safety limit.");
    }

    public async Task<Guid> MergeMemoryAsync(Guid sourceMemoryId, Guid targetMemoryId, string? reason, CancellationToken cancellationToken)
    {
        if (sourceMemoryId == targetMemoryId)
        {
            throw new InvalidOperationException("A memory cannot merge into itself.");
        }

        var source = await _dbContext.Memories
            .Include(x => x.MemoryTags)
                .ThenInclude(x => x.Tag)
            .FirstOrDefaultAsync(x => x.Id == sourceMemoryId, cancellationToken)
            ?? throw new InvalidOperationException("Source memory not found.");
        var target = await _dbContext.Memories
            .Include(x => x.MemoryTags)
                .ThenInclude(x => x.Tag)
            .FirstOrDefaultAsync(x => x.Id == targetMemoryId, cancellationToken)
            ?? throw new InvalidOperationException("Target memory not found.");

        if (target.LifecycleState != MemoryLifecycleState.Active)
        {
            throw new InvalidOperationException("Merge target must be active.");
        }

        var mergedReason = string.IsNullOrWhiteSpace(reason)
            ? $"Merged into '{target.Title}'."
            : reason.Trim();
        var now = DateTime.UtcNow;

        target.Summary = MergeSummary(target.Summary, source.Summary);
        target.Content = MergeContent(target.Content, source.Title, source.Id, source.Content);
        target.SourceReference = MergeMetadataField(target.SourceReference, source.SourceReference, 260);
        target.Importance = Math.Max(target.Importance, source.Importance);
        target.IsPinned = target.IsPinned || source.IsPinned;
        target.OccurredUtc = MinDate(target.OccurredUtc, source.OccurredUtc);
        target.UpdatedUtc = now;
        target.ReviewAfterUtc = now;
        if (source.VerificationStatus == MemoryVerificationStatus.Verified && target.VerificationStatus == MemoryVerificationStatus.Unverified)
        {
            target.VerificationStatus = MemoryVerificationStatus.NeedsReview;
        }

        var mergedTags = target.MemoryTags
            .Select(x => x.Tag!.Name)
            .Concat(source.MemoryTags.Select(x => x.Tag!.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        await _dbContext.SaveChangesAsync(cancellationToken);
        await SyncTagsAsync(target.Id, mergedTags, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await SupersedeMemoryAsync(sourceMemoryId, targetMemoryId, mergedReason, cancellationToken);
        await PublishMemoryEventAsync("memory.merged", target.Id, target.Title, $"Merged '{source.Title}' into this memory.", cancellationToken);
        return target.Id;
    }

    public async Task<MemoryEditorViewModel> BuildMemoryEditorAsync(Guid? id, Guid? selectedWingId, CancellationToken cancellationToken)
    {
        await NormalizeWingLevelMemoriesIntoGeneralRoomsAsync(cancellationToken);

        if (id is null)
        {
            return new MemoryEditorViewModel
            {
                Heading = "Add memory",
                SubmitLabel = "Add Memory",
                Input = new MemoryEditorInput
                {
                    WingId = selectedWingId
                },
                WingOptions = await BuildWingOptionsAsync(cancellationToken),
                RoomOptions = await BuildRoomOptionsAsync(selectedWingId, cancellationToken),
                SuggestedTags = Array.Empty<string>(),
                DuplicateSuggestions = Array.Empty<DuplicateSuggestionViewModel>()
            };
        }

        var query = _dbContext.Memories
            .AsNoTracking()
            .Include(x => x.MemoryTags)
                .ThenInclude(x => x.Tag);

        var memory = await FindMemoryAsync(query, id.Value, cancellationToken)
            ?? throw new InvalidOperationException("Memory entry not found.");

        return new MemoryEditorViewModel
        {
            Heading = "Edit memory",
            SubmitLabel = "Save changes",
            Input = new MemoryEditorInput
            {
                Id = memory.Id,
                Title = memory.Title,
                Summary = memory.Summary,
                Content = memory.Content,
                Kind = memory.Kind,
                SourceKind = memory.SourceKind,
                SourceReference = memory.SourceReference,
                Importance = memory.Importance,
                IsPinned = memory.IsPinned,
                OccurredUtc = memory.OccurredUtc,
                WingId = selectedWingId ?? memory.WingId,
                RoomId = memory.RoomId,
                TagsText = string.Join(", ", memory.MemoryTags.Select(x => x.Tag!.Name).OrderBy(x => x))
            },
            WingOptions = await BuildWingOptionsAsync(cancellationToken),
            RoomOptions = await BuildRoomOptionsAsync(selectedWingId ?? memory.WingId, cancellationToken),
            SuggestedTags = SuggestTagsForDraft(new MemoryEditorInput
            {
                Title = memory.Title,
                Summary = memory.Summary,
                Content = memory.Content,
                TagsText = string.Join(", ", memory.MemoryTags.Select(x => x.Tag!.Name).OrderBy(x => x))
            }),
            DuplicateSuggestions = await FindDuplicateSuggestionsAsync(new MemoryEditorInput
            {
                Id = memory.Id,
                Title = memory.Title,
                Summary = memory.Summary,
                Content = memory.Content
            }, cancellationToken)
        };
    }

    public async Task<Guid> QuickCaptureAsync(QuickCaptureInput input, CancellationToken cancellationToken)
    {
        var memoryInput = BuildQuickCaptureMemoryInput(input);
        return await SaveMemoryAsync(memoryInput, cancellationToken);
    }

    public IReadOnlyCollection<string> SuggestTagsForDraft(MemoryEditorInput input)
    {
        var explicitTags = ParseTags(input.TagsText);
        var suggestedTokens = TokenizeDraftText($"{input.Title} {input.Summary} {input.Content}")
            .Select(NormalizeSuggestedTag)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Where(token => token.Length > 3)
            .Where(token => !IgnoredSuggestedTags.Contains(token))
            .Where(token => !explicitTags.Contains(token, StringComparer.OrdinalIgnoreCase))
            .Take(6)
            .ToArray();

        return explicitTags.Concat(suggestedTokens)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<TagGovernanceViewModel> GetTagGovernanceAsync(CancellationToken cancellationToken)
    {
        var tags = await _dbContext.Tags
            .AsNoTracking()
            .Select(tag => new TagGovernanceItemViewModel
            {
                Name = tag.Name,
                Slug = tag.Slug,
                UsageCount = tag.MemoryTags.Count
            })
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return new TagGovernanceViewModel
        {
            TotalTagCount = tags.Count,
            LowSignalTagCount = tags.Count(x => x.UsageCount == 1),
            UnusedTagCount = tags.Count(x => x.UsageCount == 0),
            LowSignalTags = tags.Where(x => x.UsageCount == 1).Take(12).ToArray(),
            UnusedTags = tags.Where(x => x.UsageCount == 0).Take(12).ToArray()
        };
    }

    public async Task<IReadOnlyCollection<DuplicateSuggestionViewModel>> FindDuplicateSuggestionsAsync(MemoryEditorInput input, CancellationToken cancellationToken)
    {
        var normalizedDraft = $"{input.Title} {input.Summary} {input.Content}".Trim();
        if (string.IsNullOrWhiteSpace(normalizedDraft))
        {
            return Array.Empty<DuplicateSuggestionViewModel>();
        }

        var memories = await BuildMemoryQuery(includeRetired: false)
            .Where(x => !input.Id.HasValue || x.Id != input.Id.Value)
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(40)
            .ToListAsync(cancellationToken);

        return memories
            .Select(memory =>
            {
                var score = ScoreDuplicateSimilarity(input, memory);
                return new { Memory = memory, Score = score };
            })
            .Where(x => x.Score >= 0.34m)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Memory.UpdatedUtc)
            .Take(3)
            .Select(x => new DuplicateSuggestionViewModel
            {
                Id = x.Memory.Id,
                Title = x.Memory.Title,
                Summary = x.Memory.Summary,
                Url = $"/Palace/Memory/{x.Memory.Id}",
                Score = Math.Round(x.Score * 100m, 1),
                ScoreLabel = x.Score >= 0.75m ? "Likely duplicate" : "Related memory",
                MatchReason = x.Score >= 0.75m
                    ? "High overlap with the current draft. Prefer merge or supersede instead of saving another copy."
                    : "Similar language and intent detected.",
                CanMerge = x.Score >= 0.75m
            })
            .ToArray();
    }

    private static string NormalizeSuggestedTag(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        var normalized = token.Trim().ToLowerInvariant();
        return SuggestedTagAliases.TryGetValue(normalized, out var alias)
            ? alias
            : normalized;
    }

    public async Task<RoomEditorViewModel> BuildRoomEditorAsync(CancellationToken cancellationToken)
    {
        return new RoomEditorViewModel
        {
            Input = new RoomEditorInput(),
            WingOptions = await BuildWingOptionsAsync(cancellationToken)
        };
    }

    public async Task<Guid> SaveMemoryAsync(MemoryEditorInput input, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        Room? room = null;
        if (input.RoomId.HasValue)
        {
            room = await _dbContext.Rooms.AsNoTracking().FirstOrDefaultAsync(x => x.Id == input.RoomId.Value, cancellationToken);
        }

        if (input.RoomId.HasValue && room is null)
        {
            throw new InvalidOperationException("The selected room no longer exists.");
        }

        if (input.WingId.HasValue && !await _dbContext.Wings.AnyAsync(x => x.Id == input.WingId.Value, cancellationToken))
        {
            throw new InvalidOperationException("The selected wing no longer exists.");
        }

        var wingId = room?.WingId ?? input.WingId;
        if (room is not null && input.WingId.HasValue && room.WingId != input.WingId.Value)
        {
            throw new InvalidOperationException("The selected room does not belong to the selected wing.");
        }

        if (room is null && wingId.HasValue)
        {
            room = await GetOrCreateGeneralRoomAsync(wingId.Value, cancellationToken);
            wingId = room.WingId;
        }

        MemoryEntry memory;
        var isExistingMemory = input.Id.HasValue;
        var materialChangeDetected = false;
        if (input.Id is null)
        {
            memory = new MemoryEntry
            {
                CreatedUtc = now,
                VerificationStatus = MemoryVerificationStatus.Unverified
            };
            _dbContext.Memories.Add(memory);
        }
        else
        {
            memory = await _dbContext.Memories
                .Include(x => x.MemoryTags)
                .FirstOrDefaultAsync(x => x.Id == input.Id.Value, cancellationToken)
                ?? throw new InvalidOperationException("Memory entry not found.");

            materialChangeDetected =
                !string.Equals(memory.Title, input.Title.Trim(), StringComparison.Ordinal) ||
                !string.Equals(memory.Summary, input.Summary.Trim(), StringComparison.Ordinal) ||
                !string.Equals(memory.Content, input.Content.Trim(), StringComparison.Ordinal) ||
                memory.Kind != input.Kind ||
                memory.SourceKind != input.SourceKind ||
                !string.Equals(memory.SourceReference, input.SourceReference.Trim(), StringComparison.Ordinal) ||
                memory.Importance != Math.Clamp(input.Importance, 1, 5) ||
                memory.IsPinned != input.IsPinned ||
                memory.OccurredUtc != input.OccurredUtc ||
                memory.WingId != wingId ||
                memory.RoomId != room?.Id;
        }

        memory.Title = input.Title.Trim();
        memory.Summary = input.Summary.Trim();
        memory.Content = input.Content.Trim();
        memory.Kind = input.Kind;
        memory.SourceKind = input.SourceKind;
        memory.SourceReference = input.SourceReference.Trim();
        memory.Importance = Math.Clamp(input.Importance, 1, 5);
        memory.IsPinned = input.IsPinned;
        memory.OccurredUtc = input.OccurredUtc;
        memory.WingId = wingId;
        memory.RoomId = room?.Id;
        memory.UpdatedUtc = now;

        if (!isExistingMemory)
        {
            memory.VerificationStatus = MemoryVerificationStatus.Unverified;
        }
        else if (materialChangeDetected)
        {
            memory.VerificationStatus = MemoryVerificationStatus.NeedsReview;
            memory.ReviewAfterUtc = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var tagNames = SuggestTagsForDraft(input);
        await SyncTagsAsync(memory.Id, tagNames, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await PublishMemoryEventAsync(
            isExistingMemory ? "memory.updated" : "memory.created",
            memory.Id,
            memory.Title,
            memory.Summary,
            cancellationToken);
        return memory.Id;
    }

    public async Task MarkMemoryVerifiedAsync(Guid id, CancellationToken cancellationToken)
    {
        var memory = await _dbContext.Memories.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Memory entry not found.");

        var now = DateTime.UtcNow;
        memory.VerificationStatus = MemoryVerificationStatus.Verified;
        memory.LastVerifiedUtc = now;
        memory.ReviewAfterUtc = now.AddDays(MemoryTrustHelper.DefaultReviewWindowDays);
        memory.UpdatedUtc = now;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await PublishMemoryEventAsync("memory.verified", memory.Id, memory.Title, "Memory marked verified.", cancellationToken);
    }

    public async Task MarkMemoryNeedsReviewAsync(Guid id, CancellationToken cancellationToken)
    {
        var memory = await _dbContext.Memories.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Memory entry not found.");

        var now = DateTime.UtcNow;
        memory.VerificationStatus = MemoryVerificationStatus.NeedsReview;
        memory.ReviewAfterUtc = now;
        memory.UpdatedUtc = now;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await PublishMemoryEventAsync("memory.needs-review", memory.Id, memory.Title, "Memory marked for review.", cancellationToken);
    }

    public async Task UpdateMemoryTagsAsync(Guid id, string tagsText, CancellationToken cancellationToken)
    {
        var memory = await _dbContext.Memories.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Memory entry not found.");

        memory.UpdatedUtc = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        await SyncTagsAsync(id, ParseTags(tagsText), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await PublishMemoryEventAsync("memory.tags-updated", memory.Id, memory.Title, "Memory tags updated.", cancellationToken);
    }

    public async Task MarkMemoryActiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var memory = await _dbContext.Memories.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Memory entry not found.");

        var now = DateTime.UtcNow;
        memory.LifecycleState = MemoryLifecycleState.Active;
        memory.SupersededByMemoryId = null;
        memory.LifecycleReason = string.Empty;
        memory.LifecycleChangedUtc = now;
        memory.ArchivedUtc = null;
        memory.UpdatedUtc = now;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await PublishMemoryEventAsync("memory.restored", memory.Id, memory.Title, "Memory restored to active state.", cancellationToken);
    }

    public async Task ArchiveMemoryAsync(Guid id, string? reason, CancellationToken cancellationToken)
    {
        var memory = await _dbContext.Memories.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Memory entry not found.");

        var now = DateTime.UtcNow;
        memory.LifecycleState = MemoryLifecycleState.Archived;
        memory.SupersededByMemoryId = null;
        memory.LifecycleReason = (reason ?? string.Empty).Trim();
        memory.LifecycleChangedUtc = now;
        memory.ArchivedUtc = now;
        memory.IsPinned = false;
        memory.UpdatedUtc = now;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await PublishMemoryEventAsync("memory.archived", memory.Id, memory.Title, memory.LifecycleReason, cancellationToken);
    }

    public async Task SupersedeMemoryAsync(Guid id, Guid replacementMemoryId, string? reason, CancellationToken cancellationToken)
    {
        if (id == replacementMemoryId)
        {
            throw new InvalidOperationException("A memory cannot supersede itself.");
        }

        var memory = await _dbContext.Memories.FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new InvalidOperationException("Memory entry not found.");
        var replacement = await _dbContext.Memories.FirstOrDefaultAsync(x => x.Id == replacementMemoryId, cancellationToken)
            ?? throw new InvalidOperationException("Replacement memory not found.");

        if (replacement.LifecycleState != MemoryLifecycleState.Active)
        {
            throw new InvalidOperationException("Replacement memory must be active.");
        }

        var cursor = replacement;
        for (var depth = 0; depth < 50 && cursor.SupersededByMemoryId.HasValue; depth++)
        {
            if (cursor.SupersededByMemoryId.Value == id)
            {
                throw new InvalidOperationException("Supersession cycles are not allowed.");
            }

            var next = await _dbContext.Memories.FirstOrDefaultAsync(x => x.Id == cursor.SupersededByMemoryId.Value, cancellationToken);
            if (next is null)
            {
                break;
            }

            cursor = next;
        }

        var now = DateTime.UtcNow;
        memory.LifecycleState = MemoryLifecycleState.Superseded;
        memory.SupersededByMemoryId = replacementMemoryId;
        memory.LifecycleReason = (reason ?? string.Empty).Trim();
        memory.LifecycleChangedUtc = now;
        memory.ArchivedUtc = null;
        memory.IsPinned = false;
        memory.UpdatedUtc = now;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await PublishMemoryEventAsync("memory.superseded", memory.Id, memory.Title, memory.LifecycleReason, cancellationToken);
    }

    public async Task BulkUpdateMemoryGovernanceAsync(MemoryBulkGovernanceInput input, CancellationToken cancellationToken)
    {
        var memoryIds = input.MemoryIds
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToArray();

        foreach (var memoryId in memoryIds)
        {
            switch (input.Action)
            {
                case MemoryBulkGovernanceAction.Verify:
                    await MarkMemoryVerifiedAsync(memoryId, cancellationToken);
                    break;
                case MemoryBulkGovernanceAction.MarkNeedsReview:
                    await MarkMemoryNeedsReviewAsync(memoryId, cancellationToken);
                    break;
                case MemoryBulkGovernanceAction.Archive:
                    await ArchiveMemoryAsync(memoryId, input.Reason, cancellationToken);
                    break;
                case MemoryBulkGovernanceAction.RestoreActive:
                    await MarkMemoryActiveAsync(memoryId, cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported memory governance action.");
            }
        }
    }

    public async Task<MemoryGovernanceQueueViewModel> GetMemoryGovernanceQueueAsync(CancellationToken cancellationToken)
        => await BuildGovernanceQueueAsync(cancellationToken);

    public async Task<Guid> CreateWingAsync(WingEditorInput input, CancellationToken cancellationToken)
    {
        var trimmedName = input.Name.Trim();
        var normalizedName = trimmedName.ToLowerInvariant();
        if (await _dbContext.Wings.AnyAsync(x => x.Name.ToLower() == normalizedName, cancellationToken))
        {
            throw new InvalidOperationException("A wing with that name already exists.");
        }

        var wing = new Wing
        {
            Name = trimmedName,
            Description = input.Description.Trim(),
            Slug = await BuildUniqueWingSlugAsync(trimmedName, cancellationToken)
        };

        _dbContext.Wings.Add(wing);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new InvalidOperationException("A wing with that name already exists.");
        }

        return wing.Id;
    }

    public async Task<Guid> CreateRoomAsync(RoomEditorInput input, CancellationToken cancellationToken)
    {
        if (!await _dbContext.Wings.AnyAsync(x => x.Id == input.WingId, cancellationToken))
        {
            throw new InvalidOperationException("The selected wing no longer exists.");
        }

        var room = new Room
        {
            WingId = input.WingId,
            Name = input.Name.Trim(),
            Description = input.Description.Trim(),
            Slug = await BuildUniqueRoomSlugAsync(input.WingId, input.Name, cancellationToken)
        };

        _dbContext.Rooms.Add(room);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _eventPublisher.PublishAsync(new FocusMcpPublishedEvent
        {
            EventType = "room.created",
            Title = room.Name,
            Description = room.Description,
            ResourceUris = ["focus://system/metrics", "focus://workspace", "focus://recent-changes"]
        }, cancellationToken);
        return room.Id;
    }

    public async Task<Guid> CreateTodoAsync(TodoEditorInput input, CancellationToken cancellationToken)
    {
        var todo = new TodoEntry
        {
            Title = input.Title.Trim(),
            Details = input.Details.Trim(),
            Status = input.Status,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
            CompletedUtc = input.Status == TodoStatus.Done ? DateTime.UtcNow : null
        };

        _dbContext.Todos.Add(todo);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await PublishTodoEventAsync("todo.created", todo.Id, todo.Title, todo.Details, cancellationToken);
        return todo.Id;
    }

    public async Task UpdateTodoStatusAsync(Guid id, TodoStatus status, CancellationToken cancellationToken)
    {
        var todo = await _dbContext.Todos.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (todo is null)
        {
            throw new InvalidOperationException("That todo no longer exists.");
        }

        todo.Status = status;
        todo.UpdatedUtc = DateTime.UtcNow;
        todo.CompletedUtc = status == TodoStatus.Done ? DateTime.UtcNow : null;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await PublishTodoEventAsync("todo.status-updated", todo.Id, todo.Title, status.ToString(), cancellationToken);
    }

    public async Task UpdateTodoAsync(Guid id, TodoEditorInput input, CancellationToken cancellationToken)
    {
        var todo = await _dbContext.Todos.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (todo is null)
        {
            throw new InvalidOperationException("That todo no longer exists.");
        }

        todo.Title = input.Title.Trim();
        todo.Details = input.Details.Trim();
        todo.Status = input.Status;
        todo.UpdatedUtc = DateTime.UtcNow;
        todo.CompletedUtc = input.Status == TodoStatus.Done ? todo.CompletedUtc ?? todo.UpdatedUtc : null;
        await _dbContext.SaveChangesAsync(cancellationToken);
        await PublishTodoEventAsync("todo.updated", todo.Id, todo.Title, todo.Details, cancellationToken);
    }

    public async Task DeleteTodoAsync(Guid id, CancellationToken cancellationToken)
    {
        var todo = await _dbContext.Todos.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (todo is null)
        {
            throw new InvalidOperationException("That todo no longer exists.");
        }

        _dbContext.Todos.Remove(todo);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _eventPublisher.PublishAsync(new FocusMcpPublishedEvent
        {
            EventType = "todo.deleted",
            Title = todo.Title,
            Description = "Todo deleted.",
            ResourceUris = ["focus://todos/board", "focus://workspace", "focus://recent-changes"]
        }, cancellationToken);
    }

    public async Task<string?> GetWingSlugAsync(Guid wingId, CancellationToken cancellationToken)
    {
        return await _dbContext.Wings
            .AsNoTracking()
            .Where(x => x.Id == wingId)
            .Select(x => x.Slug)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<PalaceApiSummaryViewModel> GetApiSummaryAsync(CancellationToken cancellationToken)
    {
        var dashboard = await GetDashboardAsync((ContextBriefInput?)null, cancellationToken);
        return new PalaceApiSummaryViewModel
        {
            Stats = dashboard.Stats,
            Wings = dashboard.Wings
        };
    }

    public async Task<IReadOnlyCollection<MemoryCardViewModel>> SearchMemoriesAsync(string? query, Guid? wingId, Guid? roomId, MemoryKind? kind, string? tag, DateTime? updatedSince, CancellationToken cancellationToken)
        => await SearchMemoriesAsync(query, wingId, roomId, kind, tag, updatedSince, includeRetired: false, lifecycleState: null, verificationStatus: null, cancellationToken);

    public async Task<IReadOnlyCollection<MemoryCardViewModel>> SearchMemoriesAsync(
        string? query,
        Guid? wingId,
        Guid? roomId,
        MemoryKind? kind,
        string? tag,
        DateTime? updatedSince,
        bool includeRetired,
        MemoryLifecycleState? lifecycleState,
        MemoryVerificationStatus? verificationStatus,
        CancellationToken cancellationToken)
    {
        var memories = await BuildMemoryQuery(query, wingId, roomId, kind, tag, updatedSince, includeRetired, lifecycleState, verificationStatus)
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.UpdatedUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        return memories.Select(MapCard).ToArray();
    }

    private IQueryable<MemoryEntry> BuildMemoryQuery(
        string? query = null,
        Guid? wingId = null,
        Guid? roomId = null,
        MemoryKind? kind = null,
        string? tag = null,
        DateTime? updatedSince = null,
        bool includeRetired = false,
        MemoryLifecycleState? lifecycleState = null,
        MemoryVerificationStatus? verificationStatus = null)
    {
        var memories = _dbContext.Memories
            .AsNoTracking()
            .Include(x => x.SupersededByMemory)
            .Include(x => x.Wing)
            .Include(x => x.Room)
            .Include(x => x.MemoryTags)
                .ThenInclude(x => x.Tag)
            .AsQueryable();

        if (!includeRetired)
        {
            memories = memories.Where(x => x.LifecycleState == MemoryLifecycleState.Active);
        }
        else if (lifecycleState.HasValue)
        {
            memories = memories.Where(x => x.LifecycleState == lifecycleState.Value);
        }

        if (verificationStatus.HasValue)
        {
            memories = memories.Where(x => x.VerificationStatus == verificationStatus.Value);
        }

        if (wingId.HasValue)
        {
            memories = memories.Where(x => x.WingId == wingId.Value);
        }

        if (roomId.HasValue)
        {
            memories = memories.Where(x => x.RoomId == roomId.Value);
        }

        if (kind.HasValue)
        {
            memories = memories.Where(x => x.Kind == kind.Value);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalized = query.Trim().ToLowerInvariant();
            memories = memories.Where(x =>
                x.Title.ToLower().Contains(normalized) ||
                x.Summary.ToLower().Contains(normalized) ||
                x.Content.ToLower().Contains(normalized) ||
                x.MemoryTags.Any(mt => mt.Tag!.Name.ToLower().Contains(normalized)));
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var tagSlug = SlugUtility.CreateSlug(tag);
            memories = memories.Where(x => x.MemoryTags.Any(mt => mt.Tag!.Slug == tagSlug));
        }

        if (updatedSince.HasValue)
        {
            memories = memories.Where(x => x.UpdatedUtc >= updatedSince.Value);
        }

        return memories;
    }

    private Task PublishMemoryEventAsync(string eventType, Guid memoryId, string title, string description, CancellationToken cancellationToken)
    {
        return _eventPublisher.PublishAsync(new FocusMcpPublishedEvent
        {
            EventType = eventType,
            Title = title,
            Description = description,
            ResourceUris = ["focus://recent-changes", "focus://workspace", "focus://workspace/bootstrap", "focus://memories/governance", "focus://system/metrics", $"focus://memories/{memoryId}"]
        }, cancellationToken);
    }

    private Task PublishTodoEventAsync(string eventType, Guid todoId, string title, string description, CancellationToken cancellationToken)
    {
        return _eventPublisher.PublishAsync(new FocusMcpPublishedEvent
        {
            EventType = eventType,
            Title = title,
            Description = description,
            ResourceUris = ["focus://todos/board", "focus://workspace", "focus://recent-changes", $"focus://todos/{todoId}"]
        }, cancellationToken);
    }

    private IQueryable<TodoEntry> BuildTodoQuery(bool includeDone)
    {
        var todos = _dbContext.Todos
            .AsNoTracking()
            .AsQueryable();

        if (!includeDone)
        {
            todos = todos.Where(x => x.Status != TodoStatus.Done);
        }

        return todos
            .OrderBy(x => x.Status == TodoStatus.InProgress ? 0 : x.Status == TodoStatus.Pending ? 1 : x.Status == TodoStatus.Blocked ? 2 : 3)
            .ThenByDescending(x => x.UpdatedUtc)
            .ThenByDescending(x => x.CreatedUtc);
    }

    private async Task<PalaceStatsViewModel> BuildStatsAsync(CancellationToken cancellationToken)
    {
        var visibleSkillCount = (await GetMergedSkillsAsync(null, null, null, false, false, cancellationToken)).Count;
        return new PalaceStatsViewModel
        {
            WingCount = await _dbContext.Wings.CountAsync(cancellationToken),
            RoomCount = await _dbContext.Rooms.CountAsync(cancellationToken),
            MemoryCount = await _dbContext.Memories.CountAsync(x => x.LifecycleState == MemoryLifecycleState.Active, cancellationToken),
            SkillCount = visibleSkillCount,
            PinnedCount = await _dbContext.Memories.CountAsync(x => x.LifecycleState == MemoryLifecycleState.Active && x.IsPinned, cancellationToken),
            TagCount = await _dbContext.Tags.CountAsync(cancellationToken),
            OpenTodoCount = await _dbContext.Todos.CountAsync(x => x.Status != TodoStatus.Done, cancellationToken),
            CompletedTodoCount = await _dbContext.Todos.CountAsync(x => x.Status == TodoStatus.Done, cancellationToken),
            OpenTicketCount = await _dbContext.Tickets.CountAsync(x => x.Status != TicketStatus.Completed, cancellationToken),
            CompletedTicketCount = await _dbContext.Tickets.CountAsync(x => x.Status == TicketStatus.Completed, cancellationToken)
        };
    }

    private async Task<IReadOnlyCollection<SelectListItem>> BuildWingOptionsAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Wings
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new SelectListItem(x.Name, x.Id.ToString()))
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyCollection<SelectListItem>> BuildRoomOptionsAsync(Guid? wingId, CancellationToken cancellationToken)
    {
        await NormalizeWingLevelMemoriesIntoGeneralRoomsAsync(cancellationToken);

        var query = _dbContext.Rooms
            .AsNoTracking()
            .Include(x => x.Wing)
            .AsQueryable();

        if (wingId.HasValue)
        {
            query = query.Where(x => x.WingId == wingId.Value);
        }

        return await query
            .OrderBy(x => x.Wing!.Name)
            .ThenBy(x => x.Name)
            .Select(x => new SelectListItem($"{x.Wing!.Name} / {x.Name}", x.Id.ToString()))
            .ToListAsync(cancellationToken);
    }

    private async Task NormalizeWingLevelMemoriesIntoGeneralRoomsAsync(CancellationToken cancellationToken)
    {
        var wingIds = await _dbContext.Memories
            .Where(x => x.WingId.HasValue && x.RoomId == null)
            .Select(x => x.WingId!.Value)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        if (wingIds.Length == 0)
        {
            return;
        }

        foreach (var wingId in wingIds)
        {
            var generalRoom = await GetOrCreateGeneralRoomAsync(wingId, cancellationToken);
            var memories = await _dbContext.Memories
                .Where(x => x.WingId == wingId && x.RoomId == null)
                .ToListAsync(cancellationToken);
            foreach (var memory in memories)
            {
                memory.RoomId = generalRoom.Id;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Room> GetOrCreateGeneralRoomAsync(Guid wingId, CancellationToken cancellationToken)
    {
        var existingRoom = await _dbContext.Rooms.FirstOrDefaultAsync(
            x => x.WingId == wingId && x.Name.ToLower() == "general",
            cancellationToken);
        if (existingRoom is not null)
        {
            return existingRoom;
        }

        var room = new Room
        {
            WingId = wingId,
            Name = "General",
            Description = "Default room for wing memories that do not fit a more specific category.",
            Slug = await BuildUniqueRoomSlugAsync(wingId, "General", cancellationToken)
        };

        _dbContext.Rooms.Add(room);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return room;
    }

    public async Task<IReadOnlyCollection<WingSummaryViewModel>> GetWingSummariesAsync(string? query, CancellationToken cancellationToken)
    {
        var wings = await BuildWingSummariesAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(query))
        {
            return wings;
        }

        var trimmedQuery = query.Trim();
        return wings
            .Where(x =>
                x.Name.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) ||
                x.Slug.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) ||
                x.Description.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public async Task<IReadOnlyCollection<RoomBrowseItemViewModel>> GetRoomsAsync(Guid? wingId, string? wingSlug, string? wingName, string? query, CancellationToken cancellationToken)
    {
        var rooms = await _dbContext.Rooms
            .AsNoTracking()
            .Include(x => x.Wing)
            .Include(x => x.Memories)
            .OrderBy(x => x.Wing!.Name)
            .ThenBy(x => x.Name)
            .Select(x => new RoomBrowseItemViewModel
            {
                RoomId = x.Id,
                WingId = x.WingId,
                WingName = x.Wing!.Name,
                WingSlug = x.Wing.Slug,
                RoomName = x.Name,
                RoomDescription = x.Description,
                MemoryCount = x.Memories.Count(m => m.LifecycleState == MemoryLifecycleState.Active)
            })
            .ToListAsync(cancellationToken);

        IEnumerable<RoomBrowseItemViewModel> filtered = rooms;
        if (wingId.HasValue)
        {
            filtered = filtered.Where(x => x.WingId == wingId.Value);
        }
        else if (!string.IsNullOrWhiteSpace(wingSlug))
        {
            filtered = filtered.Where(x => string.Equals(x.WingSlug, wingSlug.Trim(), StringComparison.OrdinalIgnoreCase));
        }
        else if (!string.IsNullOrWhiteSpace(wingName))
        {
            filtered = filtered.Where(x => string.Equals(x.WingName, wingName.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var trimmedQuery = query.Trim();
            filtered = filtered.Where(x =>
                x.RoomName.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) ||
                x.RoomDescription.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) ||
                x.WingName.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase) ||
                x.WingSlug.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase));
        }

        return filtered.ToArray();
    }

    private IQueryable<SkillEntry> BuildSkillQuery(
        string? query = null,
        SkillCategory? category = null,
        Guid? wingId = null,
        bool pinnedOnly = false,
        bool needsReviewOnly = false)
    {
        var skills = _dbContext.Skills
            .AsNoTracking()
            .Include(x => x.Wing)
            .AsQueryable();

        if (category.HasValue)
        {
            skills = skills.Where(x => x.Category == category.Value);
        }

        if (wingId.HasValue)
        {
            skills = skills.Where(x => x.WingId == wingId.Value);
        }

        if (pinnedOnly)
        {
            skills = skills.Where(x => x.IsPinned);
        }

        if (needsReviewOnly)
        {
            var now = DateTime.UtcNow;
            skills = skills.Where(x => x.ReviewAfterUtc != null && x.ReviewAfterUtc <= now);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var normalized = query.Trim().ToLowerInvariant();
            skills = skills.Where(x =>
                x.Name.ToLower().Contains(normalized) ||
                x.Summary.ToLower().Contains(normalized) ||
                x.WhenToUse.ToLower().Contains(normalized) ||
                x.Flow.ToLower().Contains(normalized) ||
                x.TriggerHintsText.ToLower().Contains(normalized) ||
                (x.Wing != null && x.Wing.Name.ToLower().Contains(normalized)));
        }

        return skills;
    }

    private async Task<IReadOnlyCollection<ResolvedSkillRecord>> GetMergedSkillsAsync(
        string? query,
        SkillCategory? category,
        Guid? wingId,
        bool pinnedOnly,
        bool needsReviewOnly,
        CancellationToken cancellationToken)
    {
        var dbSkills = await BuildSkillQuery(query, category, wingId, pinnedOnly, needsReviewOnly)
            .ToListAsync(cancellationToken);
        var merged = dbSkills
            .Select(skill => new ResolvedSkillRecord(skill))
            .ToDictionary(x => x.Skill.Slug, StringComparer.OrdinalIgnoreCase);

        foreach (var repoSkill in await GetRepoSkillsAsync(cancellationToken))
        {
            if (merged.ContainsKey(repoSkill.Skill.Slug) || !MatchesSkillFilters(repoSkill.Skill, query, category, wingId, pinnedOnly, needsReviewOnly))
            {
                continue;
            }

            merged[repoSkill.Skill.Slug] = repoSkill;
        }

        return merged.Values.ToArray();
    }

    private async Task<IReadOnlyCollection<ResolvedSkillRecord>> GetRepoSkillsAsync(CancellationToken cancellationToken)
    {
        if (_repoSkillCatalogService is null)
        {
            return Array.Empty<ResolvedSkillRecord>();
        }

        var wingLookup = await _dbContext.Wings
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Slug, StringComparer.OrdinalIgnoreCase, cancellationToken);

        return _repoSkillCatalogService.GetAllSkills()
            .Select(skill => CreateRepoSkillRecord(skill, wingLookup))
            .ToArray();
    }

    private async Task<ResolvedSkillRecord?> GetRepoSkillAsync(string slug, CancellationToken cancellationToken)
    {
        if (_repoSkillCatalogService is null)
        {
            return null;
        }

        var repoSkill = _repoSkillCatalogService.GetSkill(slug);
        if (repoSkill is null)
        {
            return null;
        }

        var wingLookup = await _dbContext.Wings
            .AsNoTracking()
            .ToDictionaryAsync(x => x.Slug, StringComparer.OrdinalIgnoreCase, cancellationToken);

        return CreateRepoSkillRecord(repoSkill, wingLookup);
    }

    private static ResolvedSkillRecord CreateRepoSkillRecord(RepoSkillDocument repoSkill, IReadOnlyDictionary<string, Wing> wingLookup)
    {
        var skill = CloneSkill(repoSkill.Skill);
        if (!string.IsNullOrWhiteSpace(repoSkill.WingSlug) && wingLookup.TryGetValue(repoSkill.WingSlug, out var wing))
        {
            skill.WingId = wing.Id;
            skill.Wing = wing;
        }

        return new ResolvedSkillRecord(skill, true, repoSkill.SourceLabel, repoSkill.SourcePath);
    }

    private static SkillEntry CloneSkill(SkillEntry skill)
    {
        return new SkillEntry
        {
            Id = skill.Id,
            WingId = skill.WingId,
            Name = skill.Name,
            Slug = skill.Slug,
            Summary = skill.Summary,
            Category = skill.Category,
            WhenToUse = skill.WhenToUse,
            Flow = skill.Flow,
            ExamplesText = skill.ExamplesText,
            TriggerHintsText = skill.TriggerHintsText,
            IsPinned = skill.IsPinned,
            UseCount = skill.UseCount,
            LastUsedUtc = skill.LastUsedUtc,
            LastReviewedUtc = skill.LastReviewedUtc,
            ReviewAfterUtc = skill.ReviewAfterUtc,
            CreatedUtc = skill.CreatedUtc,
            UpdatedUtc = skill.UpdatedUtc,
            Wing = skill.Wing
        };
    }

    private async Task<IReadOnlyCollection<SkillCardViewModel>> GetFeaturedSkillsAsync(int limit, CancellationToken cancellationToken)
    {
        var skills = await GetMergedSkillsAsync(null, null, null, false, false, cancellationToken);
        return skills
            .OrderByDescending(x => x.Skill.IsPinned)
            .ThenBy(x => x.Skill.Category)
            .ThenBy(x => x.Skill.Name)
            .Take(limit)
            .Select(MapSkillCard)
            .ToArray();
    }

    private static IReadOnlyCollection<SkillCardViewModel> MergeSkillCards(
        IReadOnlyCollection<SkillCardViewModel>? primary,
        IReadOnlyCollection<SkillCardViewModel>? secondary,
        int limit)
    {
        var merged = new Dictionary<string, SkillCardViewModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in primary ?? Array.Empty<SkillCardViewModel>())
        {
            merged.TryAdd(skill.Slug, skill);
        }

        foreach (var skill in secondary ?? Array.Empty<SkillCardViewModel>())
        {
            merged.TryAdd(skill.Slug, skill);
        }

        return merged.Values.Take(limit).ToArray();
    }

    private static bool MatchesSkillFilters(
        SkillEntry skill,
        string? query,
        SkillCategory? category,
        Guid? wingId,
        bool pinnedOnly,
        bool needsReviewOnly)
    {
        if (category.HasValue && skill.Category != category.Value)
        {
            return false;
        }

        if (wingId.HasValue && skill.WingId != wingId.Value)
        {
            return false;
        }

        if (pinnedOnly && !skill.IsPinned)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        if (needsReviewOnly && !(skill.ReviewAfterUtc != null && skill.ReviewAfterUtc <= now))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var normalized = query.Trim();
        return skill.Name.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
               skill.Summary.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
               skill.WhenToUse.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
               skill.Flow.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
               skill.TriggerHintsText.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
               (skill.Wing?.Name?.Contains(normalized, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private async Task<IReadOnlyCollection<WingSummaryViewModel>> BuildWingSummariesAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Wings
            .AsNoTracking()
            .Include(x => x.Rooms)
            .Include(x => x.Memories)
            .OrderBy(x => x.Name)
            .Select(x => new WingSummaryViewModel
            {
                Id = x.Id,
                Name = x.Name,
                Slug = x.Slug,
                Description = x.Description,
                RoomCount = x.Rooms.Count,
                MemoryCount = x.Memories.Count(m => m.LifecycleState == MemoryLifecycleState.Active),
                LatestActivityUtc = x.Memories.Where(m => m.LifecycleState == MemoryLifecycleState.Active).OrderByDescending(m => m.UpdatedUtc).Select(m => m.UpdatedUtc).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyCollection<SelectListItem>> BuildSupersedeTargetOptionsAsync(Guid memoryId, Guid? wingId, CancellationToken cancellationToken)
    {
        return await BuildMemoryQuery(wingId: wingId)
            .Where(x => x.Id != memoryId)
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.UpdatedUtc)
            .Take(40)
            .Select(x => new SelectListItem(x.Title, x.Id.ToString()))
            .ToListAsync(cancellationToken);
    }

    private async Task<MemoryGovernanceQueueViewModel> BuildGovernanceQueueAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var items = await BuildMemoryQuery(includeRetired: true)
            .Where(x =>
                x.LifecycleState != MemoryLifecycleState.Active ||
                x.VerificationStatus == MemoryVerificationStatus.NeedsReview ||
                (x.VerificationStatus == MemoryVerificationStatus.Unverified && x.UpdatedUtc <= now.AddDays(-14)))
            .OrderByDescending(x => x.IsPinned)
            .ThenBy(x => x.LifecycleState == MemoryLifecycleState.Active ? 0 : 1)
            .ThenBy(x => x.LastReferencedUtc ?? DateTime.MinValue)
            .ThenBy(x => x.UpdatedUtc)
            .Take(60)
            .ToListAsync(cancellationToken);

        var archivedCount = await _dbContext.Memories.CountAsync(x => x.LifecycleState == MemoryLifecycleState.Archived, cancellationToken);
        var supersededCount = await _dbContext.Memories.CountAsync(x => x.LifecycleState == MemoryLifecycleState.Superseded, cancellationToken);
        var needsReviewCount = await _dbContext.Memories.CountAsync(x => x.LifecycleState == MemoryLifecycleState.Active && x.VerificationStatus == MemoryVerificationStatus.NeedsReview, cancellationToken);
        var unverifiedActiveCount = await _dbContext.Memories.CountAsync(x => x.LifecycleState == MemoryLifecycleState.Active && x.VerificationStatus == MemoryVerificationStatus.Unverified, cancellationToken);

        return new MemoryGovernanceQueueViewModel
        {
            Items = items.Select(MapCard).ToArray(),
            Summary = new MemoryGovernanceSummaryViewModel
            {
                QueueItemCount = items.Count,
                ArchivedCount = archivedCount,
                SupersededCount = supersededCount,
                NeedsReviewCount = needsReviewCount,
                UnverifiedActiveCount = unverifiedActiveCount
            },
            ArchivedCount = archivedCount,
            SupersededCount = supersededCount,
            NeedsReviewCount = needsReviewCount,
            UnverifiedActiveCount = unverifiedActiveCount
        };
    }

    private async Task TouchMemoryReferencesAsync(IReadOnlyCollection<Guid> memoryIds, CancellationToken cancellationToken)
    {
        if (memoryIds.Count == 0)
        {
            return;
        }

        var ids = memoryIds.Distinct().ToArray();
        var memories = await _dbContext.Memories
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(cancellationToken);

        if (memories.Count == 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var memory in memories)
        {
            memory.LastReferencedUtc = now;
            memory.ReferenceCount += 1;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static TodoItemViewModel MapTodo(TodoEntry todo)
    {
        var preview = BuildTodoPreview(todo.Details);
        return new TodoItemViewModel
        {
            Id = todo.Id,
            Title = todo.Title,
            Details = todo.Details,
            PreviewDetails = preview,
            HasMoreDetails = !string.Equals(preview, todo.Details, StringComparison.Ordinal),
            Status = todo.Status,
            StatusLabel = todo.Status switch
            {
                TodoStatus.InProgress => "In progress",
                TodoStatus.Pending => "Pending",
                TodoStatus.Blocked => "Blocked",
                TodoStatus.Done => "Done",
                _ => todo.Status.ToString()
            },
            CreatedUtc = todo.CreatedUtc,
            UpdatedUtc = todo.UpdatedUtc,
            CompletedUtc = todo.CompletedUtc
        };
    }

    private static string BuildTodoPreview(string details)
    {
        if (string.IsNullOrWhiteSpace(details))
        {
            return string.Empty;
        }

        const int maxLength = 240;
        var normalized = details.Replace("\r\n", "\n").Trim();
        return normalized.Length <= maxLength
            ? normalized
            : $"{normalized[..maxLength].TrimEnd()}...";
    }

    private static async Task<MemoryEntry?> FindMemoryAsync(IQueryable<MemoryEntry> query, Guid id, CancellationToken cancellationToken)
    {
        var memory = await query.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (memory is not null)
        {
            return memory;
        }

        var candidates = await query.ToListAsync(cancellationToken);
        return candidates.FirstOrDefault(x => x.Id == id);
    }

    private async Task SyncTagsAsync(Guid memoryId, IReadOnlyCollection<string> tagNames, CancellationToken cancellationToken)
    {
        var desiredSlugs = tagNames.Select(SlugUtility.CreateSlug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingTags = await _dbContext.Tags
            .Where(x => desiredSlugs.Contains(x.Slug))
            .ToListAsync(cancellationToken);

        foreach (var tagName in tagNames)
        {
            var slug = SlugUtility.CreateSlug(tagName);
            if (existingTags.Any(x => x.Slug == slug))
            {
                continue;
            }

            var tag = new Tag
            {
                Name = tagName,
                Slug = slug
            };
            _dbContext.Tags.Add(tag);
            existingTags.Add(tag);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        var existingMemoryTags = await _dbContext.MemoryTags
            .Where(x => x.MemoryEntryId == memoryId)
            .ToListAsync(cancellationToken);

        if (existingMemoryTags.Count > 0)
        {
            _dbContext.MemoryTags.RemoveRange(existingMemoryTags);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var storedTags = await LoadStoredTagsAsync(desiredSlugs, cancellationToken);
        foreach (var tag in storedTags.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
        {
            await InsertMemoryTagAsync(memoryId, tag.Id, cancellationToken);
        }
    }

    private async Task<IReadOnlyCollection<StoredTagRecord>> LoadStoredTagsAsync(IReadOnlyCollection<string> desiredSlugs, CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        var parameterNames = desiredSlugs
            .Select((slug, index) =>
            {
                var parameterName = $"$slug{index}";
                var parameter = command.CreateParameter();
                parameter.ParameterName = parameterName;
                parameter.Value = slug;
                command.Parameters.Add(parameter);
                return parameterName;
            })
            .ToArray();

        command.CommandText = $"SELECT \"Id\", \"Name\", \"Slug\" FROM \"Tags\" WHERE \"Slug\" IN ({string.Join(", ", parameterNames)})";
        var results = new List<StoredTagRecord>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(new StoredTagRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        if (shouldClose)
        {
            await connection.CloseAsync();
        }

        return results;
    }

    private async Task InsertMemoryTagAsync(Guid memoryId, string tagId, CancellationToken cancellationToken)
    {
        var connection = _dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO \"MemoryTags\" (\"MemoryEntryId\", \"TagId\") VALUES ($memoryId, $tagId)";
        var memoryParameter = command.CreateParameter();
        memoryParameter.ParameterName = "$memoryId";
        memoryParameter.Value = memoryId.ToString().ToUpperInvariant();
        command.Parameters.Add(memoryParameter);

        var tagParameter = command.CreateParameter();
        tagParameter.ParameterName = "$tagId";
        tagParameter.Value = tagId;
        command.Parameters.Add(tagParameter);
        await command.ExecuteNonQueryAsync(cancellationToken);

        if (shouldClose)
        {
            await connection.CloseAsync();
        }
    }

    private sealed record StoredTagRecord(string Id, string Name, string Slug);

    private async Task<string> BuildUniqueWingSlugAsync(string name, CancellationToken cancellationToken)
    {
        var slug = SlugUtility.CreateSlug(name);
        var suffix = 2;
        while (await _dbContext.Wings.AnyAsync(x => x.Slug == slug, cancellationToken))
        {
            slug = $"{SlugUtility.CreateSlug(name)}-{suffix++}";
        }

        return slug;
    }

    private async Task<string> BuildUniqueRoomSlugAsync(Guid wingId, string name, CancellationToken cancellationToken)
    {
        var baseSlug = SlugUtility.CreateSlug(name);
        var slug = baseSlug;
        var suffix = 2;
        while (await _dbContext.Rooms.AnyAsync(x => x.WingId == wingId && x.Slug == slug, cancellationToken))
        {
            slug = $"{baseSlug}-{suffix++}";
        }

        return slug;
    }

    private static IReadOnlyCollection<string> ParseTags(string tagsText)
    {
        return tagsText
            .Split([',', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static MemoryEditorInput BuildQuickCaptureMemoryInput(QuickCaptureInput input)
    {
        var rawText = input.RawText.Trim();
        var lines = rawText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var title = lines.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(title))
        {
            title = rawText.Length <= 80 ? rawText : rawText[..80].TrimEnd();
        }

        title = title.Length <= 200 ? title : title[..200].TrimEnd();
        var summary = rawText.Length <= 220 ? rawText : rawText[..220].TrimEnd() + "...";

        return new MemoryEditorInput
        {
            Title = title,
            Summary = summary,
            Content = rawText,
            Kind = input.Kind,
            SourceKind = input.SourceKind,
            IsPinned = input.IsPinned,
            WingId = input.WingId,
            TagsText = string.Empty
        };
    }

    private static IReadOnlyCollection<string> TokenizeDraftText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value
            .ToLowerInvariant()
            .Split([' ', '\r', '\n', '\t', ',', '.', ':', ';', '/', '\\', '(', ')', '[', ']', '{', '}', '-', '_', '"', '\''], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(token => token)
            .ToArray();
    }

    private static decimal ScoreDuplicateSimilarity(string left, string right)
    {
        var leftTokens = TokenizeDraftText(left).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rightTokens = TokenizeDraftText(right).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (leftTokens.Count == 0 || rightTokens.Count == 0)
        {
            return 0m;
        }

        var overlap = leftTokens.Intersect(rightTokens, StringComparer.OrdinalIgnoreCase).Count();
        var denominator = Math.Max(leftTokens.Count, rightTokens.Count);
        var tokenScore = denominator == 0 ? 0m : (decimal)overlap / denominator;
        var phraseScore = left.Contains(right, StringComparison.OrdinalIgnoreCase) || right.Contains(left, StringComparison.OrdinalIgnoreCase)
            ? 1m
            : 0m;
        return Math.Round(Math.Max(tokenScore, phraseScore), 3);
    }

    private static decimal ScoreDuplicateSimilarity(MemoryEditorInput draft, MemoryEntry memory)
    {
        var titleScore = ScoreDuplicateSimilarity(draft.Title, memory.Title);
        var summaryScore = ScoreDuplicateSimilarity(draft.Summary, memory.Summary);
        var contentScore = ScoreDuplicateSimilarity(draft.Content, memory.Content);
        var combinedScore = ScoreDuplicateSimilarity(
            $"{draft.Title} {draft.Summary} {draft.Content}",
            $"{memory.Title} {memory.Summary} {memory.Content}");

        var tagTokens = ParseTags(draft.TagsText);
        var memoryTags = memory.MemoryTags.Select(x => x.Tag!.Name).ToArray();
        var tagScore = tagTokens.Count == 0 || memoryTags.Length == 0
            ? 0m
            : ScoreDuplicateSimilarity(string.Join(' ', tagTokens), string.Join(' ', memoryTags));

        var exactTitleLikeMatch =
            !string.IsNullOrWhiteSpace(draft.Title) &&
            (draft.Title.Contains(memory.Title, StringComparison.OrdinalIgnoreCase) ||
             memory.Title.Contains(draft.Title, StringComparison.OrdinalIgnoreCase));

        var weightedScore =
            (titleScore * 0.45m) +
            (summaryScore * 0.20m) +
            (contentScore * 0.20m) +
            (combinedScore * 0.10m) +
            (tagScore * 0.05m);

        if (exactTitleLikeMatch)
        {
            weightedScore = Math.Max(weightedScore, 0.78m);
        }

        if (memory.VerificationStatus == MemoryVerificationStatus.Verified)
        {
            weightedScore += 0.03m;
        }

        return Math.Round(Math.Min(weightedScore, 1m), 3);
    }

    private static IReadOnlyCollection<MemoryCardViewModel> BuildResurfacingMemories(
        IReadOnlyCollection<MemoryEntry> memories,
        ContextPackViewModel? contextPack)
    {
        var contextIds = contextPack?.Memories.Select(x => x.Id).ToHashSet() ?? new HashSet<Guid>();

        return memories
            .Select(MapCard)
            .OrderByDescending(x => contextIds.Contains(x.Id))
            .ThenByDescending(x => x.IsReviewDue)
            .ThenByDescending(x => x.ReferenceCount > 0)
            .ThenBy(x => x.LastReferencedUtc ?? DateTime.MinValue)
            .ThenBy(x => x.UpdatedUtc)
            .Take(4)
            .ToArray();
    }

    private static TicketSummaryViewModel MapDashboardTicketSummary(TicketEntry ticket)
    {
        const int maxLength = 180;
        var preview = string.IsNullOrWhiteSpace(ticket.Description)
            ? string.Empty
            : ticket.Description.Trim().Length <= maxLength
                ? ticket.Description.Trim()
                : $"{ticket.Description.Trim()[..maxLength].TrimEnd()}...";

        return new TicketSummaryViewModel
        {
            Id = ticket.Id,
            ParentTicketId = ticket.ParentTicketId,
            TicketNumber = ticket.TicketNumber,
            Title = ticket.Title,
            Description = ticket.Description,
            PreviewDescription = preview,
            HasMoreDescription = !string.Equals(preview, ticket.Description?.Trim(), StringComparison.Ordinal),
            Status = ticket.Status,
            StatusLabel = MapTicketStatusLabel(ticket.Status),
            Priority = ticket.Priority,
            PriorityLabel = MapTicketPriorityLabel(ticket.Priority),
            Assignee = ticket.Assignee,
            Tags = ParseTags(ticket.TagsText).ToArray(),
            GitBranch = ticket.GitBranch,
            HasGitCommit = !string.IsNullOrWhiteSpace(ticket.GitCommit) && !string.Equals(ticket.GitCommit, "No", StringComparison.OrdinalIgnoreCase),
            CreatedUtc = ticket.CreatedUtc,
            UpdatedUtc = ticket.UpdatedUtc,
            CompletedUtc = ticket.CompletedUtc,
            SubTicketCount = ticket.SubTickets.Count,
            CompletedSubTicketCount = ticket.SubTickets.Count(x => x.Status == TicketStatus.Completed),
            TotalMinutesSpent = ticket.TimeLogs.Sum(x => x.MinutesSpent)
        };
    }

    private static string MapTicketStatusLabel(TicketStatus status) => status switch
    {
        TicketStatus.New => "New",
        TicketStatus.InProgress => "In progress",
        TicketStatus.Blocked => "Blocked",
        TicketStatus.Completed => "Completed",
        _ => status.ToString()
    };

    private static string MapTicketPriorityLabel(TicketPriority priority) => priority switch
    {
        TicketPriority.Low => "Low",
        TicketPriority.Medium => "Medium",
        TicketPriority.High => "High",
        TicketPriority.Critical => "Critical",
        _ => priority.ToString()
    };

    private static string MapDashboardTodoStatusLabel(TodoStatus status) => status switch
    {
        TodoStatus.Pending => "Pending",
        TodoStatus.InProgress => "In progress",
        TodoStatus.Blocked => "Blocked",
        TodoStatus.Done => "Done",
        _ => status.ToString()
    };

    private static ContextPackGoal MapSkillPackGoal(SkillCategory category) => category switch
    {
        SkillCategory.System => ContextPackGoal.Architecture,
        SkillCategory.Tooling => ContextPackGoal.Delivery,
        _ => ContextPackGoal.General
    };

    private static IReadOnlyCollection<DashboardWarningViewModel> BuildDashboardWarnings(
        ContextBriefInput contextInput,
        ContextBriefInput resolvedContextInput,
        DashboardFallbackContextViewModel? fallbackContext,
        ContextPackViewModel? contextPack,
        IReadOnlyCollection<TodoItemViewModel> currentTodos,
        IReadOnlyCollection<TicketSummaryViewModel> activeTickets,
        IReadOnlyCollection<DashboardActivityViewModel> recentActivity,
        IReadOnlyCollection<MemoryCardViewModel> pinnedMemories,
        IReadOnlyCollection<MemoryCardViewModel> recentMemories,
        IReadOnlyCollection<WingSummaryViewModel> wings)
    {
        var now = DateTime.UtcNow;
        var warnings = new List<DashboardWarningViewModel>();
        var stalePinnedMemories = pinnedMemories
            .Where(x => x.IsReviewDue || (x.VerificationStatus == MemoryVerificationStatus.Unverified && x.UpdatedUtc <= now.AddDays(-14)))
            .ToArray();
        var agingUnverifiedCount = recentMemories.Count(x => x.VerificationStatus == MemoryVerificationStatus.Unverified && x.UpdatedUtc <= now.AddDays(-14));

        if (currentTodos.Count == 0 &&
            activeTickets.Count == 0 &&
            string.IsNullOrWhiteSpace(fallbackContext?.CurrentWorkSummary))
        {
            warnings.Add(new DashboardWarningViewModel
            {
                Code = "no-active-work",
                Severity = "warning",
                Message = "No active todos or tickets are currently visible on the dashboard.",
                ActionLabel = "Open Inspect",
                ActionUrl = "/Admin/Inspect"
            });
        }

        if (recentActivity.Count == 0)
        {
            warnings.Add(new DashboardWarningViewModel
            {
                Code = "no-recent-activity",
                Severity = "warning",
                Message = "No recent activity is available, so recency-based context is missing.",
                ActionLabel = "Open Inspect",
                ActionUrl = "/Admin/Inspect"
            });
        }

        if (pinnedMemories.Count == 0)
        {
            warnings.Add(new DashboardWarningViewModel
            {
                Code = "no-pinned-memories",
                Severity = "warning",
                Message = "No pinned memories are available, so high-value durable context is not surfaced.",
                ActionLabel = "Add Memory",
                ActionUrl = "/Palace/NewMemory"
            });
        }
        else if (stalePinnedMemories.Length > 0)
        {
            warnings.Add(new DashboardWarningViewModel
            {
                Code = "stale-pinned-memories",
                Severity = "warning",
                Message = stalePinnedMemories.Length == 1
                    ? "1 pinned memory is no longer trusted as fresh context."
                    : $"{stalePinnedMemories.Length} pinned memories are no longer trusted as fresh context.",
                ActionLabel = "Open Inspect",
                ActionUrl = "/Admin/Inspect"
            });
        }

        if (recentMemories.Count == 0)
        {
            warnings.Add(new DashboardWarningViewModel
            {
                Code = "no-recent-memories",
                Severity = "info",
                Message = "No recent memories are available, so the dashboard cannot surface fresh knowledge.",
                ActionLabel = "Explore Palace",
                ActionUrl = "/Palace/Explore"
            });
        }
        else if (agingUnverifiedCount > 0)
        {
            warnings.Add(new DashboardWarningViewModel
            {
                Code = "aging-unverified-memories",
                Severity = "info",
                Message = agingUnverifiedCount == 1
                    ? "1 recent memory is still unverified and may rot into unreliable context."
                    : $"{agingUnverifiedCount} recent memories are still unverified and may rot into unreliable context.",
                ActionLabel = "Explore Palace",
                ActionUrl = "/Palace/Explore"
            });
        }

        if (wings.Count == 0)
        {
            warnings.Add(new DashboardWarningViewModel
            {
                Code = "no-wings",
                Severity = "warning",
                Message = "No wings are configured, so knowledge is not organized into top-level buckets.",
                ActionLabel = "Add Wing",
                ActionUrl = "/Palace/NewWing"
            });
        }

        if (string.IsNullOrWhiteSpace(contextInput.Question) &&
            string.IsNullOrWhiteSpace(fallbackContext?.SuggestedQuestion))
        {
            warnings.Add(new DashboardWarningViewModel
            {
                Code = "no-context-question",
                Severity = "info",
                Message = "No context question was supplied, so diagnostics do not include task-specific retrieval.",
                ActionLabel = "Open Inspect",
                ActionUrl = "/Admin/Inspect"
            });
        }
        else if (!string.IsNullOrWhiteSpace(resolvedContextInput.Question) &&
                 (contextPack is null || contextPack.TopMatches.Count == 0))
        {
            warnings.Add(new DashboardWarningViewModel
            {
                Code = "no-context-matches",
                Severity = "warning",
                Message = string.IsNullOrWhiteSpace(contextInput.Question)
                    ? "Focus derived a context question from recent signal, but no strong top matches were returned."
                    : "A context question was supplied, but no strong top matches were returned.",
                ActionLabel = "Open Inspect",
                ActionUrl = $"/Admin/Inspect?Question={Uri.EscapeDataString(resolvedContextInput.Question)}&IncludeCompletedWork={resolvedContextInput.IncludeCompletedWork.ToString().ToLowerInvariant()}&ExpandHistory={resolvedContextInput.ExpandHistory.ToString().ToLowerInvariant()}&ResultsPerSection={resolvedContextInput.ResultsPerSection}"
            });
        }

        return warnings;
    }

    private static DashboardFallbackContextViewModel? BuildDashboardFallbackContext(
        ContextBriefInput contextInput,
        IReadOnlyCollection<TodoItemViewModel> currentTodos,
        IReadOnlyCollection<TicketSummaryViewModel> activeTickets,
        IReadOnlyCollection<DashboardActivityViewModel> recentActivity,
        IReadOnlyCollection<MemoryCardViewModel> pinnedMemories,
        IReadOnlyCollection<MemoryCardViewModel> recentMemories)
    {
        var suggestions = new List<string>();
        string sourceLabel;
        string currentWorkSummary;

        var activeTodo = currentTodos.FirstOrDefault(todo => todo.Status == TodoStatus.InProgress) ?? currentTodos.FirstOrDefault();
        if (activeTodo is not null)
        {
            sourceLabel = "Active todo";
            currentWorkSummary = $"Focus is tracking current work through the todo \"{activeTodo.Title}\".";
            AddDashboardSuggestion(suggestions, $"What context should I review before I continue \"{activeTodo.Title}\"?");
        }
        else
        {
            var activeTicket = activeTickets.FirstOrDefault(ticket => ticket.Status == TicketStatus.InProgress) ?? activeTickets.FirstOrDefault();
            if (activeTicket is not null)
            {
                sourceLabel = "Active ticket";
                currentWorkSummary = $"Focus is tracking current work through the ticket \"{activeTicket.TicketNumber} - {activeTicket.Title}\".";
                AddDashboardSuggestion(suggestions, $"What changed recently around \"{activeTicket.Title}\" and what context should I review before continuing?");
            }
            else
            {
                var latestActivity = recentActivity.FirstOrDefault();
                if (latestActivity is not null)
                {
                    sourceLabel = "Recent activity";
                    currentWorkSummary = $"No formal todo or ticket is active, so Focus is using recent activity as the current-work signal. Latest update: {latestActivity.Label} - {latestActivity.Title}.";
                    AddDashboardSuggestion(suggestions, $"What changed recently around \"{latestActivity.Title}\" and what should I read before I continue?");
                }
                else
                {
                    var pinnedMemory = pinnedMemories.FirstOrDefault();
                    if (pinnedMemory is not null)
                    {
                        sourceLabel = "Pinned memory";
                        currentWorkSummary = $"No active work item is visible, so Focus is falling back to pinned memory \"{pinnedMemory.Title}\" as durable context.";
                        AddDashboardSuggestion(suggestions, $"What is the current trusted context for \"{pinnedMemory.Title}\"?");
                    }
                    else
                    {
                        var recentMemory = recentMemories.FirstOrDefault();
                        if (recentMemory is null)
                        {
                            return null;
                        }

                        sourceLabel = "Recent memory";
                        currentWorkSummary = $"No active work item is visible, so Focus is falling back to the recent memory \"{recentMemory.Title}\".";
                        AddDashboardSuggestion(suggestions, $"What recent project context should I review around \"{recentMemory.Title}\"?");
                    }
                }
            }
        }

        var durableMemory = pinnedMemories.FirstOrDefault();
        if (durableMemory is not null)
        {
            AddDashboardSuggestion(suggestions, $"Which pinned memory should I trust before I change \"{durableMemory.Title}\"?");
        }

        var freshMemory = recentMemories.FirstOrDefault();
        if (freshMemory is not null)
        {
            AddDashboardSuggestion(suggestions, $"What changed recently around \"{freshMemory.Title}\"?");
        }

        AddDashboardSuggestion(suggestions, "What changed recently for this project?");

        return new DashboardFallbackContextViewModel
        {
            SourceLabel = sourceLabel,
            CurrentWorkSummary = currentWorkSummary,
            SuggestedQuestion = suggestions.FirstOrDefault() ?? string.Empty,
            SuggestedQuestions = suggestions,
            WasApplied = string.IsNullOrWhiteSpace(contextInput.Question) && suggestions.Count > 0
        };
    }

    private static ContextBriefInput ResolveDashboardContextInput(
        ContextBriefInput contextInput,
        DashboardFallbackContextViewModel? fallbackContext)
    {
        if (!string.IsNullOrWhiteSpace(contextInput.Question) ||
            string.IsNullOrWhiteSpace(fallbackContext?.SuggestedQuestion))
        {
            return CloneContextBriefInput(contextInput);
        }

        var resolvedInput = CloneContextBriefInput(contextInput);
        resolvedInput.Question = fallbackContext.SuggestedQuestion;
        return resolvedInput;
    }

    private static ContextBriefInput CloneContextBriefInput(ContextBriefInput input)
    {
        return new ContextBriefInput
        {
            Question = input.Question,
            IncludeCompletedWork = input.IncludeCompletedWork,
            WingId = input.WingId,
            RoomId = input.RoomId,
            Kind = input.Kind,
            Tag = input.Tag,
            IncludeRetired = input.IncludeRetired,
            ExpandHistory = input.ExpandHistory,
            ResultsPerSection = input.ResultsPerSection,
            PackGoal = input.PackGoal,
            PreferRecentChanges = input.PreferRecentChanges
        };
    }

    private static void AddDashboardSuggestion(List<string> suggestions, string suggestion)
    {
        if (string.IsNullOrWhiteSpace(suggestion) ||
            suggestions.Contains(suggestion, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        suggestions.Add(suggestion);
    }

    private static string BuildWorkspaceExportText(
        IReadOnlyCollection<AgentCardViewModel> recommendedAgents,
        IReadOnlyCollection<SkillCardViewModel> recommendedSkills,
        IReadOnlyCollection<MemoryCardViewModel> pinnedMemories,
        IReadOnlyCollection<TodoItemViewModel> activeTodos,
        IReadOnlyCollection<TicketSummaryViewModel> activeTickets,
        IReadOnlyCollection<CodeGraphProjectCardViewModel> codeGraphProjects,
        IReadOnlyCollection<RecentChangeItemViewModel> recentChanges)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Focus workspace export");
        builder.AppendLine();

        builder.AppendLine("Scoped agents:");
        foreach (var agent in recommendedAgents)
        {
            builder.Append("- ")
                .Append(agent.Name)
                .Append(" | ")
                .Append(string.IsNullOrWhiteSpace(agent.RecommendationReason) ? agent.Summary : agent.RecommendationReason)
                .AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("Recommended skills:");
        foreach (var skill in recommendedSkills)
        {
            builder.Append("- ")
                .Append(skill.Name)
                .Append(" | ")
                .Append(string.IsNullOrWhiteSpace(skill.RecommendationReason) ? skill.Summary : skill.RecommendationReason)
                .AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("Pinned memories:");
        foreach (var memory in pinnedMemories)
        {
            builder.Append("- ")
                .Append(memory.Title)
                .Append(string.IsNullOrWhiteSpace(memory.FreshnessLabel) ? string.Empty : $" [{memory.FreshnessLabel}]")
                .Append(" | ")
                .Append(memory.WingName)
                .Append(" / ")
                .Append(memory.RoomName)
                .Append(" | ")
                .Append(memory.Summary)
                .AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("Active todos:");
        foreach (var todo in activeTodos)
        {
            builder.Append("- ")
                .Append(todo.Title)
                .Append(" | ")
                .Append(todo.StatusLabel)
                .Append(" | ")
                .Append(todo.PreviewDetails)
                .AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("Active tickets:");
        foreach (var ticket in activeTickets)
        {
            builder.Append("- ")
                .Append(ticket.TicketNumber)
                .Append(" ")
                .Append(ticket.Title)
                .Append(" | ")
                .Append(ticket.StatusLabel)
                .Append(" | ")
                .Append(ticket.PreviewDescription)
                .AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("Code graph projects:");
        foreach (var project in codeGraphProjects)
        {
            builder.Append("- ")
                .Append(project.Name)
                .Append(" | files ")
                .Append(project.FileCount)
                .Append(" | symbols ")
                .Append(project.SymbolCount)
                .Append(" | ")
                .Append(project.Summary)
                .AppendLine();
        }

        builder.AppendLine();
        builder.AppendLine("Recent changes:");
        foreach (var change in recentChanges)
        {
            builder.Append("- ")
                .Append(change.Kind)
                .Append(" | ")
                .Append(change.Title)
                .Append(" | ")
                .Append(change.Detail)
                .AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static AgentRunViewModel BuildAgentRunViewModel(
        AgentDetailViewModel detail,
        AgentRunInput input,
        ContextPackViewModel? contextPack,
        IReadOnlyCollection<SkillCardViewModel> companionSkills,
        IReadOnlyCollection<AgentCardViewModel> followOnAgents)
    {
        AgentExecutionStepViewModel[] steps = detail.Agent.Slug switch
        {
            "context-agent" =>
            [
                new AgentExecutionStepViewModel
                {
                    Title = "Clarify the task",
                    Detail = "Use the task framing and top context matches to tighten the question before acting.",
                    Outcome = "A focused task statement and grounded context pack."
                },
                new AgentExecutionStepViewModel
                {
                    Title = "Read the strongest evidence",
                    Detail = "Review the top memories, tickets, todos, and recent changes surfaced for this task.",
                    Outcome = "The best prior decisions and relevant work are in view."
                },
                new AgentExecutionStepViewModel
                {
                    Title = "Route the work",
                    Detail = "Choose the next agent or skill based on the dominant goal and any context gaps.",
                    Outcome = "A clear downstream plan instead of a cold start."
                }
            ],
            "triage-agent" =>
            [
                new AgentExecutionStepViewModel
                {
                    Title = "Normalize the intake",
                    Detail = "Turn the raw request into a clear problem statement, explicit scope, and named urgency before creating downstream work.",
                    Outcome = "The task is framed clearly enough to route and prioritize."
                },
                new AgentExecutionStepViewModel
                {
                    Title = "Check overlap and route it",
                    Detail = "Use the context pack to look for related memories, tickets, todos, and recent changes that suggest duplicates or the right Focus destination.",
                    Outcome = "Duplicate risk is visible and the work is routed to the correct wing, room, or agent path."
                },
                new AgentExecutionStepViewModel
                {
                    Title = "Propose the next records",
                    Detail = "Name the tickets, todos, and next agent handoff that would make this work executable without losing the original intake.",
                    Outcome = "A bounded intake result that can move directly into context, planning, or execution."
                }
            ],
            "research-agent" =>
            [
                new AgentExecutionStepViewModel
                {
                    Title = "Collect evidence",
                    Detail = "Use the related context pack to gather memories, tickets, code graph clues, and recent changes tied to the question.",
                    Outcome = "A source-backed evidence set for the investigation."
                },
                new AgentExecutionStepViewModel
                {
                    Title = "Synthesize the findings",
                    Detail = "Summarize the most likely explanation, what is confirmed, and what is still missing.",
                    Outcome = "A concise investigative brief with named evidence."
                },
                new AgentExecutionStepViewModel
                {
                    Title = "Name the next checks",
                    Detail = "Turn gaps and ambiguities into concrete next checks or follow-on tasks.",
                    Outcome = "A short list of decisive next actions."
                }
            ],
            "impact-agent" =>
            [
                new AgentExecutionStepViewModel
                {
                    Title = "Map the affected surface",
                    Detail = "Use the context pack, code graph clues, and recent changes to identify which files, rooms, systems, or workflows the task could touch.",
                    Outcome = "The likely blast radius is visible before anything changes."
                },
                new AgentExecutionStepViewModel
                {
                    Title = "Rank the risks and checks",
                    Detail = "Turn the affected surface into a practical validation list covering breakpoints, dependencies, and likely regressions.",
                    Outcome = "A risk-ranked checklist for what must be validated next."
                },
                new AgentExecutionStepViewModel
                {
                    Title = "Stage the downstream handoff",
                    Detail = "Recommend whether the task should move to execution, review, or deeper research based on the impact confidence and remaining unknowns.",
                    Outcome = "The next phase is chosen from evidence instead of guesswork."
                }
            ],
            "execution-agent" =>
            [
                new AgentExecutionStepViewModel
                {
                    Title = "Confirm the bounds",
                    Detail = "Use the context pack and prompt to make sure the task is explicit, scoped, and ready for follow-through.",
                    Outcome = "A bounded plan that is safe to execute."
                },
                new AgentExecutionStepViewModel
                {
                    Title = "Run the concrete steps",
                    Detail = "Execute the approved checklist, build, test, maintenance task, or delivery flow in order.",
                    Outcome = "A completed step log and visible task progress."
                },
                new AgentExecutionStepViewModel
                {
                    Title = "Capture the result",
                    Detail = "Summarize outcomes, blockers, and any durable knowledge that should be written back into Focus.",
                    Outcome = "The task result is ready for handoff or memory capture."
                }
            ],
            "curation-agent" =>
            [
                new AgentExecutionStepViewModel
                {
                    Title = "Extract the durable outcomes",
                    Detail = "Separate stable decisions, shipped behavior, and reusable lessons from transient task chatter or one-off execution details.",
                    Outcome = "Only durable knowledge is eligible for Focus write-back."
                },
                new AgentExecutionStepViewModel
                {
                    Title = "Propose freshness and merge updates",
                    Detail = "Compare the new outcome against existing memories, bootstrap context, and recent changes to find merge, supersede, or refresh opportunities.",
                    Outcome = "Memory cleanup and freshness work is grounded in evidence."
                },
                new AgentExecutionStepViewModel
                {
                    Title = "Queue the write-back",
                    Detail = "Name the memory captures, merges, and follow-up governance actions that should happen to keep Focus trustworthy after the task.",
                    Outcome = "The durable outcome is ready to become system-of-record knowledge."
                }
            ],
            _ =>
            [
                new AgentExecutionStepViewModel
                {
                    Title = "Read the proposed work",
                    Detail = "Review the task framing, related context, and the strongest evidence before assessing quality.",
                    Outcome = "The review starts from exact context, not guesses."
                },
                new AgentExecutionStepViewModel
                {
                    Title = "Look for material risk",
                    Detail = "Check for regressions, missing wiring, weak assumptions, and work that escaped its intended scope.",
                    Outcome = "A high-signal risk list focused on what matters."
                },
                new AgentExecutionStepViewModel
                {
                    Title = "Recommend follow-up",
                    Detail = "Convert the risks into concrete follow-up checks or fixes before the work is treated as done.",
                    Outcome = "A short validation and remediation checklist."
                }
            ]
        };

        var nextActions = new List<string>();
        if (contextPack?.TopMatches.Count > 0)
        {
            nextActions.Add($"Read the top {Math.Min(3, contextPack.TopMatches.Count)} context matches first.");
        }

        if (contextPack?.ClarifyingQuestions.Count > 0)
        {
            nextActions.Add($"Answer: {contextPack.ClarifyingQuestions.First()}");
        }

        if (companionSkills.Count > 0)
        {
            nextActions.Add($"Use the companion skill {companionSkills.First().Name} for the concrete workflow.");
        }

        if (followOnAgents.Count > 0)
        {
            nextActions.Add($"Hand off next to {followOnAgents.First().Name} if the task changes phase.");
        }

        if (nextActions.Count == 0)
        {
            nextActions.Add("Read the context pack, follow the agent steps, and record the durable outcome.");
        }

        var summary = $"{detail.Agent.Name} prepared a {detail.Agent.DefaultGoalLabel.ToLowerInvariant()} task brief for \"{input.Question.Trim()}\".";
        if (contextPack is not null)
        {
            summary += $" It surfaced {contextPack.TopMatches.Count} top matches and {companionSkills.Count} companion skills.";
        }

        return new AgentRunViewModel
        {
            TaskQuestion = input.Question.Trim(),
            Summary = summary,
            OperatorPrompt = $"{detail.SuggestedPrompt} Task: {input.Question.Trim()}",
            ExecutionModeLabel = detail.Agent.ScopeLabel,
            SupportsWriteActions = detail.Agent.SupportsWriteActions,
            Steps = steps,
            NextActions = nextActions,
            CompanionSkills = companionSkills,
            FollowOnAgents = followOnAgents,
            ContextPack = contextPack
        };
    }

    private static string BuildWorkspaceBootstrapSummary(
        WorkspaceExportViewModel workspace,
        IReadOnlyCollection<WingSummaryViewModel> wings,
        string profile)
    {
        var builder = new StringBuilder();
        builder.Append(profile switch
        {
            "operator" => "Use Focus as the operational truth layer. Check recent changes, governance, and active work first. ",
            "incident-response" => "Use Focus as the incident bootstrap. Bias toward recent changes, incident memory, and canonical subsystem context. ",
            _ => "Start with Focus. Search memories, build a context pack, then check recent changes and active work. "
        });
        builder.Append("Workspace snapshot: ");
        builder.Append(workspace.RecommendedAgents.Count).Append(" recommended agents, ");
        builder.Append(workspace.RecommendedSkills.Count).Append(" recommended skills, ");
        builder.Append(workspace.PinnedMemories.Count).Append(" pinned memories, ");
        builder.Append(workspace.ActiveTodos.Count).Append(" active todos, ");
        builder.Append(workspace.ActiveTickets.Count).Append(" active tickets, ");
        builder.Append(workspace.CodeGraphProjects.Count).Append(" code graph projects");

        if (wings.Count > 0)
        {
            builder.Append(". Top wings: ");
            builder.Append(string.Join(", ", wings.Take(4).Select(x => x.Name)));
        }

        return builder.ToString();
    }

    private static string NormalizeBootstrapProfile(string? profile)
    {
        var normalized = (profile ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "operator" => "operator",
            "incident-response" or "incident" => "incident-response",
            _ => "developer"
        };
    }

    private static string MergeSummary(string existingSummary, string incomingSummary)
    {
        var merged = MergeMetadataField(existingSummary, incomingSummary, 500);
        return string.IsNullOrWhiteSpace(merged) ? existingSummary : merged;
    }

    private static string MergeContent(string existingContent, string sourceTitle, Guid sourceId, string incomingContent)
    {
        if (string.IsNullOrWhiteSpace(incomingContent) || existingContent.Contains(incomingContent, StringComparison.Ordinal))
        {
            return existingContent;
        }

        var builder = new StringBuilder(existingContent.Trim());
        if (builder.Length > 0)
        {
            builder.AppendLine().AppendLine();
        }

        builder.AppendLine($"Merged from {sourceTitle} ({sourceId}):");
        builder.Append(incomingContent.Trim());
        return builder.ToString();
    }

    private static string MergeMetadataField(string existingValue, string incomingValue, int maxLength)
    {
        var parts = new[] { existingValue?.Trim(), incomingValue?.Trim() }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (parts.Length == 0)
        {
            return string.Empty;
        }

        var combined = string.Join("; ", parts);
        return combined.Length <= maxLength ? combined : combined[..maxLength];
    }

    private static DateTime? MinDate(DateTime? left, DateTime? right)
    {
        if (!left.HasValue)
        {
            return right;
        }

        if (!right.HasValue)
        {
            return left;
        }

        return left.Value <= right.Value ? left : right;
    }

    private static SkillCardViewModel MapSkillCard(ResolvedSkillRecord skill)
        => MapSkillCard(skill.Skill, isReadOnly: skill.IsReadOnly, sourceLabel: skill.SourceLabel, sourcePath: skill.SourcePath);

    private static SkillCardViewModel MapSkillCard(
        SkillEntry skill,
        string recommendationReason = "",
        decimal recommendationScore = 0m,
        bool isReadOnly = false,
        string sourceLabel = "",
        string sourcePath = "")
    {
        var now = DateTime.UtcNow;
        return new SkillCardViewModel
        {
            Id = skill.Id,
            WingId = skill.WingId,
            Name = skill.Name,
            Slug = skill.Slug,
            Summary = skill.Summary,
            Category = skill.Category,
            CategoryLabel = skill.Category.ToString(),
            WingName = skill.Wing?.Name ?? string.Empty,
            TriggerHintsText = skill.TriggerHintsText,
            TriggerHints = SplitSkillText(skill.TriggerHintsText, ',', ';'),
            IsPinned = skill.IsPinned,
            UseCount = skill.UseCount,
            LastUsedUtc = skill.LastUsedUtc,
            LastReviewedUtc = skill.LastReviewedUtc,
            ReviewAfterUtc = skill.ReviewAfterUtc,
            NeedsReview = SkillRecommendationEngine.NeedsReview(skill, now),
            ReviewLabel = SkillRecommendationEngine.GetReviewLabel(skill, now),
            RecommendationReason = recommendationReason,
            RecommendationScore = recommendationScore,
            RecommendationScoreLabel = recommendationScore <= 0m ? string.Empty : recommendationScore.ToString("0.#"),
            UpdatedUtc = skill.UpdatedUtc,
            IsReadOnly = isReadOnly,
            SourceLabel = sourceLabel,
            SourcePath = sourcePath
        };
    }

    private static SkillDetailViewModel MapSkillDetail(
        SkillEntry skill,
        ContextPackViewModel? relatedContext,
        bool isReadOnly = false,
        string sourceLabel = "",
        string sourcePath = "")
    {
        return new SkillDetailViewModel
        {
            Skill = MapSkillCard(skill, isReadOnly: isReadOnly, sourceLabel: sourceLabel, sourcePath: sourcePath),
            WhenToUse = skill.WhenToUse,
            Flow = skill.Flow,
            ExamplesText = skill.ExamplesText,
            WhenToUsePoints = SplitSkillText(skill.WhenToUse),
            FlowSteps = SplitSkillText(skill.Flow),
            ExamplePrompts = SplitSkillText(skill.ExamplesText),
            SuggestedQuestion = SkillRecommendationEngine.BuildSuggestedQuestion(skill),
            RelatedContext = relatedContext
        };
    }

    private async Task TouchSkillUsageAsync(SkillEntry skill, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        skill.UseCount += 1;
        skill.LastUsedUtc = now;
        skill.ReviewAfterUtc ??= now.AddDays(SkillRecommendationEngine.DefaultReviewWindowDays);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static IReadOnlyCollection<string> SplitSkillText(string? value, params char[] separators)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        if (separators is { Length: > 0 })
        {
            return value.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        return value.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private sealed record ResolvedSkillRecord(
        SkillEntry Skill,
        bool IsReadOnly = false,
        string SourceLabel = "",
        string SourcePath = "");

    private static MemoryCardViewModel MapCard(MemoryEntry memory)
    {
        var trust = MemoryTrustHelper.Build(memory);
        return new MemoryCardViewModel
        {
            Id = memory.Id,
            SupersededByMemoryId = memory.SupersededByMemoryId,
            Title = memory.Title,
            Summary = memory.Summary,
            WingSlug = memory.Wing?.Slug ?? string.Empty,
            WingName = memory.Wing?.Name ?? "Unsorted",
            RoomName = memory.Room?.Name ?? "General",
            Kind = memory.Kind,
            SourceKind = memory.SourceKind,
            Importance = memory.Importance,
            IsPinned = memory.IsPinned,
            LifecycleState = memory.LifecycleState,
            LifecycleLabel = MemoryTrustHelper.GetLifecycleLabel(memory.LifecycleState),
            LifecycleReason = memory.LifecycleReason,
            SupersededByTitle = memory.SupersededByMemory?.Title ?? string.Empty,
            VerificationStatus = memory.VerificationStatus,
            VerificationStatusLabel = trust.VerificationStatusLabel,
            LastVerifiedUtc = memory.LastVerifiedUtc,
            ReviewAfterUtc = memory.ReviewAfterUtc,
            LastReferencedUtc = memory.LastReferencedUtc,
            LifecycleChangedUtc = memory.LifecycleChangedUtc,
            ReferenceCount = memory.ReferenceCount,
            IsReviewDue = trust.IsReviewDue,
            IsRetired = memory.LifecycleState != MemoryLifecycleState.Active,
            FreshnessLabel = trust.FreshnessLabel,
            TrustScore = trust.TrustScore,
            TrustLabel = trust.TrustLabel,
            UpdatedUtc = memory.UpdatedUtc,
            Tags = memory.MemoryTags.OrderBy(x => x.Tag!.Name).Select(x => x.Tag!.Name).ToArray()
        };
    }

    private static PalaceVisualizerSceneViewModel BuildPalaceScene(
        IReadOnlyCollection<Wing> wings,
        IReadOnlyCollection<Room> rooms,
        IReadOnlyCollection<MemoryEntry> memories,
        IReadOnlyCollection<MemoryLink> memoryLinks,
        IReadOnlyCollection<TodoEntry> activeTodos)
    {
        if (wings.Count == 0 && memories.Count == 0 && activeTodos.Count == 0)
        {
            return new PalaceVisualizerSceneViewModel();
        }

        const string palaceNodeId = "palace-root";
        const string unsortedNodeId = "wing:unsorted";
        const string todoWingNodeId = "wing:workboard";
        const string palaceColor = "#7EA8FF";
        const string unsortedColor = "#D59CFF";
        const string todoWingColor = "#14B8A6";
        const string structureEdgeColor = "#8AB4F8";
        const string memoryLinkEdgeColor = "#FFB76A";
        string[] wingPalette = ["#2563EB", "#DC2626", "#EAB308", "#16A34A", "#F97316", "#7C3AED", "#0891B2", "#DB2777"];

        var nodes = new List<PalaceVisualizerSceneNodeViewModel>();
        var edges = new List<PalaceVisualizerSceneEdgeViewModel>();

        var orderedWings = wings.OrderBy(x => x.Name).ToArray();
        var orderedRooms = rooms.OrderBy(x => x.Name).ToArray();
        var roomLookup = orderedRooms.ToDictionary(x => x.Id);
        var wingLookup = orderedWings.ToDictionary(x => x.Id);

        var activeMemories = memories
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.Importance)
            .ThenBy(x => x.Title)
            .ToArray();
        var unsortedMemories = activeMemories
            .Where(memory => memory.WingId is null)
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.Importance)
            .ThenBy(x => x.Title)
            .ToArray();
        var totalWingLikeCount = orderedWings.Length + (unsortedMemories.Length > 0 ? 1 : 0) + (activeTodos.Count > 0 ? 1 : 0);

        nodes.Add(new PalaceVisualizerSceneNodeViewModel
        {
            Id = palaceNodeId,
            Label = "Focus Palace",
            NodeTypeLabel = "Palace",
            SecondaryLabel = $"{orderedWings.Length} wings • {orderedRooms.Length} rooms • {activeMemories.Length} active memories • {activeTodos.Count} active todos",
            ColorHex = palaceColor,
            UrlPath = "/Palace/Explore",
            Radius = 22d,
            IsEmphasized = false
        });

        var wingAnchors = new Dictionary<Guid, (double X, double Y, double Z)>();
        var wingColors = new Dictionary<Guid, string>();
        var roomColors = new Dictionary<Guid, string>();
        for (var index = 0; index < orderedWings.Length; index++)
        {
            var wing = orderedWings[index];
            var wingColor = wingPalette[index % wingPalette.Length];
            var angle = (index / Math.Max(1d, totalWingLikeCount)) * Math.PI * 2d;
            var radius = 480d;
            var galaxyPhase = StableUnitValue(wing.Id, 11) * Math.PI * 2d;
            var quadrant = index % 4;
            var verticalLaneBias = quadrant switch
            {
                0 => 228d,
                1 => -218d,
                2 => 172d,
                _ => -182d
            };
            var depthLaneBias = quadrant switch
            {
                0 => 298d,
                1 => 286d,
                2 => -286d,
                _ => -302d
            };
            var xTilt = quadrant switch
            {
                0 => 1d,
                1 => 0.82d,
                2 => -0.82d,
                _ => -1d
            };
            var anchor = (
                X: (Math.Cos(angle) * radius) + (Math.Sin(galaxyPhase) * 88d * xTilt),
                Y: (Math.Sin(angle) * radius * 0.78d) + verticalLaneBias + (Math.Cos(galaxyPhase) * 42d),
                Z: (Math.Cos((angle * 1.28d) + galaxyPhase) * radius * 0.42d) + depthLaneBias + (Math.Sin(angle + galaxyPhase) * 52d));

            wingAnchors[wing.Id] = anchor;
            wingColors[wing.Id] = wingColor;

            var wingRoomCount = orderedRooms.Count(room => room.WingId == wing.Id);
            var wingMemoryCount = activeMemories.Count(memory => memory.WingId == wing.Id);
            var wingRadius = Math.Min(14d + (wingRoomCount * 1.55d), 25d);
            nodes.Add(new PalaceVisualizerSceneNodeViewModel
            {
                Id = $"wing:{wing.Id}",
                Label = wing.Name,
                NodeTypeLabel = "Wing",
                SecondaryLabel = $"{wingRoomCount} rooms • {wingMemoryCount} memories",
                ColorHex = wingColor,
                UrlPath = $"/Palace/Wing/{Uri.EscapeDataString(wing.Slug)}",
                X = anchor.X,
                Y = anchor.Y,
                Z = anchor.Z,
                Radius = wingRadius,
                IsEmphasized = false
            });
            edges.Add(new PalaceVisualizerSceneEdgeViewModel
            {
                FromNodeId = palaceNodeId,
                ToNodeId = $"wing:{wing.Id}",
                ColorHex = structureEdgeColor
            });
        }

        foreach (var wing in orderedWings)
        {
            var wingAnchor = wingAnchors[wing.Id];
            var wingRooms = orderedRooms.Where(room => room.WingId == wing.Id).OrderBy(room => room.Name).ToArray();
            for (var index = 0; index < wingRooms.Length; index++)
            {
                var room = wingRooms[index];
                var roomColor = BlendHexColor(wingColors[wing.Id], "#0E1828", 0.18d + (StableUnitValue(room.Id, 19) * 0.12d));
                roomColors[room.Id] = roomColor;
                var angle = ((index / Math.Max(1d, wingRooms.Length)) * Math.PI * 2d) + (StableUnitValue(room.Id, 5) * 0.34d);
                var radius = 220d + (index * 34d) + (StableUnitValue(room.Id, 9) * 42d);
                var orbitPhase = StableUnitValue(room.Id, 21) * Math.PI * 2d;
                var orbitSpeed = Math.PI / 60d;
                var x = wingAnchor.X + Math.Cos(angle) * radius;
                var y = wingAnchor.Y + Math.Sin(angle) * radius * 0.94d;
                var z = wingAnchor.Z + Math.Cos((angle * 1.14d) + orbitPhase) * radius * 0.34d;

                nodes.Add(new PalaceVisualizerSceneNodeViewModel
                {
                    Id = $"room:{room.Id}",
                    Label = room.Name,
                    NodeTypeLabel = "Room",
                    SecondaryLabel = $"{wing.Name} • {activeMemories.Count(memory => memory.RoomId == room.Id)} memories",
                    ColorHex = roomColor,
                    UrlPath = $"/Palace/Explore?roomId={room.Id}",
                    X = x,
                    Y = y,
                    Z = z,
                    Radius = 11d,
                    OrbitCenterNodeId = $"wing:{wing.Id}",
                    OrbitRadius = radius,
                    OrbitVerticalRadius = radius * 0.94d,
                    OrbitDepthRadius = radius * 0.34d,
                    OrbitPhase = angle,
                    OrbitSpeed = orbitSpeed,
                    IsEmphasized = false
                });
                edges.Add(new PalaceVisualizerSceneEdgeViewModel
                {
                    FromNodeId = $"wing:{wing.Id}",
                    ToNodeId = $"room:{room.Id}",
                    ColorHex = structureEdgeColor
                });
            }
        }

        var generalMemoriesByWing = activeMemories
            .Where(memory => memory.WingId.HasValue && memory.RoomId is null)
            .GroupBy(memory => memory.WingId!.Value)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(x => x.IsPinned).ThenByDescending(x => x.Importance).ThenBy(x => x.Title).ToArray());

        foreach (var wing in orderedWings)
        {
            if (!generalMemoriesByWing.TryGetValue(wing.Id, out var generalMemories))
            {
                continue;
            }

            var wingAnchor = wingAnchors[wing.Id];
            var wingMemoryColor = BlendHexColor(wingColors[wing.Id], "#FFFFFF", 0.16d);
            for (var index = 0; index < generalMemories.Length; index++)
            {
                var memory = generalMemories[index];
                AddMemorySceneNode(nodes, edges, $"wing:{wing.Id}", memory, $"General • {wing.Name}", wingAnchor, index, generalMemories.Length, wingMemoryColor, structureEdgeColor, $"/Palace/Memory?id={memory.Id}", 86d, 14d);
            }
        }

        foreach (var room in orderedRooms)
        {
            var roomMemories = activeMemories
                .Where(memory => memory.RoomId == room.Id)
                .OrderByDescending(x => x.IsPinned)
                .ThenByDescending(x => x.Importance)
                .ThenBy(x => x.Title)
                .ToArray();

            if (roomMemories.Length == 0)
            {
                continue;
            }

            var roomNode = nodes.First(node => node.Id == $"room:{room.Id}");
            var roomAnchor = (roomNode.X, roomNode.Y, roomNode.Z);
            var roomMemoryColor = BlendHexColor(roomColors[room.Id], "#FFFFFF", 0.16d);
            foreach (var memoryWithIndex in roomMemories.Select((memory, index) => new { memory, index }))
            {
                AddMemorySceneNode(nodes, edges, $"room:{room.Id}", memoryWithIndex.memory, $"{room.Name} • {wingLookup[room.WingId].Name}", roomAnchor, memoryWithIndex.index, roomMemories.Length, roomMemoryColor, structureEdgeColor, $"/Palace/Memory?id={memoryWithIndex.memory.Id}", 64d, 12d);
            }
        }

        if (unsortedMemories.Length > 0)
        {
            var unsortedIndex = orderedWings.Length;
            var unsortedAngle = (unsortedIndex / Math.Max(1d, totalWingLikeCount)) * Math.PI * 2d;
            var unsortedRadius = 480d;
            const double unsortedGalaxyPhase = Math.PI * 1.37d;
            var unsortedQuadrant = unsortedIndex % 4;
            var unsortedVerticalLaneBias = unsortedQuadrant switch
            {
                0 => 228d,
                1 => -218d,
                2 => 172d,
                _ => -182d
            };
            var unsortedDepthLaneBias = unsortedQuadrant switch
            {
                0 => 298d,
                1 => 286d,
                2 => -286d,
                _ => -302d
            };
            var unsortedXTilt = unsortedQuadrant switch
            {
                0 => 1d,
                1 => 0.82d,
                2 => -0.82d,
                _ => -1d
            };
            var unsortedAnchor = (
                X: (Math.Cos(unsortedAngle) * unsortedRadius) + (Math.Sin(unsortedGalaxyPhase) * 88d * unsortedXTilt),
                Y: (Math.Sin(unsortedAngle) * unsortedRadius * 0.78d) + unsortedVerticalLaneBias + (Math.Cos(unsortedGalaxyPhase) * 42d),
                Z: (Math.Cos((unsortedAngle * 1.28d) + unsortedGalaxyPhase) * unsortedRadius * 0.42d) + unsortedDepthLaneBias + (Math.Sin(unsortedAngle + unsortedGalaxyPhase) * 52d));
            nodes.Add(new PalaceVisualizerSceneNodeViewModel
            {
                Id = unsortedNodeId,
                Label = "Unsorted wing",
                NodeTypeLabel = "Wing",
                SecondaryLabel = $"{unsortedMemories.Length} unfiled memories",
                ColorHex = unsortedColor,
                UrlPath = "/Palace/Explore",
                X = unsortedAnchor.X,
                Y = unsortedAnchor.Y,
                Z = unsortedAnchor.Z,
                Radius = Math.Min(14d + (unsortedMemories.Length * 0.35d), 22d),
                IsEmphasized = false
            });
            edges.Add(new PalaceVisualizerSceneEdgeViewModel
            {
                FromNodeId = palaceNodeId,
                ToNodeId = unsortedNodeId,
                ColorHex = structureEdgeColor
            });

            var unsortedMemoryColor = BlendHexColor(unsortedColor, "#FFFFFF", 0.14d);
            foreach (var memoryWithIndex in unsortedMemories.Select((memory, index) => new { memory, index }))
            {
                AddMemorySceneNode(nodes, edges, unsortedNodeId, memoryWithIndex.memory, "Unsorted wing", unsortedAnchor, memoryWithIndex.index, unsortedMemories.Length, unsortedMemoryColor, structureEdgeColor, $"/Palace/Memory?id={memoryWithIndex.memory.Id}", 92d, 14d);
            }
        }

        if (activeTodos.Count > 0)
        {
            var todoIndex = orderedWings.Length + (unsortedMemories.Length > 0 ? 1 : 0);
            var todoAngle = (todoIndex / Math.Max(1d, totalWingLikeCount)) * Math.PI * 2d;
            var todoRadius = 480d;
            const double todoGalaxyPhase = Math.PI * 0.63d;
            var todoQuadrant = todoIndex % 4;
            var todoVerticalLaneBias = todoQuadrant switch
            {
                0 => 228d,
                1 => -218d,
                2 => 172d,
                _ => -182d
            };
            var todoDepthLaneBias = todoQuadrant switch
            {
                0 => 298d,
                1 => 286d,
                2 => -286d,
                _ => -302d
            };
            var todoXTilt = todoQuadrant switch
            {
                0 => 1d,
                1 => 0.82d,
                2 => -0.82d,
                _ => -1d
            };
            var todoAnchor = (
                X: (Math.Cos(todoAngle) * todoRadius) + (Math.Sin(todoGalaxyPhase) * 88d * todoXTilt),
                Y: (Math.Sin(todoAngle) * todoRadius * 0.78d) + todoVerticalLaneBias + (Math.Cos(todoGalaxyPhase) * 42d),
                Z: (Math.Cos((todoAngle * 1.28d) + todoGalaxyPhase) * todoRadius * 0.42d) + todoDepthLaneBias + (Math.Sin(todoAngle + todoGalaxyPhase) * 52d));
            var todoRoomCount = activeTodos.Select(x => x.Status).Distinct().Count();
            nodes.Add(new PalaceVisualizerSceneNodeViewModel
            {
                Id = todoWingNodeId,
                Label = "Workboard",
                NodeTypeLabel = "Wing",
                SecondaryLabel = $"{todoRoomCount} todo lanes • {activeTodos.Count} active todos",
                ColorHex = todoWingColor,
                UrlPath = "/Todos",
                X = todoAnchor.X,
                Y = todoAnchor.Y,
                Z = todoAnchor.Z,
                Radius = Math.Min(15d + (todoRoomCount * 2d), 24d),
                IsEmphasized = false
            });
            edges.Add(new PalaceVisualizerSceneEdgeViewModel
            {
                FromNodeId = palaceNodeId,
                ToNodeId = todoWingNodeId,
                ColorHex = structureEdgeColor
            });

            var todoStatusGroups = activeTodos
                .GroupBy(todo => todo.Status)
                .OrderBy(group => group.Key == TodoStatus.InProgress ? 0 : group.Key == TodoStatus.Pending ? 1 : 2)
                .ToArray();

            for (var index = 0; index < todoStatusGroups.Length; index++)
            {
                var group = todoStatusGroups[index];
                var roomId = $"todo-room:{group.Key}";
                var roomAngle = ((index / Math.Max(1d, todoStatusGroups.Length)) * Math.PI * 2d) + (index * 0.18d);
                var roomRadius = 210d + (index * 28d);
                var roomColor = BlendHexColor(todoWingColor, "#0E1828", 0.16d + (index * 0.06d));
                var roomPhase = index * 0.7d;
                var roomAnchor = (
                    X: todoAnchor.X + Math.Cos(roomAngle) * roomRadius,
                    Y: todoAnchor.Y + Math.Sin(roomAngle) * roomRadius * 0.92d,
                    Z: todoAnchor.Z + Math.Cos((roomAngle * 1.14d) + roomPhase) * roomRadius * 0.32d);
                nodes.Add(new PalaceVisualizerSceneNodeViewModel
                {
                    Id = roomId,
                    Label = group.Key switch
                    {
                        TodoStatus.InProgress => "In progress",
                        TodoStatus.Pending => "Pending",
                        TodoStatus.Blocked => "Blocked",
                        _ => group.Key.ToString()
                    },
                    NodeTypeLabel = "Room",
                    SecondaryLabel = $"Workboard • {group.Count()} todos",
                    ColorHex = roomColor,
                    UrlPath = "/Todos",
                    X = roomAnchor.X,
                    Y = roomAnchor.Y,
                    Z = roomAnchor.Z,
                    Radius = 11d,
                    OrbitCenterNodeId = todoWingNodeId,
                    OrbitRadius = roomRadius,
                    OrbitVerticalRadius = roomRadius * 0.92d,
                    OrbitDepthRadius = roomRadius * 0.32d,
                    OrbitPhase = roomAngle,
                    OrbitSpeed = 0.08d + (index * 0.01d),
                    IsEmphasized = false
                });
                edges.Add(new PalaceVisualizerSceneEdgeViewModel
                {
                    FromNodeId = todoWingNodeId,
                    ToNodeId = roomId,
                    ColorHex = structureEdgeColor
                });

                var orderedTodos = group
                    .OrderBy(todo => todo.Status == TodoStatus.InProgress ? 0 : todo.Status == TodoStatus.Pending ? 1 : 2)
                    .ThenByDescending(todo => todo.UpdatedUtc)
                    .ThenBy(todo => todo.Title)
                    .ToArray();
                foreach (var todoWithIndex in orderedTodos.Select((todo, itemIndex) => new { todo, itemIndex }))
                {
                    AddTodoSceneNode(
                        nodes,
                        edges,
                        roomId,
                        todoWithIndex.todo,
                        roomAnchor,
                        todoWithIndex.itemIndex,
                        orderedTodos.Length,
                        roomColor,
                        structureEdgeColor);
                }
            }
        }

        var memoryNodeIds = nodes
            .Where(node => node.Id.StartsWith("memory:", StringComparison.Ordinal))
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var link in memoryLinks.Where(link => link.FromMemoryEntryId != link.ToMemoryEntryId))
        {
            var fromNodeId = $"memory:{link.FromMemoryEntryId}";
            var toNodeId = $"memory:{link.ToMemoryEntryId}";
            if (!memoryNodeIds.Contains(fromNodeId) || !memoryNodeIds.Contains(toNodeId))
            {
                continue;
            }

            edges.Add(new PalaceVisualizerSceneEdgeViewModel
            {
                FromNodeId = fromNodeId,
                ToNodeId = toNodeId,
                ColorHex = memoryLinkEdgeColor
            });
        }

        var legend = new List<PalaceVisualizerSceneLegendItemViewModel>
        {
            new() { Label = "Palace", ColorHex = palaceColor }
        };

        legend.AddRange(orderedWings.Select(wing => new PalaceVisualizerSceneLegendItemViewModel
        {
            Label = wing.Name,
            ColorHex = wingColors[wing.Id]
        }));

        if (unsortedMemories.Length > 0)
        {
            legend.Add(new PalaceVisualizerSceneLegendItemViewModel
            {
                Label = "Unsorted wing",
                ColorHex = unsortedColor
            });
        }

        if (activeTodos.Count > 0)
        {
            legend.Add(new PalaceVisualizerSceneLegendItemViewModel
            {
                Label = "Workboard",
                ColorHex = todoWingColor
            });
        }

        return new PalaceVisualizerSceneViewModel
        {
            Heading = "3D palace graph",
            Summary = "A rotating structural map of wings, rooms, memories, and cross-memory links so the palace is easier to inspect as a connected system.",
            Legend = legend,
            Nodes = nodes,
            Edges = edges
        };
    }

    private static void AddMemorySceneNode(
        ICollection<PalaceVisualizerSceneNodeViewModel> nodes,
        ICollection<PalaceVisualizerSceneEdgeViewModel> edges,
        string parentNodeId,
        MemoryEntry memory,
        string secondaryLabel,
        (double X, double Y, double Z) anchor,
        int index,
        int totalCount,
        string nodeColor,
        string edgeColor,
        string urlPath,
        double baseOrbitRadius,
        double orbitStep)
    {
        var shellCapacity = Math.Clamp((int)Math.Ceiling(Math.Sqrt(Math.Max(1, totalCount)) * 2.4d), 6, 18);
        var shellIndex = index / shellCapacity;
        var shellSlot = index % shellCapacity;
        var shellItemCount = Math.Min(shellCapacity, totalCount - (shellIndex * shellCapacity));
        var angle = ((shellSlot / Math.Max(1d, shellItemCount)) * Math.PI * 2d)
            + (shellIndex * 0.42d)
            + (StableUnitValue(memory.Id, 17) * 0.45d);
        var radius = baseOrbitRadius
            + (shellIndex * orbitStep * 1.45d)
            + (StableUnitValue(memory.Id, 23) * Math.Min(orbitStep * 0.75d, 9d));
        var orbitPhase = StableUnitValue(memory.Id, 31) * Math.PI * 2d;
        var orbitSpeed = Math.PI / 120d;
        var x = anchor.X + Math.Cos(angle) * radius;
        var y = anchor.Y + Math.Sin(angle) * radius * 0.9d;
        var z = anchor.Z + Math.Cos((angle * 1.18d) + orbitPhase) * radius * 0.38d;
        var pinnedSuffix = memory.IsPinned ? " • pinned" : string.Empty;

        nodes.Add(new PalaceVisualizerSceneNodeViewModel
        {
            Id = $"memory:{memory.Id}",
            Label = memory.Title,
            NodeTypeLabel = "Memory",
            SecondaryLabel = $"{secondaryLabel} • importance {memory.Importance}/5{pinnedSuffix}",
            ColorHex = nodeColor,
            UrlPath = urlPath,
            X = x,
            Y = y,
            Z = z,
            Radius = Math.Min(11d + memory.Importance * 0.9d + (memory.IsPinned ? 1.4d : 0d), 17d),
            OrbitCenterNodeId = parentNodeId,
            OrbitRadius = radius,
            OrbitVerticalRadius = radius * 0.9d,
            OrbitDepthRadius = radius * 0.38d,
            OrbitPhase = angle,
            OrbitSpeed = orbitSpeed,
            IsEmphasized = false
        });
        edges.Add(new PalaceVisualizerSceneEdgeViewModel
        {
            FromNodeId = parentNodeId,
            ToNodeId = $"memory:{memory.Id}",
            ColorHex = edgeColor
        });
    }

    private static void AddTodoSceneNode(
        ICollection<PalaceVisualizerSceneNodeViewModel> nodes,
        ICollection<PalaceVisualizerSceneEdgeViewModel> edges,
        string parentNodeId,
        TodoEntry todo,
        (double X, double Y, double Z) anchor,
        int index,
        int totalCount,
        string nodeColor,
        string edgeColor)
    {
        var shellCapacity = Math.Clamp((int)Math.Ceiling(Math.Sqrt(Math.Max(1, totalCount)) * 2.2d), 5, 14);
        var shellIndex = index / shellCapacity;
        var shellSlot = index % shellCapacity;
        var shellItemCount = Math.Min(shellCapacity, totalCount - (shellIndex * shellCapacity));
        var angle = ((shellSlot / Math.Max(1d, shellItemCount)) * Math.PI * 2d)
            + (shellIndex * 0.4d)
            + (StableUnitValue(todo.Id, 41) * 0.35d);
        var radius = 70d + (shellIndex * 18d) + (StableUnitValue(todo.Id, 43) * 6d);
        var orbitPhase = StableUnitValue(todo.Id, 47) * Math.PI * 2d;
        var orbitSpeed = 0.11d + (StableUnitValue(todo.Id, 53) * 0.025d);
        var x = anchor.X + Math.Cos(angle) * radius;
        var y = anchor.Y + Math.Sin(angle) * radius * 0.88d;
        var z = anchor.Z + Math.Cos((angle * 1.18d) + orbitPhase) * radius * 0.28d;

        nodes.Add(new PalaceVisualizerSceneNodeViewModel
        {
            Id = $"todo:{todo.Id}",
            Label = todo.Title,
            NodeTypeLabel = "Todo",
            SecondaryLabel = $"{MapDashboardTodoStatusLabel(todo.Status)} • updated {todo.UpdatedUtc:g}",
            ColorHex = BlendHexColor(nodeColor, "#FFFFFF", 0.12d),
            UrlPath = $"/Todos/Details/{todo.Id}",
            X = x,
            Y = y,
            Z = z,
            Radius = 10d,
            OrbitCenterNodeId = parentNodeId,
            OrbitRadius = radius,
            OrbitVerticalRadius = radius * 0.88d,
            OrbitDepthRadius = radius * 0.28d,
            OrbitPhase = angle,
            OrbitSpeed = orbitSpeed,
            IsEmphasized = false
        });
        edges.Add(new PalaceVisualizerSceneEdgeViewModel
        {
            FromNodeId = parentNodeId,
            ToNodeId = $"todo:{todo.Id}",
            ColorHex = edgeColor
        });
    }

    private static string BlendHexColor(string primaryHex, string secondaryHex, double secondaryWeight)
    {
        secondaryWeight = Math.Clamp(secondaryWeight, 0d, 1d);
        var primary = ParseHexColor(primaryHex);
        var secondary = ParseHexColor(secondaryHex);

        static int Blend(byte primary, byte secondary, double secondaryWeight)
            => (int)Math.Round((primary * (1d - secondaryWeight)) + (secondary * secondaryWeight), MidpointRounding.AwayFromZero);

        return $"#{Blend(primary.R, secondary.R, secondaryWeight):X2}{Blend(primary.G, secondary.G, secondaryWeight):X2}{Blend(primary.B, secondary.B, secondaryWeight):X2}";
    }

    private static (byte R, byte G, byte B) ParseHexColor(string hex)
    {
        var normalized = hex.Trim().TrimStart('#');
        if (normalized.Length != 6)
        {
            throw new InvalidOperationException("Expected a 6-character hex color.");
        }

        return (
            Convert.ToByte(normalized.Substring(0, 2), 16),
            Convert.ToByte(normalized.Substring(2, 2), 16),
            Convert.ToByte(normalized.Substring(4, 2), 16));
    }

    private static double StableUnitValue(Guid id, int salt)
    {
        unchecked
        {
            var hash = 1469598103934665603UL;
            foreach (var value in id.ToByteArray())
            {
                hash ^= (ulong)(value + salt);
                hash *= 1099511628211UL;
            }

            return (hash % 10000UL) / 10000d;
        }
    }
}
