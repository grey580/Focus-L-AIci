using System.Collections.Concurrent;
using FocusLAIci.Web.Models;

namespace FocusLAIci.Web.Services;

public sealed class FocusMcpSessionService
{
    private readonly ConcurrentDictionary<string, FocusMcpSessionState> _sessions = new(StringComparer.Ordinal);

    public FocusMcpSessionSummaryViewModel CreateSession(FocusMcpInitializeInput input, string remoteAddress, string authMode)
    {
        var now = DateTime.UtcNow;
        var session = new FocusMcpSessionState(
            Guid.NewGuid().ToString("N"),
            input.ClientName.Trim(),
            input.ClientVersion.Trim(),
            remoteAddress,
            authMode,
            now,
            now,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        _sessions[session.SessionId] = session;
        return session.ToViewModel();
    }

    public bool TryTouch(string sessionId, out FocusMcpSessionSummaryViewModel session)
    {
        session = new FocusMcpSessionSummaryViewModel();
        if (!_sessions.TryGetValue(sessionId, out var current))
        {
            return false;
        }

        var updated = current with { LastSeenUtc = DateTime.UtcNow };
        _sessions[sessionId] = updated;
        session = updated.ToViewModel();
        return true;
    }

    public bool Exists(string sessionId) => _sessions.ContainsKey(sessionId);

    public IReadOnlyCollection<FocusMcpSessionSummaryViewModel> GetSessions()
    {
        return _sessions.Values
            .OrderByDescending(x => x.LastSeenUtc)
            .Select(x => x.ToViewModel())
            .ToArray();
    }

    public IReadOnlyCollection<string> Subscribe(string sessionId, IEnumerable<string> resourceUris)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new InvalidOperationException("The MCP session no longer exists.");
        }

        var subscriptions = new HashSet<string>(session.ResourceSubscriptions, StringComparer.OrdinalIgnoreCase);
        foreach (var resourceUri in resourceUris.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            subscriptions.Add(resourceUri.Trim());
        }

        var updated = session with
        {
            LastSeenUtc = DateTime.UtcNow,
            ResourceSubscriptions = subscriptions
        };

        _sessions[sessionId] = updated;
        return updated.ResourceSubscriptions.OrderBy(x => x).ToArray();
    }

    public IReadOnlyCollection<string> Unsubscribe(string sessionId, IEnumerable<string> resourceUris)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new InvalidOperationException("The MCP session no longer exists.");
        }

        var subscriptions = new HashSet<string>(session.ResourceSubscriptions, StringComparer.OrdinalIgnoreCase);
        foreach (var resourceUri in resourceUris.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            subscriptions.Remove(resourceUri.Trim());
        }

        var updated = session with
        {
            LastSeenUtc = DateTime.UtcNow,
            ResourceSubscriptions = subscriptions
        };

        _sessions[sessionId] = updated;
        return updated.ResourceSubscriptions.OrderBy(x => x).ToArray();
    }

    public bool IsSubscribed(string sessionId, IReadOnlyCollection<string> resourceUris)
    {
        if (!_sessions.TryGetValue(sessionId, out var session) || session.ResourceSubscriptions.Count == 0)
        {
            return false;
        }

        foreach (var subscription in session.ResourceSubscriptions)
        {
            if (resourceUris.Any(resourceUri => MatchesSubscription(subscription, resourceUri)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesSubscription(string subscription, string resourceUri)
    {
        if (subscription.EndsWith('*'))
        {
            return resourceUri.StartsWith(subscription[..^1], StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(subscription, resourceUri, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record FocusMcpSessionState(
        string SessionId,
        string ClientName,
        string ClientVersion,
        string RemoteAddress,
        string AuthMode,
        DateTime CreatedUtc,
        DateTime LastSeenUtc,
        HashSet<string> ResourceSubscriptions)
    {
        public FocusMcpSessionSummaryViewModel ToViewModel()
        {
            return new FocusMcpSessionSummaryViewModel
            {
                SessionId = SessionId,
                ClientName = ClientName,
                ClientVersion = ClientVersion,
                RemoteAddress = RemoteAddress,
                AuthMode = AuthMode,
                CreatedUtc = CreatedUtc,
                LastSeenUtc = LastSeenUtc,
                ResourceSubscriptions = ResourceSubscriptions.OrderBy(x => x).ToArray()
            };
        }
    }
}
