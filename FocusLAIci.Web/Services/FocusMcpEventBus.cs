using System.Collections.Concurrent;
using FocusLAIci.Web.Models;

namespace FocusLAIci.Web.Services;

public interface IFocusEventPublisher
{
    Task PublishAsync(FocusMcpPublishedEvent eventItem, CancellationToken cancellationToken = default);
}

public sealed class NullFocusEventPublisher : IFocusEventPublisher
{
    public static readonly NullFocusEventPublisher Instance = new();

    private NullFocusEventPublisher()
    {
    }

    public Task PublishAsync(FocusMcpPublishedEvent eventItem, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

public sealed class FocusMcpEventBus(ILogger<FocusMcpEventBus> logger) : IFocusEventPublisher
{
    private const int MaxRecentEvents = 200;
    private readonly ConcurrentQueue<FocusMcpPublishedEvent> _recentEvents = new();
    private readonly object _subscriptionGate = new();
    private readonly Dictionary<Guid, Action<FocusMcpPublishedEvent>> _subscriptions = new();

    public Task PublishAsync(FocusMcpPublishedEvent eventItem, CancellationToken cancellationToken = default)
    {
        _recentEvents.Enqueue(eventItem);
        while (_recentEvents.Count > MaxRecentEvents && _recentEvents.TryDequeue(out _))
        {
        }

        List<Action<FocusMcpPublishedEvent>> handlers;
        lock (_subscriptionGate)
        {
            handlers = _subscriptions.Values.ToList();
        }

        logger.LogInformation(
            "Focus MCP event {EventType} for {Uris}",
            eventItem.EventType,
            string.Join(", ", eventItem.ResourceUris));

        foreach (var handler in handlers)
        {
            handler(eventItem);
        }

        return Task.CompletedTask;
    }

    public IReadOnlyCollection<FocusMcpPublishedEvent> GetRecentEvents(int limit = 50)
    {
        return _recentEvents
            .Reverse()
            .Take(limit)
            .ToArray();
    }

    public IDisposable Subscribe(Action<FocusMcpPublishedEvent> handler)
    {
        var subscriptionId = Guid.NewGuid();
        lock (_subscriptionGate)
        {
            _subscriptions[subscriptionId] = handler;
        }

        return new Subscription(() =>
        {
            lock (_subscriptionGate)
            {
                _subscriptions.Remove(subscriptionId);
            }
        });
    }

    private sealed class Subscription(Action onDispose) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                onDispose();
            }
        }
    }
}
