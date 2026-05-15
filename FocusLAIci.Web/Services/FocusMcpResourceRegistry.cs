using FocusLAIci.Web.Models;

namespace FocusLAIci.Web.Services;

public sealed class FocusMcpResourceRegistry(
    IServiceScopeFactory scopeFactory,
    FocusMcpSessionService sessionService,
    FocusMcpEventBus eventBus,
    FocusDatabaseTargetService databaseTargetService,
    FocusAgentCatalogService agentCatalogService,
    FocusDiagnosticsService diagnosticsService)
{
    private readonly IReadOnlyCollection<FocusMcpResourceDescriptor> _resources =
    [
        new() { Uri = "focus://system/health", Description = "Service health and MCP auth details.", SupportsSubscription = false },
        new() { Uri = "focus://system/self-test", Description = "Deep MCP readiness and operator self-test results.", SupportsSubscription = false },
        new() { Uri = "focus://system/metrics", Description = "Current Focus counts and platform metrics.", SupportsSubscription = true },
        new() { Uri = "focus://system/events", Description = "Recent Focus MCP resource updates.", SupportsSubscription = true },
        new() { Uri = "focus://workspace", Description = "Workspace export used to prime a new session.", SupportsSubscription = true },
        new() { Uri = "focus://workspace/bootstrap", Description = "Cold-start workspace guidance with a recommended Focus-first flow.", SupportsSubscription = true },
        new() { Uri = "focus://workspace/bootstrap/{profile}", Description = "Cold-start workspace guidance using a bootstrap profile such as developer, operator, or incident-response.", SupportsSubscription = true },
        new() { Uri = "focus://wings", Description = "Directory of Focus wings for discovery and routing.", SupportsSubscription = true },
        new() { Uri = "focus://wings/{slug}", Description = "A single wing lookup resolved by slug.", SupportsSubscription = true },
        new() { Uri = "focus://rooms", Description = "Directory of Focus rooms across wings.", SupportsSubscription = true },
        new() { Uri = "focus://rooms/{wingSlug}", Description = "Directory of rooms for a single wing slug.", SupportsSubscription = true },
        new() { Uri = "focus://skills", Description = "Directory of Focus skills and runbook-style workflows.", SupportsSubscription = true },
        new() { Uri = "focus://skills/{slug}", Description = "A single Focus skill resolved by slug.", SupportsSubscription = true },
        new() { Uri = "focus://agents", Description = "Directory of Focus scoped agents for context, research, execution, and review.", SupportsSubscription = false },
        new() { Uri = "focus://agents/{slug}", Description = "A single Focus agent resolved by slug.", SupportsSubscription = false },
        new() { Uri = "focus://memories/governance", Description = "Memory governance queue for review, archive, restore, and supersession work.", SupportsSubscription = true },
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
               || resourceUri.StartsWith("focus://workspace/bootstrap/", StringComparison.OrdinalIgnoreCase)
               || resourceUri.StartsWith("focus://wings/", StringComparison.OrdinalIgnoreCase)
               || resourceUri.StartsWith("focus://rooms/", StringComparison.OrdinalIgnoreCase)
               || resourceUri.StartsWith("focus://agents/", StringComparison.OrdinalIgnoreCase)
               || resourceUri.StartsWith("focus://skills/", StringComparison.OrdinalIgnoreCase)
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
            "focus://system/self-test" => new FocusMcpResourceContent
            {
                Uri = resourceUri,
                Data = await diagnosticsService.RunMcpSelfTestAsync(authMode, cancellationToken)
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
            "focus://workspace/bootstrap" => new FocusMcpResourceContent
            {
                Uri = resourceUri,
                Data = await palaceService.GetWorkspaceBootstrapAsync("developer", cancellationToken)
            },
            "focus://wings" => new FocusMcpResourceContent
            {
                Uri = resourceUri,
                Data = new { wings = await palaceService.GetWingSummariesAsync(null, cancellationToken) }
            },
            "focus://rooms" => new FocusMcpResourceContent
            {
                Uri = resourceUri,
                Data = new { rooms = await palaceService.GetRoomsAsync(null, null, null, null, cancellationToken) }
            },
            "focus://skills" => new FocusMcpResourceContent
            {
                Uri = resourceUri,
                Data = new { skills = await palaceService.GetSkillSummariesAsync(null, null, cancellationToken) }
            },
            "focus://agents" => new FocusMcpResourceContent
            {
                Uri = resourceUri,
                Data = new { agents = agentCatalogService.GetCatalog() }
            },
            "focus://memories/governance" => new FocusMcpResourceContent
            {
                Uri = resourceUri,
                Data = await palaceService.GetMemoryGovernanceQueueAsync(cancellationToken)
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

        if (resourceUri.StartsWith("focus://workspace/bootstrap/", StringComparison.OrdinalIgnoreCase))
        {
            var profile = resourceUri["focus://workspace/bootstrap/".Length..];
            return new FocusMcpResourceContent
            {
                Uri = resourceUri,
                Data = await palaceService.GetWorkspaceBootstrapAsync(profile, cancellationToken)
            };
        }

        if (resourceUri.StartsWith("focus://wings/", StringComparison.OrdinalIgnoreCase))
        {
            var slug = resourceUri["focus://wings/".Length..];
            var wing = (await palaceService.GetWingSummariesAsync(slug, cancellationToken))
                .FirstOrDefault(x => string.Equals(x.Slug, slug, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("That wing resource no longer exists.");
            return new FocusMcpResourceContent { Uri = resourceUri, Data = wing };
        }

        if (resourceUri.StartsWith("focus://rooms/", StringComparison.OrdinalIgnoreCase))
        {
            var wingSlug = resourceUri["focus://rooms/".Length..];
            return new FocusMcpResourceContent
            {
                Uri = resourceUri,
                Data = new { rooms = await palaceService.GetRoomsAsync(null, wingSlug, null, null, cancellationToken) }
            };
        }

        if (resourceUri.StartsWith("focus://skills/", StringComparison.OrdinalIgnoreCase))
        {
            var slug = resourceUri["focus://skills/".Length..];
            var skill = await palaceService.GetSkillAsync(slug, cancellationToken)
                ?? throw new InvalidOperationException("That skill resource no longer exists.");
            return new FocusMcpResourceContent { Uri = resourceUri, Data = skill };
        }

        if (resourceUri.StartsWith("focus://agents/", StringComparison.OrdinalIgnoreCase))
        {
            var slug = resourceUri["focus://agents/".Length..];
            var agent = agentCatalogService.GetAgent(slug)
                ?? throw new InvalidOperationException("That agent resource no longer exists.");
            return new FocusMcpResourceContent { Uri = resourceUri, Data = agent };
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
