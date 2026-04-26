using System.Text.RegularExpressions;
using FocusLAIci.Web.Data;
using FocusLAIci.Web.Models;
using Microsoft.EntityFrameworkCore;

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

    private readonly FocusMemoryContext _dbContext;

    public ContextService(FocusMemoryContext dbContext)
    {
        _dbContext = dbContext;
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
        var tokens = Tokenize(normalizedQuestion);
        if (tokens.Count == 0)
        {
            return null;
        }

        var resultsPerSection = Math.Clamp(effectiveInput.ResultsPerSection, 3, 10);

        var memories = await _dbContext.Memories
            .AsNoTracking()
            .Include(x => x.Wing)
            .Include(x => x.Room)
            .Include(x => x.MemoryTags)
                .ThenInclude(x => x.Tag)
            .ToListAsync(cancellationToken);

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

        var projects = await _dbContext.CodeGraphProjects
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var files = await _dbContext.CodeGraphFiles
            .AsNoTracking()
            .Include(x => x.Project)
            .ToListAsync(cancellationToken);

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

        var edgeNodeIds = await _dbContext.CodeGraphEdges
            .AsNoTracking()
            .Select(x => x.FromNodeId)
            .Concat(_dbContext.CodeGraphEdges.AsNoTracking().Select(x => x.ToNodeId))
            .ToListAsync(cancellationToken);

        var nodeDegrees = edgeNodeIds
            .GroupBy(x => x)
            .ToDictionary(x => x.Key, x => x.Count());

        var nodes = await _dbContext.CodeGraphNodes
            .AsNoTracking()
            .Include(x => x.File)
            .ToListAsync(cancellationToken);

        var memoryResults = memories
            .Select(memory =>
            {
                var score = ScoreFields(
                    normalizedQuestion,
                    tokens,
                    memory.UpdatedUtc,
                    new WeightedField(memory.Title, "title", "Title", 20m, "Title matches your question closely.", "Title shares your search terms."),
                    new WeightedField(memory.Summary, "summary", "Summary", 12m, "Summary closely matches the request.", "Summary reinforces the match."),
                    new WeightedField(TrimPreview(memory.Content, 2000), "content", "Content", 7m, "Memory content contains the full request.", "Memory content covers the same terms."),
                    new WeightedField($"{memory.Wing?.Name} {memory.Room?.Name}", "location", "Wing/room", 8m, "Wing or room naming matches the request.", "Wing or room naming overlaps the request."),
                    new WeightedField(string.Join(' ', memory.MemoryTags.Select(x => x.Tag!.Name)), "tags", "Tags", 10m, "Tags line up with the request.", "Tags overlap the request."));

                score = ApplyBoost(score, memory.IsPinned ? 4m : 0m, "Pinned memory");
                score = ApplyBoost(score, Math.Max(memory.Importance - 3, 0), "High importance");

                return new { Memory = memory, Match = score };
            })
            .Where(x => x.Match.Score > 0)
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
                ScoreLabel = FormatScore(x.Match.Score),
                MatchReason = x.Match.Reason,
                Provenance = MapProvenance(x.Match)
            })
            .ToArray();

        var todoResults = todos
            .Select(todo =>
            {
                var score = ScoreFields(
                    normalizedQuestion,
                    tokens,
                    todo.UpdatedUtc,
                    new WeightedField(todo.Title, "title", "Title", 20m, "Todo title matches your question closely.", "Todo title shares your search terms."),
                    new WeightedField(todo.Details, "details", "Details", 8m, "Todo details contain the full request.", "Todo details reinforce the match."));

                score = ApplyBoost(score, todo.Status == TodoStatus.InProgress ? 4m : todo.Status == TodoStatus.Pending ? 2m : 0m, todo.Status == TodoStatus.InProgress ? "In-progress work" : "Pending work");

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
                ScoreLabel = FormatScore(x.Match.Score),
                MatchReason = x.Match.Reason,
                Provenance = MapProvenance(x.Match)
            })
            .ToArray();

        var projectResults = projects
            .Select(project =>
            {
                var score = ScoreFields(
                    normalizedQuestion,
                    tokens,
                    project.UpdatedUtc,
                    new WeightedField(project.Name, "name", "Project name", 22m, "Project name matches your request closely.", "Project name overlaps the request."),
                    new WeightedField(project.RootPath, "path", "Project path", 18m, "Project path matches the request.", "Project path overlaps the request."),
                    new WeightedField($"{project.Description} {project.Summary}", "summary", "Description/summary", 8m, "Project description contains the full request.", "Project description reinforces the match."));

                score = ApplyBoost(score, Math.Min(project.RelationshipCount / 50m, 3m), "High relationship count");

                return new { Project = project, Match = score };
            })
            .Where(x => x.Match.Score > 0)
            .OrderByDescending(x => x.Match.Score)
            .ThenByDescending(x => x.Project.UpdatedUtc)
            .Take(Math.Max(3, Math.Min(6, resultsPerSection)))
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
                ScoreLabel = FormatScore(x.Match.Score),
                MatchReason = x.Match.Reason,
                Provenance = MapProvenance(x.Match)
            })
            .ToArray();

        var fileResults = files
            .Select(file =>
            {
                var score = ScoreFields(
                    normalizedQuestion,
                    tokens,
                    file.ScannedUtc,
                    new WeightedField(file.RelativePath, "path", "File path", 20m, "File path matches your request closely.", "File path overlaps the request."),
                    new WeightedField(file.Language, "language", "Language", 5m, "File language matches your request.", "File language reinforces the request."),
                    new WeightedField(file.Project?.Name, "project", "Project", 8m, "Project name matches your request closely.", "Project name overlaps the request."));

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
                ScoreLabel = FormatScore(x.Match.Score),
                MatchReason = x.Match.Reason,
                Provenance = MapProvenance(x.Match)
            })
            .ToArray();

        var nodeResults = nodes
            .Select(node =>
            {
                var score = ScoreFields(
                    normalizedQuestion,
                    tokens,
                    null,
                    new WeightedField(node.Label, "name", "Symbol name", 24m, "Symbol name matches your request closely.", "Symbol name overlaps the request."),
                    new WeightedField(node.File?.RelativePath, "path", "File path", 20m, "File path matches the request.", "File path overlaps the request."),
                    new WeightedField($"{node.NodeType} {node.SecondaryLabel}", "metadata", "Symbol metadata", 8m, "Symbol metadata contains the full request.", "Symbol metadata reinforces the match."));

                score = ApplyBoost(score, Math.Min(nodeDegrees.GetValueOrDefault(node.Id) / 4m, 3m), "High graph connectivity");

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

        var topMatches = memoryResults
            .Concat(todoResults)
            .Concat(ticketResults)
            .Concat(projectResults)
            .Concat(fileResults)
            .Concat(nodeResults)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Title)
            .Take(Math.Max(6, resultsPerSection))
            .ToArray();

        return new ContextPackViewModel
        {
            Question = normalizedQuestion,
            Summary = BuildSummary(memoryResults, todoResults, ticketResults, projectResults, fileResults, nodeResults),
            Input = new ContextBriefInput
            {
                Question = normalizedQuestion,
                IncludeCompletedWork = effectiveInput.IncludeCompletedWork,
                ExpandHistory = effectiveInput.ExpandHistory,
                ResultsPerSection = resultsPerSection
            },
            SearchTokens = tokens.OrderBy(x => x).ToArray(),
            TopMatches = topMatches,
            Memories = memoryResults,
            Todos = todoResults,
            Tickets = ticketResults,
            CodeGraphProjects = projectResults,
            CodeGraphFiles = fileResults,
            CodeGraphNodes = nodeResults,
            ExportText = BuildExportText(normalizedQuestion, memoryResults, todoResults, ticketResults, projectResults, fileResults, nodeResults)
        };
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
            ResultsPerSection = 8
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
            ResultsPerSection = Math.Clamp(input.Limit + 2, 3, 10)
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

    private static string BuildExportText(
        string question,
        IReadOnlyCollection<ContextRecordViewModel> memories,
        IReadOnlyCollection<ContextRecordViewModel> todos,
        IReadOnlyCollection<ContextRecordViewModel> tickets,
        IReadOnlyCollection<ContextRecordViewModel> projects,
        IReadOnlyCollection<ContextRecordViewModel> files,
        IReadOnlyCollection<ContextRecordViewModel> nodes)
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
            .AppendLine();

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
                    if (!string.IsNullOrWhiteSpace(item.MatchReason))
                    {
                        builder.Append(" | ").Append(item.MatchReason);
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
                    ScoreLabel = FormatScore(boostedScore),
                    MatchReason = isLinked && !item.MatchReason.Contains("Linked context", StringComparison.OrdinalIgnoreCase)
                        ? $"{item.MatchReason} Linked context already exists."
                        : item.MatchReason,
                    IsLinked = isLinked,
                    Provenance = provenance
                };
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Title)
            .ToArray();
    }

    private static ContextScoreOutcome ScoreFields(
        string question,
        IReadOnlyCollection<string> tokens,
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

        var tokenCoverageBoost = (decimal)matchedTokens.Count / tokens.Count * 20m;
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
            reason = matchedTokens.Count == tokens.Count
                ? "All search terms matched."
                : matchedTokens.Count == 1
                    ? $"Matched \"{matchedTokens.First()}\"."
                    : $"Matched {matchedTokens.Count} search terms.";
        }

        return new ContextScoreOutcome(
            score,
            reason,
            matchedTokens.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            fieldHits.ToArray(),
            boosts.ToArray(),
            exactPhraseMatched);
    }

    private static ContextScoreOutcome ApplyBoost(ContextScoreOutcome score, decimal amount, string label)
    {
        if (amount <= 0m)
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

    private static string NormalizePhrase(string? value) => string.Join(' ', Tokenize(value, keepStopWords: true));

    private static HashSet<string> Tokenize(string? value, bool keepStopWords = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var tokens = WordRegex()
            .Matches(value.ToLowerInvariant())
            .Select(static x => x.Value)
            .Where(static x => x.Length > 2)
            .Where(token => keepStopWords || !StopWords.Contains(token))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return tokens.Count == 0 && !keepStopWords
            ? Tokenize(value, keepStopWords: true)
            : tokens;
    }

    [GeneratedRegex("[a-z0-9]+", RegexOptions.Compiled)]
    private static partial Regex WordRegex();

    private sealed record WeightedField(string? Text, string Key, string Label, decimal PerTokenWeight, string ExactReason, string PartialReason);
    private sealed record ContextScoreOutcome(
        decimal Score,
        string Reason,
        IReadOnlyCollection<string> MatchedTokens,
        IReadOnlyCollection<ContextMatchFieldHitViewModel> FieldHits,
        IReadOnlyCollection<ContextMatchBoostViewModel> Boosts,
        bool ExactPhraseMatched)
    {
        public static ContextScoreOutcome None { get; } = new(0m, string.Empty, Array.Empty<string>(), Array.Empty<ContextMatchFieldHitViewModel>(), Array.Empty<ContextMatchBoostViewModel>(), false);
    }
    private sealed record ContextRecordKey(ContextRecordKind Kind, Guid Id);
    private sealed record LinkedContextRef(Guid LinkId, string Label, ContextRecordKind Kind, Guid TargetId);
    private sealed record NormalizedContextLink(ContextRecordKind SourceKind, Guid SourceId, ContextRecordKind TargetKind, Guid TargetId);
}
