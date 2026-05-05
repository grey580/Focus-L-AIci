using System.Net;
using FocusLAIci.Web.Models;
using FocusLAIci.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
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

        var allowed = service.TryAuthorize(httpContext, out var authMode, out var errorMessage);

        Assert.True(allowed);
        Assert.Equal("loopback", authMode);
        Assert.Equal(string.Empty, errorMessage);
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

        var allowed = service.TryAuthorize(httpContext, out var authMode, out var errorMessage);

        Assert.True(allowed);
        Assert.Equal("api-key", authMode);
        Assert.Equal(string.Empty, errorMessage);
    }
}
