using System.Net;

namespace FocusLAIci.Web.Security;

public sealed class ApiWriteOriginGuardMiddleware
{
    private readonly RequestDelegate _next;

    public ApiWriteOriginGuardMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (TargetsApiWrite(context.Request) && IsUnauthorizedWrite(context))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Cross-site or non-loopback writes to Focus APIs are blocked.");
            return;
        }

        await _next(context);
    }

    private static bool TargetsApiWrite(HttpRequest request)
    {
        if (!request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // The MCP endpoint enforces its own loopback/API-key authorization model in
        // FocusMcpAuthService, so it is intentionally exempt from this browser-focused guard.
        if (request.Path.StartsWithSegments("/api/mcp", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !HttpMethods.IsGet(request.Method)
            && !HttpMethods.IsHead(request.Method)
            && !HttpMethods.IsOptions(request.Method)
            && !HttpMethods.IsTrace(request.Method);
    }

    private static bool IsUnauthorizedWrite(HttpContext context)
    {
        var request = context.Request;
        var fetchSite = request.Headers["Sec-Fetch-Site"].ToString();
        var hasOrigin = TryParseUri(request.Headers.Origin.ToString(), out var originUri);
        var hasReferer = TryParseUri(request.Headers.Referer.ToString(), out var refererUri);

        if (string.IsNullOrEmpty(fetchSite) && !hasOrigin && !hasReferer)
        {
            // No browser fetch metadata at all (e.g. curl, scripts, or other non-browser tooling).
            // Only trust these callers when they are connecting from the local machine, mirroring
            // the loopback trust model already used by the MCP endpoint.
            var remoteIp = context.Connection.RemoteIpAddress;
            return remoteIp is null || !IPAddress.IsLoopback(remoteIp);
        }

        if (string.Equals(fetchSite, "cross-site", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (hasOrigin && !IsSameOrigin(request, originUri))
        {
            return true;
        }

        if (hasReferer && !IsSameOrigin(request, refererUri))
        {
            return true;
        }

        return false;
    }

    private static bool TryParseUri(string value, out Uri uri)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out uri!);
    }

    private static bool IsSameOrigin(HttpRequest request, Uri uri)
    {
        var requestPort = request.Host.Port ?? (string.Equals(request.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80);
        var uriPort = uri.IsDefaultPort
            ? (string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80)
            : uri.Port;

        return string.Equals(uri.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && uriPort == requestPort;
    }
}
