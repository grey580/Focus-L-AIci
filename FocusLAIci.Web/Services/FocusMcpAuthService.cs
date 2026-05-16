using System.Net;
using System.Collections.Concurrent;
using FocusLAIci.Web.Models;

namespace FocusLAIci.Web.Services;

public sealed class FocusMcpAuthService(IConfiguration configuration)
{
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _requestWindows = new(StringComparer.OrdinalIgnoreCase);

    public string DescribeMode()
    {
        return GetConfiguredKeys().Count == 0
            ? "Loopback clients are allowed without an API key. Non-loopback clients are denied."
            : "Loopback clients are allowed without an API key. Non-loopback clients must supply a configured MCP API key. Keys can be labeled and limited to read-only access.";
    }

    public bool TryAuthorize(HttpContext httpContext, out FocusMcpAuthorizationResult result)
    {
        var remoteIp = httpContext.Connection.RemoteIpAddress;
        var remoteAddress = remoteIp?.ToString() ?? "unknown";
        if (!IsWithinRateLimit(remoteAddress))
        {
            result = new FocusMcpAuthorizationResult
            {
                IsAuthorized = false,
                ErrorMessage = "Rate limit exceeded for this MCP client.",
                AuthMode = "rate-limited"
            };
            return false;
        }

        if (remoteIp is not null && IPAddress.IsLoopback(remoteIp))
        {
            result = new FocusMcpAuthorizationResult
            {
                IsAuthorized = true,
                AuthMode = "loopback",
                Label = "loopback",
                CanWrite = true
            };
            return true;
        }

        var configuredKeys = GetConfiguredKeys();
        if (configuredKeys.Count == 0)
        {
            result = new FocusMcpAuthorizationResult
            {
                IsAuthorized = false,
                ErrorMessage = "Focus MCP only allows loopback connections unless API keys are configured.",
                AuthMode = "denied"
            };
            return false;
        }

        var suppliedKey = ReadSuppliedKey(httpContext);
        var keyMatch = configuredKeys.FirstOrDefault(x => string.Equals(x.Value, suppliedKey?.Trim(), StringComparison.Ordinal));
        if (keyMatch is null)
        {
            result = new FocusMcpAuthorizationResult
            {
                IsAuthorized = false,
                ErrorMessage = "A valid Focus MCP API key is required.",
                AuthMode = "denied"
            };
            return false;
        }

        result = new FocusMcpAuthorizationResult
        {
            IsAuthorized = true,
            AuthMode = keyMatch.CanWrite ? "api-key" : "api-key-readonly",
            Label = keyMatch.Label,
            CanWrite = keyMatch.CanWrite
        };
        return true;
    }

    public bool TryAuthorize(HttpContext httpContext, out string authMode, out string errorMessage)
    {
        var success = TryAuthorize(httpContext, out var result);
        authMode = result.AuthMode;
        errorMessage = result.ErrorMessage;
        return success;
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

    private IReadOnlyCollection<ConfiguredApiKey> GetConfiguredKeys()
    {
        var sectionKeys = configuration.GetSection("FocusPalace:Mcp:ApiKeys").Get<string[]>() ?? Array.Empty<string>();
        var keyedDefinitions = configuration
            .GetSection("FocusPalace:Mcp:ApiKeyDefinitions")
            .GetChildren()
            .Select(section =>
            {
                var value = section["Value"]?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(value))
                {
                    return null;
                }

                var scopes = section.GetSection("Scopes").Get<string[]>() ?? Array.Empty<string>();
                var canWrite = section.GetValue<bool?>("CanWrite")
                    ?? !scopes.Any()
                    || scopes.Any(scope => string.Equals(scope, "write", StringComparison.OrdinalIgnoreCase) || string.Equals(scope, "read-write", StringComparison.OrdinalIgnoreCase));

                return new ConfiguredApiKey(
                    value,
                    section["Label"]?.Trim() ?? "api-key",
                    canWrite);
            })
            .Where(x => x is not null)
            .Cast<ConfiguredApiKey>();
        var inlineKeys = (configuration["FocusPalace:Mcp:ApiKeysCsv"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return sectionKeys
            .Concat(inlineKeys)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Select(x => new ConfiguredApiKey(x, "api-key", true))
            .Concat(keyedDefinitions)
            .GroupBy(x => x.Value, StringComparer.Ordinal)
            .Select(x => x.First())
            .ToArray();
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

    private sealed record ConfiguredApiKey(string Value, string Label, bool CanWrite);
}
