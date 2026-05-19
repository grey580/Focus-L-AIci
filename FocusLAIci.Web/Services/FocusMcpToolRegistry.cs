using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using FocusLAIci.Web.Models;

namespace FocusLAIci.Web.Services;

public sealed class FocusMcpToolRegistry(IServiceScopeFactory scopeFactory, ILogger<FocusMcpToolRegistry> logger, FocusAgentCatalogService agentCatalogService)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    static FocusMcpToolRegistry()
    {
        SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    }

    private readonly IReadOnlyCollection<FocusMcpToolDescriptor> _tools =
    [
        CreateDescriptor(
            "focus.system.self-test",
            "Run a Focus MCP readiness check against health, workspace bootstrap, todo, and governance reads.",
            "system",
            false,
            new { type = "object", properties = new { } },
            new { type = "object", properties = new { status = new { type = "string" }, checks = new { type = "array" } } }),
        CreateDescriptor(
            "focus.memory.search",
            "Search Focus memories using the existing palace search logic.",
            "memories",
            false,
            new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string" },
                    wingId = new { type = "string", format = "uuid" },
                    roomId = new { type = "string", format = "uuid" },
                    kind = new { type = "string", @enum = Enum.GetNames<MemoryKind>() },
                    tag = new { type = "string" },
                    updatedSinceUtc = new { type = "string", format = "date-time" },
                    includeRetired = new { type = "boolean" },
                    lifecycleState = new { type = "string", @enum = Enum.GetNames<MemoryLifecycleState>() },
                    verificationStatus = new { type = "string", @enum = Enum.GetNames<MemoryVerificationStatus>() },
                    limit = new { type = "integer", minimum = 1, maximum = 50 }
                }
            },
            new { type = "object", properties = new { results = new { type = "array" } } }),
        CreateDescriptor(
            "focus.memory.get",
            "Fetch a single memory by id.",
            "memories",
            false,
            new { type = "object", required = new[] { "id" }, properties = new { id = new { type = "string", format = "uuid" } } },
            new { type = "object", properties = new { memory = new { type = "object" } } }),
        CreateDescriptor(
            "focus.memory.save",
            "Create or update a Focus memory using the existing memory editor model.",
            "memories",
            true,
            new
            {
                type = "object",
                required = new[] { "title", "summary", "content" },
                properties = new
                {
                    id = new { type = "string", format = "uuid" },
                    title = new { type = "string" },
                    summary = new { type = "string" },
                    content = new { type = "string" },
                    wingId = new { type = "string", format = "uuid" },
                    roomId = new { type = "string", format = "uuid" },
                    kind = new { type = "string", @enum = Enum.GetNames<MemoryKind>() },
                    sourceKind = new { type = "string", @enum = Enum.GetNames<SourceKind>() },
                    sourceReference = new { type = "string" },
                    importance = new { type = "integer", minimum = 1, maximum = 5 },
                    isPinned = new { type = "boolean" },
                    occurredUtc = new { type = "string", format = "date-time" },
                    tagsText = new { type = "string" }
                }
            },
            new { type = "object", properties = new { saved = new { type = "boolean" }, id = new { type = "string", format = "uuid" }, duplicateSuggestions = new { type = "array" } } }),
        CreateDescriptor(
            "focus.memory.duplicates",
            "Find likely duplicate memories for an existing memory or draft.",
            "memories",
            false,
            new
            {
                type = "object",
                properties = new
                {
                    id = new { type = "string", format = "uuid" },
                    title = new { type = "string" },
                    summary = new { type = "string" },
                    content = new { type = "string" }
                }
            },
            new { type = "object", properties = new { duplicates = new { type = "array" } } }),
        CreateDescriptor(
            "focus.memory.merge",
            "Merge a source memory into a target memory, combine tags/content, and supersede the source.",
            "memories",
            true,
            new { type = "object", required = new[] { "sourceMemoryId", "targetMemoryId" }, properties = new { sourceMemoryId = new { type = "string", format = "uuid" }, targetMemoryId = new { type = "string", format = "uuid" }, reason = new { type = "string" } } },
            new { type = "object", properties = new { targetMemoryId = new { type = "string", format = "uuid" } } }),
        CreateDescriptor(
            "focus.memory.resolve-canonical",
            "Resolve the canonical active memory and supersession trail for a memory.",
            "memories",
            false,
            new { type = "object", required = new[] { "id" }, properties = new { id = new { type = "string", format = "uuid" } } },
            new { type = "object", properties = new { canonical = new { type = "object" }, trail = new { type = "array" } } }),
        CreateDescriptor(
            "focus.memory.verify",
            "Mark a memory verified.",
            "memories",
            true,
            new { type = "object", required = new[] { "id" }, properties = new { id = new { type = "string", format = "uuid" } } },
            new { type = "object", properties = new { id = new { type = "string", format = "uuid" }, status = new { type = "string" } } }),
        CreateDescriptor(
            "focus.memory.mark-review",
            "Mark a memory as needing review.",
            "memories",
            true,
            new { type = "object", required = new[] { "id" }, properties = new { id = new { type = "string", format = "uuid" } } },
            new { type = "object", properties = new { id = new { type = "string", format = "uuid" }, status = new { type = "string" } } }),
        CreateDescriptor(
            "focus.memory.archive",
            "Archive an existing memory.",
            "memories",
            true,
            new { type = "object", required = new[] { "id" }, properties = new { id = new { type = "string", format = "uuid" }, reason = new { type = "string" } } },
            new { type = "object", properties = new { id = new { type = "string", format = "uuid" }, state = new { type = "string" } } }),
        CreateDescriptor(
            "focus.memory.restore",
            "Restore an archived or superseded memory to active state.",
            "memories",
            true,
            new { type = "object", required = new[] { "id" }, properties = new { id = new { type = "string", format = "uuid" } } },
            new { type = "object", properties = new { id = new { type = "string", format = "uuid" }, state = new { type = "string" } } }),
        CreateDescriptor(
            "focus.memory.supersede",
            "Supersede one memory with another active memory.",
            "memories",
            true,
            new { type = "object", required = new[] { "id", "replacementMemoryId" }, properties = new { id = new { type = "string", format = "uuid" }, replacementMemoryId = new { type = "string", format = "uuid" }, reason = new { type = "string" } } },
            new { type = "object", properties = new { id = new { type = "string", format = "uuid" }, replacementMemoryId = new { type = "string", format = "uuid" }, state = new { type = "string" } } }),
        CreateDescriptor(
            "focus.memory.update-tags",
            "Replace the explicit tag list for a memory.",
            "memories",
            true,
            new { type = "object", required = new[] { "id" }, properties = new { id = new { type = "string", format = "uuid" }, tagsText = new { type = "string" } } },
            new { type = "object", properties = new { id = new { type = "string", format = "uuid" }, tags = new { type = "array" } } }),
        CreateDescriptor(
            "focus.memory.governance-queue",
            "Fetch the current memory governance queue and trust counts.",
            "memories",
            false,
            new { type = "object", properties = new { } },
            new { type = "object", properties = new { queue = new { type = "object" } } }),
        CreateDescriptor(
            "focus.wing.list",
            "List Focus wings, optionally filtered by query.",
            "palace",
            false,
            new { type = "object", properties = new { query = new { type = "string" } } },
            new { type = "object", properties = new { wings = new { type = "array" } } }),
        CreateDescriptor(
            "focus.room.list",
            "List Focus rooms and resolve them by wing id, slug, or name.",
            "palace",
            false,
            new
            {
                type = "object",
                properties = new
                {
                    wingId = new { type = "string", format = "uuid" },
                    wingSlug = new { type = "string" },
                    wingName = new { type = "string" },
                    query = new { type = "string" }
                }
            },
            new { type = "object", properties = new { rooms = new { type = "array" } } }),
        CreateDescriptor(
            "focus.agent.list",
            "List the built-in Focus scoped agents.",
            "agents",
            false,
            new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string" },
                    goal = new { type = "string", @enum = Enum.GetNames<ContextPackGoal>() },
                    supportsWriteActions = new { type = "boolean" }
                }
            },
            new { type = "object", properties = new { agents = new { type = "array" } } }),
        CreateDescriptor(
            "focus.agent.get",
            "Fetch a single Focus scoped agent by slug.",
            "agents",
            false,
            new { type = "object", required = new[] { "slug" }, properties = new { slug = new { type = "string" } } },
            new { type = "object", properties = new { agent = new { type = "object" } } }),
        CreateDescriptor(
            "focus.agent.recommend",
            "Recommend the most relevant Focus agents for a task question and pack goal.",
            "agents",
            false,
            new
            {
                type = "object",
                properties = new
                {
                    question = new { type = "string" },
                    goal = new { type = "string", @enum = Enum.GetNames<ContextPackGoal>() },
                    limit = new { type = "integer", minimum = 1, maximum = 4 }
                }
            },
            new { type = "object", properties = new { agents = new { type = "array" } } }),
        CreateDescriptor(
            "focus.agent.run",
            "Build an actionable task brief for a Focus agent using a concrete task question.",
            "agents",
            false,
            new
            {
                type = "object",
                required = new[] { "slug", "question" },
                properties = new
                {
                    slug = new { type = "string" },
                    question = new { type = "string" },
                    includeCompletedWork = new { type = "boolean" },
                    expandHistory = new { type = "boolean" },
                    resultsPerSection = new { type = "integer", minimum = 3, maximum = 10 }
                }
            },
            new { type = "object", properties = new { run = new { type = "object" } } }),
        CreateDescriptor(
            "focus.skill.list",
            "List Focus skills and reusable runbook workflows.",
            "skills",
            false,
            new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string" },
                    category = new { type = "string", @enum = Enum.GetNames<SkillCategory>() },
                    wingId = new { type = "string", format = "uuid" },
                    pinnedOnly = new { type = "boolean" },
                    needsReviewOnly = new { type = "boolean" }
                }
            },
            new { type = "object", properties = new { skills = new { type = "array" } } }),
        CreateDescriptor(
            "focus.skill.get",
            "Fetch a single Focus skill by slug.",
            "skills",
            false,
            new { type = "object", required = new[] { "slug" }, properties = new { slug = new { type = "string" } } },
            new { type = "object", properties = new { skill = new { type = "object" } } }),
        CreateDescriptor(
            "focus.skill.recommend",
            "Recommend the most relevant Focus skills for a task question.",
            "skills",
            false,
            new
            {
                type = "object",
                required = new[] { "question" },
                properties = new
                {
                    question = new { type = "string" },
                    wingId = new { type = "string", format = "uuid" },
                    category = new { type = "string", @enum = Enum.GetNames<SkillCategory>() },
                    limit = new { type = "integer", minimum = 1, maximum = 12 }
                 }
             },
             new { type = "object", properties = new { skills = new { type = "array" } } }),
        CreateDescriptor(
            "focus.skill.create",
            "Create a Focus skill in the editable catalog.",
            "skills",
            true,
            new
            {
                type = "object",
                required = new[] { "name", "summary", "category", "whenToUse", "flow" },
                properties = new
                {
                    name = new { type = "string" },
                    summary = new { type = "string" },
                    category = new { type = "string", @enum = Enum.GetNames<SkillCategory>() },
                    whenToUse = new { type = "string" },
                    flow = new { type = "string" },
                    examplesText = new { type = "string" },
                    triggerHintsText = new { type = "string" },
                    wingId = new { type = "string", format = "uuid" },
                    isPinned = new { type = "boolean" }
                }
            },
            new
            {
                type = "object",
                properties = new
                {
                    created = new { type = "boolean" },
                    id = new { type = "string", format = "uuid" },
                    slug = new { type = "string" }
                }
            }),
        CreateDescriptor(
            "focus.skill.update",
            "Update an editable Focus skill.",
            "skills",
            true,
            new
            {
                type = "object",
                required = new[] { "id", "name", "summary", "category", "whenToUse", "flow" },
                properties = new
                {
                    id = new { type = "string", format = "uuid" },
                    name = new { type = "string" },
                    summary = new { type = "string" },
                    category = new { type = "string", @enum = Enum.GetNames<SkillCategory>() },
                    whenToUse = new { type = "string" },
                    flow = new { type = "string" },
                    examplesText = new { type = "string" },
                    triggerHintsText = new { type = "string" },
                    wingId = new { type = "string", format = "uuid" },
                    isPinned = new { type = "boolean" }
                }
            },
            new
            {
                type = "object",
                properties = new
                {
                    updated = new { type = "boolean" },
                    id = new { type = "string", format = "uuid" },
                    slug = new { type = "string" }
                }
            }),
        CreateDescriptor(
            "focus.skill.delete",
            "Delete an editable Focus skill.",
            "skills",
            true,
            new
            {
                type = "object",
                required = new[] { "id" },
                properties = new
                {
                    id = new { type = "string", format = "uuid" }
                }
            },
            new
            {
                type = "object",
                properties = new
                {
                    deleted = new { type = "boolean" },
                    id = new { type = "string", format = "uuid" }
                }
            }),
        CreateDescriptor(
            "focus.ticket.board",
            "Get the Focus ticket board summary.",
            "tickets",
            false,
            new { type = "object", properties = new { completedSearch = new { type = "string" }, completedPage = new { type = "integer", minimum = 1 } } },
            new { type = "object", properties = new { board = new { type = "object" } } }),
        CreateDescriptor(
            "focus.ticket.details",
            "Get ticket details, notes, subtickets, and activity.",
            "tickets",
            false,
            new { type = "object", required = new[] { "id" }, properties = new { id = new { type = "string", format = "uuid" } } },
            new { type = "object", properties = new { ticket = new { type = "object" } } }),
        CreateDescriptor(
            "focus.ticket.update-status",
            "Update an existing ticket status.",
            "tickets",
            true,
            new { type = "object", required = new[] { "id", "status" }, properties = new { id = new { type = "string", format = "uuid" }, status = new { type = "string" } } },
            new { type = "object", properties = new { id = new { type = "string", format = "uuid" }, status = new { type = "string" } } }),
        CreateDescriptor(
            "focus.ticket.add-note",
            "Append a note to an existing Focus ticket.",
            "tickets",
            true,
            new { type = "object", required = new[] { "ticketId", "author", "content" } },
            new { type = "object", properties = new { noteId = new { type = "string", format = "uuid" } } }),
        CreateDescriptor(
            "focus.todo.board",
            "Get the Focus todo board summary.",
            "todos",
            false,
            new { type = "object", properties = new { } },
            new { type = "object", properties = new { board = new { type = "object" } } }),
        CreateDescriptor(
            "focus.todo.create",
            "Create a Focus todo.",
            "todos",
            true,
            new { type = "object", required = new[] { "title", "status" } },
            new { type = "object", properties = new { id = new { type = "string", format = "uuid" } } }),
        CreateDescriptor(
            "focus.todo.update-status",
            "Update an existing Focus todo status.",
            "todos",
            true,
            new { type = "object", required = new[] { "id", "status" } },
            new { type = "object", properties = new { id = new { type = "string", format = "uuid" }, status = new { type = "string" } } }),
        CreateDescriptor(
            "focus.context.inspect",
            "Build a Focus context pack for a specific question.",
            "context",
            false,
            new
            {
                type = "object",
                required = new[] { "question" },
                properties = new
                {
                    question = new { type = "string" },
                    wingId = new { type = "string", format = "uuid" },
                    roomId = new { type = "string", format = "uuid" },
                    kind = new { type = "string", @enum = Enum.GetNames<MemoryKind>() },
                    tag = new { type = "string" },
                    includeRetired = new { type = "boolean" },
                    includeCompletedWork = new { type = "boolean" },
                    expandHistory = new { type = "boolean" },
                    resultsPerSection = new { type = "integer", minimum = 3, maximum = 10 },
                    packGoal = new { type = "string", @enum = Enum.GetNames<ContextPackGoal>() },
                    preferRecentChanges = new { type = "boolean" }
                }
            },
            new { type = "object", properties = new { contextPack = new { type = "object" } } }),
        CreateDescriptor(
            "focus.codegraph.projects",
            "List code graph projects known to Focus.",
            "code-graph",
            false,
            new { type = "object", properties = new { } },
            new { type = "object", properties = new { projects = new { type = "array" } } }),
        CreateDescriptor(
            "focus.codegraph.project",
            "Get details for a saved code graph project.",
            "code-graph",
            false,
            new { type = "object", required = new[] { "projectId" } },
            new { type = "object", properties = new { project = new { type = "object" } } }),
        CreateDescriptor(
            "focus.codegraph.rescan",
            "Rescan an existing code graph project.",
            "code-graph",
            true,
            new { type = "object", required = new[] { "projectId" } },
            new { type = "object", properties = new { projectId = new { type = "string", format = "uuid" } } })
    ];

    public IReadOnlyCollection<FocusMcpToolDescriptor> GetTools() => _tools;

    public bool TryGetDescriptor(string toolName, out FocusMcpToolDescriptor? descriptor)
    {
        descriptor = _tools.FirstOrDefault(x => string.Equals(x.Name, toolName, StringComparison.OrdinalIgnoreCase));
        return descriptor is not null;
    }

    public IReadOnlyCollection<string> Complete(string prefix)
    {
        return _tools
            .Select(x => x.Name)
            .Where(x => x.StartsWith(prefix ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x)
            .ToArray();
    }

    public async Task<object> InvokeAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken)
    {
        logger.LogInformation("Handling Focus MCP tool call {ToolName}", toolName);

        await using var scope = scopeFactory.CreateAsyncScope();
        var palaceService = scope.ServiceProvider.GetRequiredService<PalaceService>();
        var ticketingService = scope.ServiceProvider.GetRequiredService<TicketingService>();
        var contextService = scope.ServiceProvider.GetRequiredService<ContextService>();
        var codeGraphService = scope.ServiceProvider.GetRequiredService<CodeGraphService>();
        var diagnosticsService = scope.ServiceProvider.GetRequiredService<FocusDiagnosticsService>();

        return toolName switch
        {
            "focus.system.self-test" => await diagnosticsService.RunMcpSelfTestAsync("mcp-tool", cancellationToken),
            "focus.memory.search" => await SearchMemoriesAsync(palaceService, arguments, cancellationToken),
            "focus.memory.get" => await GetMemoryAsync(palaceService, arguments, cancellationToken),
            "focus.memory.save" => await SaveMemoryAsync(palaceService, arguments, cancellationToken),
            "focus.memory.duplicates" => await GetMemoryDuplicatesAsync(palaceService, arguments, cancellationToken),
            "focus.memory.merge" => await MergeMemoryAsync(palaceService, arguments, cancellationToken),
            "focus.memory.resolve-canonical" => await ResolveCanonicalMemoryAsync(palaceService, arguments, cancellationToken),
            "focus.memory.verify" => await VerifyMemoryAsync(palaceService, arguments, cancellationToken),
            "focus.memory.mark-review" => await MarkMemoryReviewAsync(palaceService, arguments, cancellationToken),
            "focus.memory.archive" => await ArchiveMemoryAsync(palaceService, arguments, cancellationToken),
            "focus.memory.restore" => await RestoreMemoryAsync(palaceService, arguments, cancellationToken),
            "focus.memory.supersede" => await SupersedeMemoryAsync(palaceService, arguments, cancellationToken),
            "focus.memory.update-tags" => await UpdateMemoryTagsAsync(palaceService, arguments, cancellationToken),
            "focus.memory.governance-queue" => await GetMemoryGovernanceQueueAsync(palaceService, cancellationToken),
            "focus.wing.list" => await GetWingsAsync(palaceService, arguments, cancellationToken),
            "focus.room.list" => await GetRoomsAsync(palaceService, arguments, cancellationToken),
            "focus.agent.list" => GetAgents(arguments),
            "focus.agent.get" => await GetAgentAsync(palaceService, arguments, cancellationToken),
            "focus.agent.recommend" => RecommendAgents(arguments),
            "focus.agent.run" => await RunAgentAsync(palaceService, arguments, cancellationToken),
            "focus.skill.list" => await GetSkillsAsync(palaceService, arguments, cancellationToken),
            "focus.skill.get" => await GetSkillAsync(palaceService, arguments, cancellationToken),
            "focus.skill.recommend" => await RecommendSkillsAsync(palaceService, arguments, cancellationToken),
            "focus.skill.create" => await CreateSkillAsync(palaceService, arguments, cancellationToken),
            "focus.skill.update" => await UpdateSkillAsync(palaceService, arguments, cancellationToken),
            "focus.skill.delete" => await DeleteSkillAsync(palaceService, arguments, cancellationToken),
            "focus.ticket.board" => await GetTicketBoardAsync(ticketingService, arguments, cancellationToken),
            "focus.ticket.details" => await GetTicketDetailsAsync(ticketingService, arguments, cancellationToken),
            "focus.ticket.update-status" => await UpdateTicketStatusAsync(ticketingService, arguments, cancellationToken),
            "focus.ticket.add-note" => await AddTicketNoteAsync(ticketingService, arguments, cancellationToken),
            "focus.todo.board" => await GetTodoBoardAsync(palaceService, cancellationToken),
            "focus.todo.create" => await CreateTodoAsync(palaceService, arguments, cancellationToken),
            "focus.todo.update-status" => await UpdateTodoStatusAsync(palaceService, arguments, cancellationToken),
            "focus.context.inspect" => await BuildContextAsync(contextService, arguments, cancellationToken),
            "focus.codegraph.projects" => await GetCodeGraphProjectsAsync(codeGraphService, cancellationToken),
            "focus.codegraph.project" => await GetCodeGraphProjectAsync(codeGraphService, arguments, cancellationToken),
            "focus.codegraph.rescan" => await RescanCodeGraphProjectAsync(codeGraphService, arguments, cancellationToken),
            _ => throw new InvalidOperationException($"Unknown Focus MCP tool '{toolName}'.")
        };
    }

    private static FocusMcpToolDescriptor CreateDescriptor(string name, string description, string category, bool mutating, object inputSchema, object outputSchema)
    {
        return new FocusMcpToolDescriptor
        {
            Name = name,
            Description = description,
            Category = category,
            Mutating = mutating,
            InputSchema = inputSchema,
            OutputSchema = outputSchema
        };
    }

    private static async Task<object> SearchMemoriesAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<SearchMemoriesToolInput>(arguments);
        Validate(input);
        var results = await palaceService.SearchMemoriesAsync(
            input.Query,
            input.WingId,
            input.RoomId,
            input.Kind,
            input.Tag,
            input.UpdatedSinceUtc,
            input.IncludeRetired,
            input.LifecycleState,
            input.VerificationStatus,
            cancellationToken);
        return new
        {
            appliedFilters = new
            {
                input.WingId,
                input.RoomId,
                input.Kind,
                input.Tag,
                input.IncludeRetired,
                input.LifecycleState,
                input.VerificationStatus
            },
            results = results.Take(input.Limit).ToArray()
        };
    }

    private static async Task<object> GetMemoryAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<FocusGuidInput>(arguments);
        Validate(input);
        var memory = await palaceService.GetMemoryAsync(input.Id, cancellationToken) ?? throw new InvalidOperationException("That memory no longer exists.");
        var duplicateSuggestions = await palaceService.FindDuplicateSuggestionsAsync(new MemoryEditorInput
        {
            Id = input.Id,
            Title = memory.Memory.Title,
            Summary = memory.Memory.Summary,
            Content = memory.Content
        }, cancellationToken);
        var canonical = await palaceService.ResolveCanonicalMemoryAsync(input.Id, cancellationToken);
        return new { memory, canonical = canonical.Canonical, trail = canonical.Trail, duplicateSuggestions };
    }

    private static async Task<object> SaveMemoryAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<MemorySaveToolInput>(arguments);
        Validate(input);
        var editorInput = input.ToEditorInput();
        var duplicates = input.DetectDuplicates || input.DryRun || input.RequireConfirmationOnDuplicate
            ? await palaceService.FindDuplicateSuggestionsAsync(editorInput, cancellationToken)
            : Array.Empty<DuplicateSuggestionViewModel>();
        var blockingDuplicates = duplicates.Where(x => x.Score >= input.DuplicateThreshold).ToArray();

        if (input.DryRun)
        {
            return new
            {
                saved = false,
                dryRun = true,
                duplicateSuggestions = duplicates,
                blockedByDuplicates = false,
                preview = editorInput
            };
        }

        if (input.RequireConfirmationOnDuplicate && blockingDuplicates.Length > 0 && !input.ConfirmDuplicate)
        {
            return new
            {
                saved = false,
                dryRun = false,
                duplicateSuggestions = duplicates,
                blockedByDuplicates = true,
                message = "Duplicate confirmation is required before saving this memory."
            };
        }

        var id = await palaceService.SaveMemoryAsync(editorInput, cancellationToken);
        return new
        {
            saved = true,
            id,
            duplicateSuggestions = duplicates,
            blockedByDuplicates = false
        };
    }

    private static async Task<object> GetMemoryDuplicatesAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<MemoryDuplicateToolInput>(arguments);
        Validate(input);

        MemoryEditorInput draft;
        if (input.Id.HasValue)
        {
            var memory = await palaceService.GetMemoryAsync(input.Id.Value, cancellationToken) ?? throw new InvalidOperationException("That memory no longer exists.");
            draft = new MemoryEditorInput
            {
                Id = input.Id,
                Title = memory.Memory.Title,
                Summary = memory.Memory.Summary,
                Content = memory.Content
            };
        }
        else
        {
            draft = new MemoryEditorInput
            {
                Title = input.Title ?? string.Empty,
                Summary = input.Summary ?? string.Empty,
                Content = input.Content ?? string.Empty
            };
        }

        return new { duplicates = await palaceService.FindDuplicateSuggestionsAsync(draft, cancellationToken) };
    }

    private static async Task<object> MergeMemoryAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<MemoryMergeToolInput>(arguments);
        Validate(input);
        var targetMemoryId = await palaceService.MergeMemoryAsync(input.SourceMemoryId, input.TargetMemoryId, input.Reason, cancellationToken);
        var canonical = await palaceService.ResolveCanonicalMemoryAsync(targetMemoryId, cancellationToken);
        return new { targetMemoryId, canonical = canonical.Canonical, trail = canonical.Trail };
    }

    private static async Task<object> ResolveCanonicalMemoryAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<FocusGuidInput>(arguments);
        Validate(input);
        var canonical = await palaceService.ResolveCanonicalMemoryAsync(input.Id, cancellationToken);
        return new { canonical = canonical.Canonical, trail = canonical.Trail };
    }

    private static async Task<object> VerifyMemoryAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<FocusGuidInput>(arguments);
        Validate(input);
        await palaceService.MarkMemoryVerifiedAsync(input.Id, cancellationToken);
        return new { id = input.Id, status = nameof(MemoryVerificationStatus.Verified) };
    }

    private static async Task<object> MarkMemoryReviewAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<FocusGuidInput>(arguments);
        Validate(input);
        await palaceService.MarkMemoryNeedsReviewAsync(input.Id, cancellationToken);
        return new { id = input.Id, status = nameof(MemoryVerificationStatus.NeedsReview) };
    }

    private static async Task<object> ArchiveMemoryAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<MemoryArchiveToolInput>(arguments);
        Validate(input);
        await palaceService.ArchiveMemoryAsync(input.Id, input.Reason, cancellationToken);
        return new { id = input.Id, state = nameof(MemoryLifecycleState.Archived) };
    }

    private static async Task<object> RestoreMemoryAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<FocusGuidInput>(arguments);
        Validate(input);
        await palaceService.MarkMemoryActiveAsync(input.Id, cancellationToken);
        return new { id = input.Id, state = nameof(MemoryLifecycleState.Active) };
    }

    private static async Task<object> SupersedeMemoryAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<MemorySupersedeToolInput>(arguments);
        Validate(input);
        await palaceService.SupersedeMemoryAsync(input.Id, input.ReplacementMemoryId, input.Reason, cancellationToken);
        return new { id = input.Id, replacementMemoryId = input.ReplacementMemoryId, state = nameof(MemoryLifecycleState.Superseded) };
    }

    private static async Task<object> UpdateMemoryTagsAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<MemoryTagsToolInput>(arguments);
        Validate(input);
        await palaceService.UpdateMemoryTagsAsync(input.Id, input.TagsText, cancellationToken);
        var memory = await palaceService.GetMemoryAsync(input.Id, cancellationToken) ?? throw new InvalidOperationException("That memory no longer exists.");
        return new { id = input.Id, tags = memory.Memory.Tags };
    }

    private static async Task<object> GetMemoryGovernanceQueueAsync(PalaceService palaceService, CancellationToken cancellationToken)
    {
        var queue = await palaceService.GetMemoryGovernanceQueueAsync(cancellationToken);
        return new { queue, summary = queue.Summary };
    }

    private static async Task<object> GetWingsAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<WingListToolInput>(arguments);
        return new { wings = await palaceService.GetWingSummariesAsync(input.Query, cancellationToken) };
    }

    private static async Task<object> GetRoomsAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<RoomListToolInput>(arguments);
        Validate(input);
        return new { rooms = await palaceService.GetRoomsAsync(input.WingId, input.WingSlug, input.WingName, input.Query, cancellationToken) };
    }

    object GetAgents(JsonElement arguments)
    {
        var input = Deserialize<AgentListToolInput>(arguments);
        return new { agents = agentCatalogService.GetCatalog(input.Query, input.Goal, input.SupportsWriteActions) };
    }

    private static async Task<object> GetAgentAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<AgentGetToolInput>(arguments);
        Validate(input);
        var agent = await palaceService.GetAgentAsync(input.Slug, cancellationToken)
            ?? throw new InvalidOperationException("That agent no longer exists.");
        return new { agent };
    }

    object RecommendAgents(JsonElement arguments)
    {
        var input = Deserialize<AgentRecommendToolInput>(arguments);
        Validate(input);
        return new { agents = agentCatalogService.RecommendAgents(input.Question, input.Goal, input.Limit) };
    }

    private static async Task<object> RunAgentAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<AgentRunToolInput>(arguments);
        Validate(input);
        var detail = await palaceService.RunAgentAsync(input.Slug, new AgentRunInput
        {
            Question = input.Question,
            IncludeCompletedWork = input.IncludeCompletedWork,
            ExpandHistory = input.ExpandHistory,
            ResultsPerSection = input.ResultsPerSection
        }, cancellationToken)
            ?? throw new InvalidOperationException("That agent no longer exists.");
        return new { run = detail.RunResult, agent = detail.Agent };
    }

    private static async Task<object> GetSkillsAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<SkillListToolInput>(arguments);
        return new
        {
            skills = await palaceService.GetSkillSummariesAsync(
                input.Query,
                input.Category,
                cancellationToken,
                input.WingId,
                input.PinnedOnly,
                input.NeedsReviewOnly)
        };
    }

    private static async Task<object> GetSkillAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<SkillGetToolInput>(arguments);
        Validate(input);
        var skill = await palaceService.GetSkillAsync(input.Slug, cancellationToken, trackUsage: true)
            ?? throw new InvalidOperationException("That skill no longer exists.");
        return new { skill };
    }

    private static async Task<object> RecommendSkillsAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<SkillRecommendToolInput>(arguments);
        Validate(input);
        return new
        {
            skills = await palaceService.RecommendSkillsAsync(input.Question, input.WingId, input.Category, input.Limit, cancellationToken)
        };
    }

    private static async Task<object> CreateSkillAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<SkillCreateToolInput>(arguments);
        Validate(input);
        var id = await palaceService.SaveSkillAsync(input.ToEditorInput(), cancellationToken);
        return new { created = true, id, slug = SlugUtility.CreateSlug(input.Name) };
    }

    private static async Task<object> UpdateSkillAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<SkillUpdateToolInput>(arguments);
        Validate(input);
        await palaceService.SaveSkillAsync(input.ToEditorInput(), cancellationToken);
        return new { updated = true, id = input.Id, slug = SlugUtility.CreateSlug(input.Name) };
    }

    private static async Task<object> DeleteSkillAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<FocusGuidInput>(arguments);
        Validate(input);
        await palaceService.DeleteSkillAsync(input.Id, cancellationToken);
        return new { deleted = true, id = input.Id };
    }

    private static async Task<object> GetTicketBoardAsync(TicketingService ticketingService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<TicketBoardToolInput>(arguments);
        var board = await ticketingService.GetBoardAsync(input.CompletedSearch, input.CompletedPage, cancellationToken);
        return new { board };
    }

    private static async Task<object> GetTicketDetailsAsync(TicketingService ticketingService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<FocusGuidInput>(arguments);
        Validate(input);
        return new { ticket = await ticketingService.GetDetailsAsync(input.Id, cancellationToken) };
    }

    private static async Task<object> UpdateTicketStatusAsync(TicketingService ticketingService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<TicketStatusToolInput>(arguments);
        Validate(input);
        await ticketingService.UpdateTicketStatusAsync(input.Id, input.Status, cancellationToken);
        return new { id = input.Id, status = input.Status.ToString() };
    }

    private static async Task<object> AddTicketNoteAsync(TicketingService ticketingService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<TicketNoteToolInput>(arguments);
        Validate(input);
        var noteId = await ticketingService.AddNoteAsync(input.TicketId, new TicketNoteInput
        {
            Author = input.Author,
            Content = input.Content
        }, cancellationToken);
        return new { noteId };
    }

    private static async Task<object> GetTodoBoardAsync(PalaceService palaceService, CancellationToken cancellationToken)
    {
        return new { board = await palaceService.GetTodoBoardAsync(cancellationToken) };
    }

    private static async Task<object> CreateTodoAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<TodoEditorInput>(arguments);
        Validate(input);
        var id = await palaceService.CreateTodoAsync(input, cancellationToken);
        return new { id };
    }

    private static async Task<object> UpdateTodoStatusAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<TodoStatusToolInput>(arguments);
        Validate(input);
        await palaceService.UpdateTodoStatusAsync(input.Id, input.Status, cancellationToken);
        return new { id = input.Id, status = input.Status.ToString() };
    }

    private static async Task<object> BuildContextAsync(ContextService contextService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<ContextBriefInput>(arguments);
        Validate(input);
        var contextPack = await contextService.BuildContextPackAsync(input, cancellationToken);
        return new { contextPack };
    }

    private static async Task<object> GetCodeGraphProjectsAsync(CodeGraphService codeGraphService, CancellationToken cancellationToken)
    {
        var board = await codeGraphService.GetBoardAsync(cancellationToken);
        return new { projects = board.Projects };
    }

    private static async Task<object> GetCodeGraphProjectAsync(CodeGraphService codeGraphService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<CodeGraphProjectToolInput>(arguments);
        Validate(input);
        var project = await codeGraphService.GetProjectAsync(input.ProjectId, input.Query, input.SelectedNodeId, input.SelectedFileId, cancellationToken)
            ?? throw new InvalidOperationException("That code graph project no longer exists.");
        return new { project };
    }

    private static async Task<object> RescanCodeGraphProjectAsync(CodeGraphService codeGraphService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<CodeGraphRescanToolInput>(arguments);
        Validate(input);
        await codeGraphService.RescanProjectAsync(input.ProjectId, cancellationToken);
        return new { projectId = input.ProjectId };
    }

    private static T Deserialize<T>(JsonElement arguments)
    {
        if (arguments.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return Activator.CreateInstance<T>();
        }

        try
        {
            return JsonSerializer.Deserialize<T>(arguments.GetRawText(), SerializerOptions)
                ?? throw new FocusMcpInputException("Unable to parse MCP tool arguments.");
        }
        catch (JsonException ex)
        {
            throw new FocusMcpInputException(
                $"Unable to parse MCP tool arguments at '{ex.Path ?? "$"}'.",
                new
                {
                    path = ex.Path,
                    lineNumber = ex.LineNumber,
                    bytePositionInLine = ex.BytePositionInLine
                });
        }
    }

    private static void Validate(object instance)
    {
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(instance, new ValidationContext(instance), results, true);
        if (isValid)
        {
            return;
        }

        throw new FocusMcpInputException(
            "The MCP tool input is invalid.",
            results
                .GroupBy(result => result.MemberNames.FirstOrDefault() ?? string.Empty)
                .ToDictionary(
                    group => string.IsNullOrWhiteSpace(group.Key) ? "$" : group.Key,
                    group => group.Select(result => result.ErrorMessage ?? "Invalid value.").ToArray()));
    }

    private sealed class SearchMemoriesToolInput : IValidatableObject
    {
        public string? Query { get; set; }
        public Guid? WingId { get; set; }
        public Guid? RoomId { get; set; }
        public MemoryKind? Kind { get; set; }
        public string? Tag { get; set; }
        public DateTime? UpdatedSinceUtc { get; set; }
        public bool IncludeRetired { get; set; }
        public MemoryLifecycleState? LifecycleState { get; set; }
        public MemoryVerificationStatus? VerificationStatus { get; set; }
        [Range(1, 50)]
        public int Limit { get; set; } = 10;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (!IncludeRetired && LifecycleState.HasValue && LifecycleState != MemoryLifecycleState.Active)
            {
                yield return new ValidationResult("Set includeRetired=true when filtering for archived or superseded memories.", [nameof(IncludeRetired), nameof(LifecycleState)]);
            }
        }
    }

    private sealed class MemorySaveToolInput
    {
        public Guid? Id { get; set; }

        [Required]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        public string Summary { get; set; } = string.Empty;

        [Required]
        public string Content { get; set; } = string.Empty;

        public MemoryKind Kind { get; set; } = MemoryKind.Decision;
        public SourceKind SourceKind { get; set; } = SourceKind.ManualNote;
        [StringLength(260)]
        public string SourceReference { get; set; } = string.Empty;
        [Range(1, 5)]
        public int Importance { get; set; } = 3;
        public bool IsPinned { get; set; }
        public DateTime? OccurredUtc { get; set; }
        public Guid? WingId { get; set; }
        public Guid? RoomId { get; set; }
        public string TagsText { get; set; } = string.Empty;
        public bool DryRun { get; set; }
        public bool DetectDuplicates { get; set; } = true;
        public bool RequireConfirmationOnDuplicate { get; set; }
        public bool ConfirmDuplicate { get; set; }
        [Range(1, 100)]
        public decimal DuplicateThreshold { get; set; } = 75m;

        public MemoryEditorInput ToEditorInput() => new()
        {
            Id = Id,
            Title = Title,
            Summary = Summary,
            Content = Content,
            Kind = Kind,
            SourceKind = SourceKind,
            SourceReference = SourceReference,
            Importance = Importance,
            IsPinned = IsPinned,
            OccurredUtc = OccurredUtc,
            WingId = WingId,
            RoomId = RoomId,
            TagsText = TagsText
        };
    }

    private sealed class MemoryDuplicateToolInput : IValidatableObject
    {
        public Guid? Id { get; set; }
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public string? Content { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Id.HasValue)
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(Title) && string.IsNullOrWhiteSpace(Summary) && string.IsNullOrWhiteSpace(Content))
            {
                yield return new ValidationResult("Provide an id or a draft title, summary, or content.", [nameof(Id), nameof(Title), nameof(Summary), nameof(Content)]);
            }
        }
    }

    private sealed class MemoryMergeToolInput
    {
        [Required]
        public Guid SourceMemoryId { get; set; }

        [Required]
        public Guid TargetMemoryId { get; set; }

        [StringLength(260)]
        public string Reason { get; set; } = string.Empty;
    }

    private sealed class TicketBoardToolInput
    {
        public string? CompletedSearch { get; set; }
        [Range(1, 100)]
        public int CompletedPage { get; set; } = 1;
    }

    private sealed class WingListToolInput
    {
        public string? Query { get; set; }
    }

    private sealed class FocusGuidInput
    {
        [Required]
        public Guid Id { get; set; }
    }

    private sealed class MemoryArchiveToolInput
    {
        [Required]
        public Guid Id { get; set; }
        [StringLength(260)]
        public string Reason { get; set; } = string.Empty;
    }

    private sealed class MemorySupersedeToolInput
    {
        [Required]
        public Guid Id { get; set; }
        [Required]
        public Guid ReplacementMemoryId { get; set; }
        [StringLength(260)]
        public string Reason { get; set; } = string.Empty;
    }

    private sealed class MemoryTagsToolInput
    {
        [Required]
        public Guid Id { get; set; }
        public string TagsText { get; set; } = string.Empty;
    }

    private sealed class RoomListToolInput : IValidatableObject
    {
        public Guid? WingId { get; set; }
        public string? WingSlug { get; set; }
        public string? WingName { get; set; }
        public string? Query { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            var providedWingFilters = new[] { WingId.HasValue, !string.IsNullOrWhiteSpace(WingSlug), !string.IsNullOrWhiteSpace(WingName) }
                .Count(x => x);
            if (providedWingFilters > 1)
            {
                yield return new ValidationResult("Provide at most one wing filter: wingId, wingSlug, or wingName.", [nameof(WingId), nameof(WingSlug), nameof(WingName)]);
            }
        }
    }

    private sealed class AgentListToolInput
    {
        public string? Query { get; set; }
        public ContextPackGoal? Goal { get; set; }
        public bool SupportsWriteActions { get; set; }
    }

    private sealed class AgentGetToolInput
    {
        [Required]
        [RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
        public string Slug { get; set; } = string.Empty;
    }

    private sealed class AgentRecommendToolInput
    {
        [StringLength(400)]
        public string Question { get; set; } = string.Empty;
        public ContextPackGoal Goal { get; set; } = ContextPackGoal.General;
        [Range(1, 4)]
        public int Limit { get; set; } = 4;
    }

    private sealed class AgentRunToolInput
    {
        [Required]
        [RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
        public string Slug { get; set; } = string.Empty;

        [Required]
        [StringLength(400)]
        public string Question { get; set; } = string.Empty;

        public bool IncludeCompletedWork { get; set; } = true;
        public bool ExpandHistory { get; set; } = true;

        [Range(3, 10)]
        public int ResultsPerSection { get; set; } = 4;
    }

    private sealed class SkillListToolInput
    {
        public string? Query { get; set; }
        public SkillCategory? Category { get; set; }
        public Guid? WingId { get; set; }
        public bool PinnedOnly { get; set; }
        public bool NeedsReviewOnly { get; set; }
    }

    private sealed class SkillGetToolInput
    {
        [Required]
        [RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
        public string Slug { get; set; } = string.Empty;
    }

    private sealed class SkillRecommendToolInput
    {
        [Required]
        [StringLength(400)]
        public string Question { get; set; } = string.Empty;
        public Guid? WingId { get; set; }
        public SkillCategory? Category { get; set; }
        [Range(1, 12)]
        public int Limit { get; set; } = 5;
    }

    private class SkillCreateToolInput
    {
        [Required]
        [StringLength(160)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        public string Summary { get; set; } = string.Empty;

        [Required]
        public SkillCategory Category { get; set; } = SkillCategory.Task;

        [Required]
        public string WhenToUse { get; set; } = string.Empty;

        [Required]
        public string Flow { get; set; } = string.Empty;

        public string ExamplesText { get; set; } = string.Empty;

        [StringLength(500)]
        public string TriggerHintsText { get; set; } = string.Empty;

        public Guid? WingId { get; set; }

        public bool IsPinned { get; set; } = true;

        public SkillEditorInput ToEditorInput() => new()
        {
            Name = Name,
            Summary = Summary,
            Category = Category,
            WhenToUse = WhenToUse,
            Flow = Flow,
            ExamplesText = ExamplesText,
            TriggerHintsText = TriggerHintsText,
            WingId = WingId,
            IsPinned = IsPinned
        };
    }

    private sealed class SkillUpdateToolInput : SkillCreateToolInput
    {
        [Required]
        public Guid Id { get; set; }

        public new SkillEditorInput ToEditorInput()
        {
            var input = base.ToEditorInput();
            input.Id = Id;
            return input;
        }
    }

    private sealed class TicketStatusToolInput
    {
        [Required]
        public Guid Id { get; set; }
        [Required]
        public TicketStatus Status { get; set; }
    }

    private sealed class TicketNoteToolInput
    {
        [Required]
        public Guid TicketId { get; set; }
        [Required]
        [StringLength(120)]
        public string Author { get; set; } = "Copilot";
        [Required]
        public string Content { get; set; } = string.Empty;
    }

    private sealed class TodoStatusToolInput
    {
        [Required]
        public Guid Id { get; set; }
        [Required]
        public TodoStatus Status { get; set; }
    }

    private sealed class CodeGraphProjectToolInput
    {
        [Required]
        public Guid ProjectId { get; set; }
        public string? Query { get; set; }
        public Guid? SelectedNodeId { get; set; }
        public Guid? SelectedFileId { get; set; }
    }

    private sealed class CodeGraphRescanToolInput
    {
        [Required]
        public Guid ProjectId { get; set; }
    }
}
