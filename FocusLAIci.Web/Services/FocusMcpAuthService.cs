using System.Net;
using System.Collections.Concurrent;

namespace FocusLAIci.Web.Services;

public sealed class FocusMcpAuthService(IConfiguration configuration)
{
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _requestWindows = new(StringComparer.OrdinalIgnoreCase);

    public string DescribeMode()
    {
        return GetConfiguredKeys().Count == 0
            ? "Loopback clients are allowed without an API key. Non-loopback clients are denied."
            : "Loopback clients are allowed without an API key. Non-loopback clients must supply a configured MCP API key.";
    }

    public bool TryAuthorize(HttpContext httpContext, out string authMode, out string errorMessage)
    {
        authMode = "loopback";
        errorMessage = string.Empty;

        var remoteIp = httpContext.Connection.RemoteIpAddress;
        var remoteAddress = remoteIp?.ToString() ?? "unknown";
        if (!IsWithinRateLimit(remoteAddress))
        {
            errorMessage = "Rate limit exceeded for this MCP client.";
            authMode = "rate-limited";
            return false;
        }

        if (remoteIp is not null && IPAddress.IsLoopback(remoteIp))
        {
            return true;
        }

        var configuredKeys = GetConfiguredKeys();
        if (configuredKeys.Count == 0)
        {
            errorMessage = "Focus MCP only allows loopback connections unless API keys are configured.";
            authMode = "denied";
            return false;
        }

        var suppliedKey = ReadSuppliedKey(httpContext);
        if (string.IsNullOrWhiteSpace(suppliedKey) || !configuredKeys.Contains(suppliedKey.Trim()))
        {
            errorMessage = "A valid Focus MCP API key is required.";
            authMode = "denied";
            return false;
        }

        authMode = "api-key";
        return true;
    }

    private bool IsWithinRateLimit(string remoteAddress)
    {
        var limit = Math.Max(configuration.GetValue<int?>("FocusPalace:Mcp:RequestsPerMinute") ?? 120, 10);
        var now = DateTime.UtcNow;
        var queue = _requestWindows.GetOrAdd(remoteAddress, _ => new Queue<DateTime>());
        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() > TimeSpan.FromMinutes(1))
            {
                queue.Dequeue();
            }

            if (queue.Count >= limit)
            {
                return false;
            }

            queue.Enqueue(now);
            return true;
        }
    }

    private HashSet<string> GetConfiguredKeys()
    {
        var sectionKeys = configuration.GetSection("FocusPalace:Mcp:ApiKeys").Get<string[]>() ?? Array.Empty<string>();
        var inlineKeys = (configuration["FocusPalace:Mcp:ApiKeysCsv"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return sectionKeys
            .Concat(inlineKeys)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string ReadSuppliedKey(HttpContext httpContext)
    {
        if (httpContext.Request.Headers.TryGetValue("X-Focus-Mcp-Key", out var headerKey)
            && !string.IsNullOrWhiteSpace(headerKey))
        {
            return headerKey.ToString();
        }

        if (httpContext.Request.Headers.TryGetValue("Authorization", out var authorization)
            && authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authorization.ToString()["Bearer ".Length..].Trim();
        }

        return string.Empty;
    }
}
