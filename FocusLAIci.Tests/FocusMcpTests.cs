using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using FocusLAIci.Web.Models;
using FocusLAIci.Web.Data;
using FocusLAIci.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace FocusLAIci.Tests;

public sealed class FocusMcpTests
{
    [Fact]
    public void SessionService_SupportsSubscriptionsAndWildcardMatching()
    {
        var service = new FocusMcpSessionService();
        var session = service.CreateSession(
            new FocusMcpInitializeInput
            {
                ClientName = "Test Client",
                ClientVersion = "1.0"
            },
            "127.0.0.1",
            "loopback");

        var subscriptions = service.Subscribe(session.SessionId, ["focus://tickets/*", "focus://system/events"]);

        Assert.Contains("focus://tickets/*", subscriptions);
        Assert.True(service.IsSubscribed(session.SessionId, ["focus://tickets/board"]));
        Assert.True(service.IsSubscribed(session.SessionId, ["focus://system/events"]));

        var remaining = service.Unsubscribe(session.SessionId, ["focus://system/events"]);

        Assert.DoesNotContain("focus://system/events", remaining);
        Assert.False(service.IsSubscribed(session.SessionId, ["focus://system/events"]));
    }

    [Fact]
    public void SessionService_RestoresMissingSessionIdsOnTouch()
    {
        var service = new FocusMcpSessionService();

        var touched = service.TryTouch("restored-session", out var session);

        Assert.True(touched);
        Assert.Equal("restored-session", session.SessionId);
        Assert.Equal("recovered", session.AuthMode);
        Assert.True(service.Exists("restored-session"));
    }

    [Fact]
    public async Task EventBus_PublishesToSubscribersAndKeepsNewestEventsFirst()
    {
        var bus = new FocusMcpEventBus(NullLogger<FocusMcpEventBus>.Instance);
        FocusMcpPublishedEvent? observed = null;

        using var subscription = bus.Subscribe(eventItem => observed = eventItem);

        var first = new FocusMcpPublishedEvent
        {
            EventType = "ticket.updated",
            Title = "First",
            Description = "first event",
            ResourceUris = ["focus://tickets/board"]
        };

        var second = new FocusMcpPublishedEvent
        {
            EventType = "todo.created",
            Title = "Second",
            Description = "second event",
            ResourceUris = ["focus://todos/board"]
        };

        await bus.PublishAsync(first);
        await bus.PublishAsync(second);

        var recent = bus.GetRecentEvents(2).ToArray();

        Assert.NotNull(observed);
        Assert.Equal("todo.created", observed!.EventType);
        Assert.Equal(["todo.created", "ticket.updated"], recent.Select(x => x.EventType).ToArray());
    }

    [Fact]
    public void AuthService_AllowsLoopbackWithoutApiKey()
    {
        var configuration = new ConfigurationBuilder().Build();
        var service = new FocusMcpAuthService(configuration);
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Loopback;

        var allowed = service.TryAuthorize(httpContext, out var auth);

        Assert.True(allowed);
        Assert.Equal("loopback", auth.AuthMode);
        Assert.Equal(string.Empty, auth.ErrorMessage);
        Assert.True(auth.CanWrite);
    }

    [Fact]
    public void AuthService_RequiresConfiguredApiKeyForNonLoopbackClients()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FocusPalace:Mcp:ApiKeys:0"] = "secret-key"
            })
            .Build();

        var service = new FocusMcpAuthService(configuration);
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.10.10.20");
        httpContext.Request.Headers["X-Focus-Mcp-Key"] = "secret-key";

        var allowed = service.TryAuthorize(httpContext, out var auth);

        Assert.True(allowed);
        Assert.Equal("api-key", auth.AuthMode);
        Assert.Equal(string.Empty, auth.ErrorMessage);
        Assert.True(auth.CanWrite);
    }

    [Fact]
    public void AuthService_SupportsReadOnlyLabeledKeys()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FocusPalace:Mcp:ApiKeyDefinitions:0:Value"] = "readonly-key",
                ["FocusPalace:Mcp:ApiKeyDefinitions:0:Label"] = "agent-audit",
                ["FocusPalace:Mcp:ApiKeyDefinitions:0:CanWrite"] = "false"
            })
            .Build();

        var service = new FocusMcpAuthService(configuration);
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.10.10.20");
        httpContext.Request.Headers["Authorization"] = "Bearer readonly-key";

        var allowed = service.TryAuthorize(httpContext, out var auth);

        Assert.True(allowed);
        Assert.Equal("api-key-readonly", auth.AuthMode);
        Assert.Equal("agent-audit", auth.Label);
        Assert.False(auth.CanWrite);
    }

    [Fact]
    public async Task ToolRegistry_MemoryMutationToolsAcceptStringEnumsAndExposeGovernanceFlow()
    {
        await using var harness = await McpHarness.CreateAsync();
        await using var scope = harness.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FocusMemoryContext>();
        var registry = scope.ServiceProvider.GetRequiredService<FocusMcpToolRegistry>();
        var palaceService = scope.ServiceProvider.GetRequiredService<PalaceService>();

        var wing = new Wing
        {
            Name = "Microsoft",
            Slug = "microsoft",
            Description = "Microsoft work"
        };
        var room = new Room
        {
            Wing = wing,
            Name = "Identity",
            Slug = "identity",
            Description = "Identity notes"
        };
        dbContext.Wings.Add(wing);
        dbContext.Rooms.Add(room);
        await dbContext.SaveChangesAsync();

        var saveResult = await registry.InvokeAsync(
            "focus.memory.save",
            JsonDocument.Parse(
                $$"""
                {
                  "title": "Microsoft Entra flow",
                  "summary": "Track delegated Graph setup",
                  "content": "Delegated Graph scopes power the SOC mailbox workflow.",
                  "kind": "Decision",
                  "sourceKind": "ManualNote",
                  "wingId": "{{wing.Id}}",
                  "roomId": "{{room.Id}}",
                  "tagsText": "microsoft, entra"
                }
                """).RootElement,
            CancellationToken.None);

        var memoryId = JsonSerializer.SerializeToElement(saveResult).GetProperty("id").GetGuid();

        await registry.InvokeAsync("focus.memory.verify", JsonDocument.Parse($$"""{"id":"{{memoryId}}"}""").RootElement, CancellationToken.None);
        var tagsResult = await registry.InvokeAsync(
            "focus.memory.update-tags",
            JsonDocument.Parse($$"""{"id":"{{memoryId}}","tagsText":"microsoft, graph"}""").RootElement,
            CancellationToken.None);
        await registry.InvokeAsync(
            "focus.memory.archive",
            JsonDocument.Parse($$"""{"id":"{{memoryId}}","reason":"Replaced by newer setup"}""").RootElement,
            CancellationToken.None);
        await registry.InvokeAsync("focus.memory.restore", JsonDocument.Parse($$"""{"id":"{{memoryId}}"}""").RootElement, CancellationToken.None);

        var memory = await palaceService.GetMemoryAsync(memoryId, CancellationToken.None);
        var updatedTags = JsonSerializer.SerializeToElement(tagsResult).GetProperty("tags").EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray();

        Assert.NotNull(memory);
        Assert.Equal("Verified", memory!.Memory.VerificationStatusLabel);
        Assert.Equal(MemoryLifecycleState.Active, memory.Memory.LifecycleState);
        Assert.Equal(["graph", "microsoft"], updatedTags);
        Assert.Equal(["graph", "microsoft"], memory.Memory.Tags.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task DiscoveryToolsAndBootstrapResourceExposeWorkspaceRoutingContext()
    {
        await using var harness = await McpHarness.CreateAsync();
        await using var scope = harness.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FocusMemoryContext>();
        var registry = scope.ServiceProvider.GetRequiredService<FocusMcpToolRegistry>();
        var resources = scope.ServiceProvider.GetRequiredService<FocusMcpResourceRegistry>();
        var palaceService = scope.ServiceProvider.GetRequiredService<PalaceService>();

        var wing = new Wing
        {
            Name = "Grey Canary",
            Slug = "grey-canary",
            Description = "Grey Canary implementation notes"
        };
        var room = new Room
        {
            Wing = wing,
            Name = "Platform",
            Slug = "platform",
            Description = "Platform work"
        };
        dbContext.Wings.Add(wing);
        dbContext.Rooms.Add(room);
        await dbContext.SaveChangesAsync();

        await palaceService.SaveMemoryAsync(
            new MemoryEditorInput
            {
                Title = "Platform bootstrap",
                Summary = "Search and context inspect are the best cold start path.",
                Content = "Search memories, then inspect recent changes before coding.",
                Kind = MemoryKind.Decision,
                SourceKind = SourceKind.ManualNote,
                WingId = wing.Id,
                RoomId = room.Id,
                IsPinned = true
            },
            CancellationToken.None);

        var wingResult = await registry.InvokeAsync("focus.wing.list", JsonDocument.Parse("""{"query":"grey"}""").RootElement, CancellationToken.None);
        var roomResult = await registry.InvokeAsync("focus.room.list", JsonDocument.Parse("""{"wingSlug":"grey-canary"}""").RootElement, CancellationToken.None);
        var bootstrapResource = await resources.GetResourceAsync("focus://workspace/bootstrap", "loopback", CancellationToken.None);
        var wingsResource = await resources.GetResourceAsync("focus://wings", "loopback", CancellationToken.None);

        var wingNames = JsonSerializer.SerializeToElement(wingResult).GetProperty("wings").EnumerateArray().Select(x => x.GetProperty("Name").GetString() ?? string.Empty).ToArray();
        var roomNames = JsonSerializer.SerializeToElement(roomResult).GetProperty("rooms").EnumerateArray().Select(x => x.GetProperty("RoomName").GetString() ?? string.Empty).ToArray();
        var bootstrapData = JsonSerializer.SerializeToElement(bootstrapResource.Data);
        var resourceWingNames = JsonSerializer.SerializeToElement(wingsResource.Data).GetProperty("wings").EnumerateArray().Select(x => x.GetProperty("Name").GetString() ?? string.Empty).ToArray();

        Assert.Equal(["Grey Canary"], wingNames);
        Assert.Equal(["Platform"], roomNames);
        Assert.Equal("Start with Focus. Search memories, build a context pack, review recent changes or tickets if relevant, then work.", bootstrapData.GetProperty("SuggestedTaskPrompt").GetString());
        Assert.Contains("Grey Canary", bootstrapData.GetProperty("BootstrapSummary").GetString());
        Assert.Contains("Grey Canary", resourceWingNames);
    }

    [Fact]
    public async Task AdvancedMemoryToolsSupportDryRunDuplicatesCanonicalMergeAndParameterizedResources()
    {
        await using var harness = await McpHarness.CreateAsync();
        await using var scope = harness.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FocusMemoryContext>();
        var registry = scope.ServiceProvider.GetRequiredService<FocusMcpToolRegistry>();
        var resources = scope.ServiceProvider.GetRequiredService<FocusMcpResourceRegistry>();
        var contextService = scope.ServiceProvider.GetRequiredService<ContextService>();
        var palaceService = scope.ServiceProvider.GetRequiredService<PalaceService>();

        var wing = new Wing { Name = "Ops", Slug = "ops", Description = "Ops wing" };
        var room = new Room { Wing = wing, Name = "Incidents", Slug = "incidents", Description = "Incident room" };
        dbContext.Wings.Add(wing);
        dbContext.Rooms.Add(room);
        await dbContext.SaveChangesAsync();

        var savedA = JsonSerializer.SerializeToElement(await registry.InvokeAsync(
            "focus.memory.save",
            JsonDocument.Parse($$"""
            {
              "title": "API outage",
              "summary": "Primary incident memory",
              "content": "The API outage was caused by a token rollover issue.",
              "kind": "Incident",
              "sourceKind": "DebugSession",
              "wingId": "{{wing.Id}}",
              "roomId": "{{room.Id}}",
              "tagsText": "incident, api"
            }
            """).RootElement,
            CancellationToken.None)).GetProperty("id").GetGuid();

        var dryRun = JsonSerializer.SerializeToElement(await registry.InvokeAsync(
            "focus.memory.save",
            JsonDocument.Parse($$"""
            {
              "title": "API outage duplicate",
              "summary": "Primary incident memory",
              "content": "The API outage was caused by a token rollover issue.",
              "kind": "Incident",
              "sourceKind": "DebugSession",
              "wingId": "{{wing.Id}}",
              "roomId": "{{room.Id}}",
              "dryRun": true,
              "requireConfirmationOnDuplicate": true
            }
            """).RootElement,
            CancellationToken.None));

        var savedB = JsonSerializer.SerializeToElement(await registry.InvokeAsync(
            "focus.memory.save",
            JsonDocument.Parse($$"""
            {
              "title": "API outage canonical",
              "summary": "Canonical incident memory",
              "content": "Canonical remediation was to rotate the token signer and recycle the app.",
              "kind": "Incident",
              "sourceKind": "DebugSession",
              "wingId": "{{wing.Id}}",
              "roomId": "{{room.Id}}",
              "tagsText": "incident, canonical"
            }
            """).RootElement,
            CancellationToken.None)).GetProperty("id").GetGuid();

        var mergeResult = JsonSerializer.SerializeToElement(await registry.InvokeAsync(
            "focus.memory.merge",
            JsonDocument.Parse($$"""{"sourceMemoryId":"{{savedA}}","targetMemoryId":"{{savedB}}","reason":"Canonicalized duplicate incident memory."}""").RootElement,
            CancellationToken.None));
        var canonicalResult = JsonSerializer.SerializeToElement(await registry.InvokeAsync(
            "focus.memory.resolve-canonical",
            JsonDocument.Parse($$"""{"id":"{{savedA}}"}""").RootElement,
            CancellationToken.None));
        var governanceResult = JsonSerializer.SerializeToElement(await registry.InvokeAsync(
            "focus.memory.governance-queue",
            JsonDocument.Parse("""{}""").RootElement,
            CancellationToken.None));
        var skillId = await palaceService.SaveSkillAsync(new SkillEditorInput
        {
            Name = "Run MCP usability pass",
            Summary = "Exercise bootstrap, search, context, and governance flows.",
            Category = SkillCategory.Task,
            WhenToUse = "Use this after MCP changes.",
            Flow = "Initialize MCP.\nRun bootstrap.\nInspect a realistic context question.",
            ExamplesText = "Run an MCP usability pass for Focus.",
            TriggerHintsText = "mcp, usability",
            WingId = wing.Id,
            IsPinned = true
        }, CancellationToken.None);
        var skillListResult = JsonSerializer.SerializeToElement(await registry.InvokeAsync(
            "focus.skill.list",
            JsonDocument.Parse("""{"query":"usability","category":"Task"}""").RootElement,
            CancellationToken.None));
        var agentListResult = JsonSerializer.SerializeToElement(await registry.InvokeAsync(
            "focus.agent.list",
            JsonDocument.Parse("""{"query":"review"}""").RootElement,
            CancellationToken.None));
        var agentGetResult = JsonSerializer.SerializeToElement(await registry.InvokeAsync(
            "focus.agent.get",
            JsonDocument.Parse("""{"slug":"review-agent"}""").RootElement,
            CancellationToken.None));
        var agentRecommendResult = JsonSerializer.SerializeToElement(await registry.InvokeAsync(
            "focus.agent.recommend",
            JsonDocument.Parse("""{"question":"review a risky change before shipping it","goal":"Delivery","limit":2}""").RootElement,
            CancellationToken.None));
        var skillGetResult = JsonSerializer.SerializeToElement(await registry.InvokeAsync(
            "focus.skill.get",
            JsonDocument.Parse("""{"slug":"run-mcp-usability-pass"}""").RootElement,
            CancellationToken.None));
        var skillRecommendResult = JsonSerializer.SerializeToElement(await registry.InvokeAsync(
            "focus.skill.recommend",
            JsonDocument.Parse($$"""{"question":"run an mcp usability regression","wingId":"{{wing.Id}}","limit":3}""").RootElement,
            CancellationToken.None));
        var searchResult = JsonSerializer.SerializeToElement(await registry.InvokeAsync(
            "focus.memory.search",
            JsonDocument.Parse($$"""{"query":"outage","wingId":"{{wing.Id}}","kind":"Incident","includeRetired":true,"lifecycleState":"Superseded"}""").RootElement,
            CancellationToken.None));
        var contextPack = await contextService.BuildContextPackAsync(new ContextBriefInput
        {
            Question = "api outage",
            WingId = wing.Id,
            RoomId = room.Id,
            Kind = MemoryKind.Incident,
            Tag = "incident",
            PreferRecentChanges = true
        }, CancellationToken.None);
        var operatorBootstrap = JsonSerializer.SerializeToElement((await resources.GetResourceAsync("focus://workspace/bootstrap/operator", "loopback", CancellationToken.None)).Data);
        var roomDirectory = JsonSerializer.SerializeToElement((await resources.GetResourceAsync("focus://rooms/ops", "loopback", CancellationToken.None)).Data);
        var skillDirectory = JsonSerializer.SerializeToElement((await resources.GetResourceAsync("focus://skills", "loopback", CancellationToken.None)).Data);
        var skillResource = JsonSerializer.SerializeToElement((await resources.GetResourceAsync("focus://skills/run-mcp-usability-pass", "loopback", CancellationToken.None)).Data);
        var agentDirectory = JsonSerializer.SerializeToElement((await resources.GetResourceAsync("focus://agents", "loopback", CancellationToken.None)).Data);
        var agentResource = JsonSerializer.SerializeToElement((await resources.GetResourceAsync("focus://agents/context-agent", "loopback", CancellationToken.None)).Data);

        Assert.False(dryRun.GetProperty("saved").GetBoolean());
        Assert.True(dryRun.GetProperty("dryRun").GetBoolean());
        Assert.NotEmpty(dryRun.GetProperty("duplicateSuggestions").EnumerateArray());
        Assert.Equal(savedB, mergeResult.GetProperty("targetMemoryId").GetGuid());
        Assert.Equal(savedB, canonicalResult.GetProperty("canonical").GetProperty("Id").GetGuid());
        Assert.Equal(2, canonicalResult.GetProperty("trail").GetArrayLength());
        Assert.True(governanceResult.GetProperty("queue").GetProperty("UnverifiedActiveCount").GetInt32() >= 1);
        Assert.Equal(governanceResult.GetProperty("queue").GetProperty("Items").GetArrayLength(), governanceResult.GetProperty("summary").GetProperty("QueueItemCount").GetInt32());
        Assert.Equal(governanceResult.GetProperty("queue").GetProperty("NeedsReviewCount").GetInt32(), governanceResult.GetProperty("summary").GetProperty("NeedsReviewCount").GetInt32());
        Assert.Contains(agentListResult.GetProperty("agents").EnumerateArray(), x => x.GetProperty("Slug").GetString() == "review-agent");
        Assert.Equal("Review Agent", agentGetResult.GetProperty("agent").GetProperty("Agent").GetProperty("Name").GetString());
        Assert.Contains(agentRecommendResult.GetProperty("agents").EnumerateArray(), x => x.GetProperty("Slug").GetString() == "review-agent");
        Assert.Contains(skillListResult.GetProperty("skills").EnumerateArray(), x => x.GetProperty("Id").GetGuid() == skillId);
        Assert.Equal("Run MCP usability pass", skillGetResult.GetProperty("skill").GetProperty("Skill").GetProperty("Name").GetString());
        Assert.Equal("Run MCP usability pass", skillRecommendResult.GetProperty("skills").EnumerateArray().First().GetProperty("Name").GetString());
        Assert.Equal(1, searchResult.GetProperty("results").GetArrayLength());
        Assert.NotNull(contextPack);
        Assert.Contains(contextPack!.TopMatches, x => x.Kind == ContextRecordKind.Memory && x.Title.Contains("API outage", StringComparison.Ordinal));
        Assert.Equal("operator", operatorBootstrap.GetProperty("Profile").GetString());
        Assert.Equal("Ops", roomDirectory.GetProperty("rooms").EnumerateArray().Single().GetProperty("WingName").GetString());
        Assert.Equal(4, agentDirectory.GetProperty("agents").GetArrayLength());
        Assert.Equal("Context Agent", agentResource.GetProperty("Agent").GetProperty("Name").GetString());
        Assert.Contains(skillDirectory.GetProperty("skills").EnumerateArray(), x => x.GetProperty("Id").GetGuid() == skillId);
        Assert.Equal("Run MCP usability pass", skillResource.GetProperty("Skill").GetProperty("Name").GetString());
    }

    [Fact]
    public async Task JsonRpcInitializeAndToolsListUseStandardMcpEndpoint()
    {
        await using var harness = await McpHarness.CreateAsync();
        await using var scope = harness.Services.CreateAsyncScope();

        var controller = CreateController(scope.ServiceProvider, IPAddress.Loopback);
        using var initializeDocument = JsonDocument.Parse("""
        {
          "jsonrpc": "2.0",
          "id": 1,
          "method": "initialize",
          "params": {
            "protocolVersion": "2025-03-26",
            "capabilities": {},
            "clientInfo": {
              "name": "Copilot CLI",
              "version": "1.0.48"
            }
          }
        }
        """);

        var initializeResult = await controller.Rpc(initializeDocument.RootElement.Clone(), CancellationToken.None);
        var initializeOk = Assert.IsType<OkObjectResult>(initializeResult);
        var initializePayload = Assert.IsType<JsonObject>(initializeOk.Value);
        var sessionId = controller.HttpContext.Response.Headers["Mcp-Session-Id"].ToString();

        Assert.Equal("2.0", initializePayload["jsonrpc"]?.GetValue<string>());
        Assert.Equal("2025-03-26", initializePayload["result"]?["protocolVersion"]?.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(sessionId));

        controller.HttpContext.Request.Headers["Mcp-Session-Id"] = sessionId;
        using var toolsDocument = JsonDocument.Parse("""
        {
          "jsonrpc": "2.0",
          "id": 2,
          "method": "tools/list",
          "params": {}
        }
        """);

        var toolsResult = await controller.Rpc(toolsDocument.RootElement.Clone(), CancellationToken.None);
        var toolsOk = Assert.IsType<OkObjectResult>(toolsResult);
        var toolsPayload = Assert.IsType<JsonObject>(toolsOk.Value);
        var tools = Assert.IsType<JsonArray>(toolsPayload["result"]?["tools"]);

        Assert.NotEmpty(tools);
        Assert.Contains(tools, node => string.Equals(node?["name"]?.GetValue<string>(), "focus.memory.search", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReadOnlyApiKeysCannotInvokeMutatingMcpTools()
    {
        await using var harness = await McpHarness.CreateAsync(new Dictionary<string, string?>
        {
            ["FocusPalace:Mcp:ApiKeyDefinitions:0:Value"] = "readonly-key",
            ["FocusPalace:Mcp:ApiKeyDefinitions:0:Label"] = "observer",
            ["FocusPalace:Mcp:ApiKeyDefinitions:0:CanWrite"] = "false"
        });

        await using var scope = harness.Services.CreateAsyncScope();
        var authService = scope.ServiceProvider.GetRequiredService<FocusMcpAuthService>();
        var sessionService = scope.ServiceProvider.GetRequiredService<FocusMcpSessionService>();
        var toolRegistry = scope.ServiceProvider.GetRequiredService<FocusMcpToolRegistry>();
        var resourceRegistry = scope.ServiceProvider.GetRequiredService<FocusMcpResourceRegistry>();
        var eventBus = scope.ServiceProvider.GetRequiredService<FocusMcpEventBus>();

        var controller = new FocusLAIci.Web.Controllers.Api.McpController(
            authService,
            sessionService,
            toolRegistry,
            resourceRegistry,
            eventBus,
            NullLogger<FocusLAIci.Web.Controllers.Api.McpController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.10.10.20");
        controller.HttpContext.Request.Headers["Authorization"] = "Bearer readonly-key";

        var session = sessionService.CreateSession(new FocusMcpInitializeInput { ClientName = "Readonly", ClientVersion = "1.0" }, "10.10.10.20", "api-key-readonly");
        using var document = JsonDocument.Parse("""
        {
          "name": "focus.memory.save",
          "arguments": {
            "title": "Blocked write",
            "summary": "Should not save",
            "content": "Readonly key must not mutate."
          }
        }
        """);

        var response = await controller.Message(new FocusMcpRequestEnvelope
        {
            Id = "readonly-test",
            Type = "call_tool",
            SessionId = session.SessionId,
            Payload = document.RootElement.Clone()
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var envelope = Assert.IsType<FocusMcpResponseEnvelope>(ok.Value);
        Assert.Equal("error", envelope.Type);
        Assert.Equal("unauthorized", envelope.Error?.Code);
    }

    private static FocusLAIci.Web.Controllers.Api.McpController CreateController(IServiceProvider services, IPAddress remoteAddress, string? bearerToken = null)
    {
        var controller = new FocusLAIci.Web.Controllers.Api.McpController(
            services.GetRequiredService<FocusMcpAuthService>(),
            services.GetRequiredService<FocusMcpSessionService>(),
            services.GetRequiredService<FocusMcpToolRegistry>(),
            services.GetRequiredService<FocusMcpResourceRegistry>(),
            services.GetRequiredService<FocusMcpEventBus>(),
            NullLogger<FocusLAIci.Web.Controllers.Api.McpController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
        controller.HttpContext.Connection.RemoteIpAddress = remoteAddress;
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            controller.HttpContext.Request.Headers["Authorization"] = $"Bearer {bearerToken}";
        }

        return controller;
    }

    private sealed class McpHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private McpHarness(ServiceProvider services, SqliteConnection connection)
        {
            Services = services;
            _connection = connection;
        }

        public ServiceProvider Services { get; }

        public static async Task<McpHarness> CreateAsync(IReadOnlyDictionary<string, string?>? extraConfiguration = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:FocusPalace"] = "Data Source=:memory:"
            };
            if (extraConfiguration is not null)
            {
                foreach (var pair in extraConfiguration)
                {
                    settings[pair.Key] = pair.Value;
                }
            }

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(settings)
                .Build();

            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<IHostEnvironment>(new TestHostEnvironment
            {
                ContentRootPath = AppContext.BaseDirectory
            });
            services.AddLogging();
            services.AddSingleton<FocusMcpAuthService>();
            services.AddSingleton<FocusDatabaseTargetService>();
            services.AddSingleton<FocusMcpSessionService>();
            services.AddSingleton<FocusMcpEventBus>();
            services.AddSingleton<FocusAgentCatalogService>();
            services.AddSingleton<FocusDiagnosticsService>();
            services.AddDbContext<FocusMemoryContext>(options => options.UseSqlite(connection));
            services.AddScoped<IFocusEventPublisher>(_ => NullFocusEventPublisher.Instance);
            services.AddScoped<ContextService>();
            services.AddScoped<CodeGraphService>();
            services.AddScoped<PalaceService>();
            services.AddScoped<TicketingService>();
            services.AddScoped<SiteSettingsService>();
            services.AddScoped<FocusMcpToolRegistry>();
            services.AddScoped<FocusMcpResourceRegistry>();

            var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<FocusMemoryContext>();
            await dbContext.Database.EnsureCreatedAsync();

            return new McpHarness(provider, connection);
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "FocusLAIci.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(AppContext.BaseDirectory);
    }
}
