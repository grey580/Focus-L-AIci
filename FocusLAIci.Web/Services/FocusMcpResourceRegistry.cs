using FocusLAIci.Web.Models;

namespace FocusLAIci.Web.Services;

public sealed class FocusMcpResourceRegistry(
    IServiceScopeFactory scopeFactory,
    FocusMcpSessionService sessionService,
    FocusMcpEventBus eventBus,
    FocusDatabaseTargetService databaseTargetService)
{
    private readonly IReadOnlyCollection<FocusMcpResourceDescriptor> _resources =
    [
        new() { Uri = "focus://system/health", Description = "Service health and MCP auth details.", SupportsSubscription = false },
        new() { Uri = "focus://system/metrics", Description = "Current Focus counts and platform metrics.", SupportsSubscription = true },
        new() { Uri = "focus://system/events", Description = "Recent Focus MCP resource updates.", SupportsSubscription = true },
        new() { Uri = "focus://workspace", Description = "Workspace export used to prime a new session.", SupportsSubscription = true },
        new() { Uri = "focus://recent-changes", Description = "Recent Focus changes across memories, todos, tickets, and code graph.", SupportsSubscription = true },
        new() { Uri = "focus://tickets/board", Description = "Open and completed ticket board state.", SupportsSubscription = true },
        new() { Uri = "focus://todos/board", Description = "Todo board grouped by status.", SupportsSubscription = true },
        new() { Uri = "focus://code-graph/projects", Description = "Saved code graph projects.", SupportsSubscription = true },
        new() { Uri = "focus://memories/{id}", Description = "A single memory detail document.", SupportsSubscription = true },
        new() { Uri = "focus://todos/{id}", Description = "A single todo detail document.", SupportsSubscription = true },
        new() { Uri = "focus://tickets/{id}", Description = "A single ticket detail document.", SupportsSubscription = true }
    ];

    public IReadOnlyCollection<FocusMcpResourceDescriptor> GetResources() => _resources;

    public IReadOnlyCollection<string> Complete(string prefix)
    {
        return _resources
            .Select(x => x.Uri)
            .Where(x => x.StartsWith(prefix ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x)
            .ToArray();
    }

    public bool IsKnownResource(string resourceUri)
    {
        return resourceUri.StartsWith("focus://memories/", StringComparison.OrdinalIgnoreCase)
               || resourceUri.StartsWith("focus://todos/", StringComparison.OrdinalIgnoreCase)
               || resourceUri.StartsWith("focus://tickets/", StringComparison.OrdinalIgnoreCase)
               || _resources.Any(x => string.Equals(x.Uri, resourceUri, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<FocusMcpResourceContent> GetResourceAsync(string resourceUri, string authMode, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var palaceService = scope.ServiceProvider.GetRequiredService<PalaceService>();
        var ticketingService = scope.ServiceProvider.GetRequiredService<TicketingService>();
        var codeGraphService = scope.ServiceProvider.GetRequiredService<CodeGraphService>();

        var knownResource = resourceUri switch
        {
            "focus://system/health" => new FocusMcpResourceContent
            {
                Uri = resourceUri,
                Data = new
                {
                    status = "ok",
                    serverTimeUtc = DateTime.UtcNow,
                    authMode,
                    activeSessions = sessionService.GetSessions().Count,
                    database = databaseTargetService.GetCurrentTarget()
                }
            },
            "focus://system/metrics" => new FocusMcpResourceContent
            {
                Uri = resourceUri,
                Data = await palaceService.GetApiSummaryAsync(cancellationToken)
            },
            "focus://system/events" => new FocusMcpResourceContent
            {
                Uri = resourceUri,
                Data = new { events = eventBus.GetRecentEvents(50) }
            },
            "focus://workspace" => new FocusMcpResourceContent
            {
                Uri = resourceUri,
                Data = await palaceService.GetWorkspaceExportAsync(cancellationToken)
            },
            "focus://recent-changes" => new FocusMcpResourceContent
            {
                Uri = resourceUri,
                Data = new { changes = await palaceService.GetRecentChangesAsync(25, cancellationToken) }
            },
            "focus://tickets/board" => new FocusMcpResourceContent
            {
                Uri = resourceUri,
                Data = await ticketingService.GetBoardAsync(null, 1, cancellationToken)
            },
            "focus://todos/board" => new FocusMcpResourceContent
            {
                Uri = resourceUri,
                Data = await palaceService.GetTodoBoardAsync(cancellationToken)
            },
            "focus://code-graph/projects" => new FocusMcpResourceContent
            {
                Uri = resourceUri,
                Data = await codeGraphService.GetBoardAsync(cancellationToken)
            },
            _ => null
        };

        if (knownResource is not null)
        {
            return knownResource;
        }

        if (resourceUri.StartsWith("focus://memories/", StringComparison.OrdinalIgnoreCase))
        {
            var id = ParseIdFromUri(resourceUri);
            var memory = await palaceService.GetMemoryAsync(id, cancellationToken) ?? throw new InvalidOperationException("That memory resource no longer exists.");
            return new FocusMcpResourceContent { Uri = resourceUri, Data = memory };
        }

        if (resourceUri.StartsWith("focus://tickets/", StringComparison.OrdinalIgnoreCase))
        {
            var id = ParseIdFromUri(resourceUri);
            var ticket = await ticketingService.GetDetailsAsync(id, cancellationToken);
            return new FocusMcpResourceContent { Uri = resourceUri, Data = ticket };
        }

        if (resourceUri.StartsWith("focus://todos/", StringComparison.OrdinalIgnoreCase))
        {
            var id = ParseIdFromUri(resourceUri);
            var todo = await palaceService.GetTodoDetailsAsync(id, cancellationToken);
            return new FocusMcpResourceContent { Uri = resourceUri, Data = todo };
        }

        throw new InvalidOperationException($"Unknown Focus MCP resource '{resourceUri}'.");
    }

    private static Guid ParseIdFromUri(string resourceUri)
    {
        var idText = resourceUri[(resourceUri.LastIndexOf('/') + 1)..];
        return Guid.TryParse(idText, out var id)
            ? id
            : throw new InvalidOperationException("The MCP resource uri does not contain a valid guid.");
    }
}
