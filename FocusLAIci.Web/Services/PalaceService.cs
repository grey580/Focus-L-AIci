using System.Text;
using FocusLAIci.Web.Data;
using FocusLAIci.Web.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FocusLAIci.Web.Services;

public sealed class PalaceService
{
    private readonly FocusMemoryContext _dbContext;
    private readonly ContextService _contextService;

    public PalaceService(FocusMemoryContext dbContext)
        : this(dbContext, new ContextService(dbContext))
    {
    }

    public PalaceService(FocusMemoryContext dbContext, ContextService contextService)
    {
        _dbContext = dbContext;
        _contextService = contextService;
    }

    public Task<DashboardViewModel> GetDashboardAsync(CancellationToken cancellationToken)
        => GetDashboardAsync((ContextBriefInput?)null, cancellationToken);

    public async Task<DashboardViewModel> GetDashboardAsync(string? contextQuestion, CancellationToken cancellationToken)
        => await GetDashboardAsync(
            string.IsNullOrWhiteSpace(contextQuestion)
                ? null
                : new ContextBriefInput
                {
                    Question = contextQuestion.Trim()
                },
            cancellationToken);

    public async Task<DashboardViewModel> GetDashboardAsync(ContextBriefInput? contextInput, CancellationToken cancellationToken)
    {
        var effectiveContextInput = contextInput ?? new ContextBriefInput();
        var memories = await BuildMemoryQuery()
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.UpdatedUtc)
            .Take(12)
            .ToListAsync(cancellationToken);
        var currentTodos = await BuildTodoQuery(includeDone: false)
            .Take(5)
            .ToListAsync(cancellationToken);
        var activeTickets = await _dbContext.Tickets
            .AsNoTracking()
            .Include(x => x.SubTickets)
            .Include(x => x.TimeLogs)
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
        var contextPack = await _contextService.BuildContextPackAsync(effectiveContextInput, cancellationToken);
        var recentActivity = recentTicketActivity
            .Concat(recentMemoryActivity)
            .Concat(recentTodoActivity)
            .OrderByDescending(x => x.OccurredUtc)
            .Take(8)
            .ToArray();
        var warningItems = BuildDashboardWarnings(
            effectiveContextInput,
            contextPack,
            currentTodos.Select(MapTodo).ToArray(),
            activeTickets.Select(MapDashboardTicketSummary).ToArray(),
            recentActivity,
            memories.Where(x => x.IsPinned).Select(MapCard).ToArray(),
            memories.Select(MapCard).ToArray(),
            wings);

        return new DashboardViewModel
        {
            Stats = await BuildStatsAsync(cancellationToken),
            ContextInput = effectiveContextInput,
            ContextPack = contextPack,
            ActiveTickets = activeTickets.Select(MapDashboardTicketSummary).ToArray(),
            RecentActivity = recentActivity,
            Wings = wings,
            RecentMemories = memories.Select(MapCard).ToArray(),
            PinnedMemories = memories.Where(x => x.IsPinned).Select(MapCard).ToArray(),
            CurrentTodos = currentTodos.Select(MapTodo).ToArray(),
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

    public async Task<DashboardDiagnosticsViewModel> GetDashboardDiagnosticsAsync(ContextBriefInput? contextInput, CancellationToken cancellationToken)
    {
        var dashboard = await GetDashboardAsync(contextInput, cancellationToken);
        var recentChanges = await GetRecentChangesAsync(16, cancellationToken);

        return new DashboardDiagnosticsViewModel
        {
            GeneratedUtc = DateTime.UtcNow,
            Stats = dashboard.Stats,
            ContextInput = dashboard.ContextInput,
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

        return new WorkspaceExportViewModel
        {
            GeneratedUtc = DateTime.UtcNow,
            Stats = await BuildStatsAsync(cancellationToken),
            ExportText = BuildWorkspaceExportText(mappedPinned, mappedTodos, mappedTickets, codeGraphProjects, recentChanges),
            PinnedMemories = mappedPinned,
            ActiveTodos = mappedTodos,
            ActiveTickets = mappedTickets,
            CodeGraphProjects = codeGraphProjects,
            RecentChanges = recentChanges
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
                    GeneralMemories = visualMemories
                        .Where(memory => memory.WingId == wing.Id && memory.RoomId is null)
                        .Select(memory => memory.Item)
                        .ToArray()
                })
                .ToArray(),
            UnsortedMemories = visualMemories
                .Where(memory => memory.WingId is null)
                .Select(memory => memory.Item)
                .ToArray(),
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
                .ToArray()
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

    public async Task<WingDetailViewModel?> GetWingAsync(string slug, CancellationToken cancellationToken)
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

        return new WingDetailViewModel
        {
            Id = wing.Id,
            Name = wing.Name,
            Description = wing.Description,
            Rooms = wing.Rooms
                .OrderBy(x => x.Name)
                .Select(x => new RoomSummaryViewModel
                {
                    Id = x.Id,
                    Name = x.Name,
                    Description = x.Description,
                    MemoryCount = wing.Memories.Count(m => m.RoomId == x.Id && m.LifecycleState == MemoryLifecycleState.Active)
                })
                .ToArray(),
            Memories = wing.Memories
                .Where(MemoryTrustHelper.IsActive)
                .OrderByDescending(x => x.IsPinned)
                .ThenByDescending(x => x.UpdatedUtc)
                .Select(MapCard)
                .ToArray()
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

    public async Task<MemoryEditorViewModel> BuildMemoryEditorAsync(Guid? id, Guid? selectedWingId, CancellationToken cancellationToken)
    {
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
                RoomOptions = await BuildRoomOptionsAsync(selectedWingId, cancellationToken)
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
            RoomOptions = await BuildRoomOptionsAsync(selectedWingId ?? memory.WingId, cancellationToken)
        };
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
        var room = input.RoomId.HasValue
            ? await _dbContext.Rooms.AsNoTracking().FirstOrDefaultAsync(x => x.Id == input.RoomId.Value, cancellationToken)
            : null;

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

        var tagNames = ParseTags(input.TagsText);
        await SyncTagsAsync(memory.Id, tagNames, cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
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
    {
        var memories = await BuildMemoryQuery(query, wingId, roomId, kind, tag, updatedSince)
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.UpdatedUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        return memories.Select(MapCard).ToArray();
    }

    private IQueryable<MemoryEntry> BuildMemoryQuery(string? query = null, Guid? wingId = null, Guid? roomId = null, MemoryKind? kind = null, string? tag = null, DateTime? updatedSince = null, bool includeRetired = false)
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
        return new PalaceStatsViewModel
        {
            WingCount = await _dbContext.Wings.CountAsync(cancellationToken),
            RoomCount = await _dbContext.Rooms.CountAsync(cancellationToken),
            MemoryCount = await _dbContext.Memories.CountAsync(x => x.LifecycleState == MemoryLifecycleState.Active, cancellationToken),
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

        return new MemoryGovernanceQueueViewModel
        {
            Items = items.Select(MapCard).ToArray(),
            ArchivedCount = await _dbContext.Memories.CountAsync(x => x.LifecycleState == MemoryLifecycleState.Archived, cancellationToken),
            SupersededCount = await _dbContext.Memories.CountAsync(x => x.LifecycleState == MemoryLifecycleState.Superseded, cancellationToken),
            NeedsReviewCount = await _dbContext.Memories.CountAsync(x => x.LifecycleState == MemoryLifecycleState.Active && x.VerificationStatus == MemoryVerificationStatus.NeedsReview, cancellationToken),
            UnverifiedActiveCount = await _dbContext.Memories.CountAsync(x => x.LifecycleState == MemoryLifecycleState.Active && x.VerificationStatus == MemoryVerificationStatus.Unverified, cancellationToken)
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
        }

        foreach (var tag in existingTags.OrderBy(x => x.Name))
        {
            _dbContext.MemoryTags.Add(new MemoryEntryTag
            {
                MemoryEntryId = memoryId,
                TagId = tag.Id
            });
        }
    }

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

    private static IReadOnlyCollection<DashboardWarningViewModel> BuildDashboardWarnings(
        ContextBriefInput contextInput,
        ContextPackViewModel? contextPack,
        IReadOnlyCollection<TodoItemViewModel> currentTodos,
        IReadOnlyCollection<TicketSummaryViewModel> activeTickets,
        IReadOnlyCollection<DashboardActivityViewModel> recentActivity,
        IReadOnlyCollection<MemoryCardViewModel> pinnedMemories,
        IReadOnlyCollection<MemoryCardViewModel> recentMemories,
        IReadOnlyCollection<WingSummaryViewModel> wings)
    {
        var warnings = new List<DashboardWarningViewModel>();
        var stalePinnedMemories = pinnedMemories.Where(x => x.IsReviewDue || x.FreshnessLabel == "Unverified").ToArray();
        var agingUnverifiedCount = recentMemories.Count(x => x.VerificationStatus == MemoryVerificationStatus.Unverified && x.FreshnessLabel == "Unverified");

        if (currentTodos.Count == 0 && activeTickets.Count == 0)
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

        if (string.IsNullOrWhiteSpace(contextInput.Question))
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
        else if (contextPack is null || contextPack.TopMatches.Count == 0)
        {
            warnings.Add(new DashboardWarningViewModel
            {
                Code = "no-context-matches",
                Severity = "warning",
                Message = "A context question was supplied, but no strong top matches were returned.",
                ActionLabel = "Open Inspect",
                ActionUrl = $"/Admin/Inspect?Question={Uri.EscapeDataString(contextInput.Question)}&IncludeCompletedWork={contextInput.IncludeCompletedWork.ToString().ToLowerInvariant()}&ExpandHistory={contextInput.ExpandHistory.ToString().ToLowerInvariant()}&ResultsPerSection={contextInput.ResultsPerSection}"
            });
        }

        return warnings;
    }

    private static string BuildWorkspaceExportText(
        IReadOnlyCollection<MemoryCardViewModel> pinnedMemories,
        IReadOnlyCollection<TodoItemViewModel> activeTodos,
        IReadOnlyCollection<TicketSummaryViewModel> activeTickets,
        IReadOnlyCollection<CodeGraphProjectCardViewModel> codeGraphProjects,
        IReadOnlyCollection<RecentChangeItemViewModel> recentChanges)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Focus workspace export");
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
            UpdatedUtc = memory.UpdatedUtc,
            Tags = memory.MemoryTags.OrderBy(x => x.Tag!.Name).Select(x => x.Tag!.Name).ToArray()
        };
    }
}
