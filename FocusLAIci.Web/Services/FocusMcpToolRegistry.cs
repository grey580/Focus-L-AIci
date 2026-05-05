using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FocusLAIci.Web.Models;

namespace FocusLAIci.Web.Services;

public sealed class FocusMcpToolRegistry(IServiceScopeFactory scopeFactory, ILogger<FocusMcpToolRegistry> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IReadOnlyCollection<FocusMcpToolDescriptor> _tools =
    [
        CreateDescriptor(
            "focus.memory.search",
            "Search Focus memories using the existing palace search logic.",
            "memories",
            false,
            new { type = "object", properties = new { query = new { type = "string" }, tag = new { type = "string" }, updatedSinceUtc = new { type = "string", format = "date-time" }, limit = new { type = "integer", minimum = 1, maximum = 50 } } },
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
            new { type = "object", required = new[] { "title", "summary", "content" } },
            new { type = "object", properties = new { id = new { type = "string", format = "uuid" } } }),
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
            new { type = "object", required = new[] { "question" } },
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

        return toolName switch
        {
            "focus.memory.search" => await SearchMemoriesAsync(palaceService, arguments, cancellationToken),
            "focus.memory.get" => await GetMemoryAsync(palaceService, arguments, cancellationToken),
            "focus.memory.save" => await SaveMemoryAsync(palaceService, arguments, cancellationToken),
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
        var results = await palaceService.SearchMemoriesAsync(input.Query, null, null, null, input.Tag, input.UpdatedSinceUtc, cancellationToken);
        return new
        {
            results = results.Take(input.Limit).ToArray()
        };
    }

    private static async Task<object> GetMemoryAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<FocusGuidInput>(arguments);
        Validate(input);
        var memory = await palaceService.GetMemoryAsync(input.Id, cancellationToken) ?? throw new InvalidOperationException("That memory no longer exists.");
        return new { memory };
    }

    private static async Task<object> SaveMemoryAsync(PalaceService palaceService, JsonElement arguments, CancellationToken cancellationToken)
    {
        var input = Deserialize<MemoryEditorInput>(arguments);
        Validate(input);
        var id = await palaceService.SaveMemoryAsync(input, cancellationToken);
        return new { id };
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

        return JsonSerializer.Deserialize<T>(arguments.GetRawText(), SerializerOptions)
            ?? throw new InvalidOperationException("Unable to parse MCP tool arguments.");
    }

    private static void Validate(object instance)
    {
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(instance, new ValidationContext(instance), results, true);
        if (isValid)
        {
            return;
        }

        throw new InvalidOperationException(results.First().ErrorMessage ?? "The MCP tool input is invalid.");
    }

    private sealed class SearchMemoriesToolInput
    {
        public string? Query { get; set; }
        public string? Tag { get; set; }
        public DateTime? UpdatedSinceUtc { get; set; }
        [Range(1, 50)]
        public int Limit { get; set; } = 10;
    }

    private sealed class TicketBoardToolInput
    {
        public string? CompletedSearch { get; set; }
        [Range(1, 100)]
        public int CompletedPage { get; set; } = 1;
    }

    private sealed class FocusGuidInput
    {
        [Required]
        public Guid Id { get; set; }
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
