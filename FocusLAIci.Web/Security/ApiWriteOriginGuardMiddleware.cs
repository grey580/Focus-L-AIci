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
        if (TargetsApiWrite(context.Request) && IsCrossSiteBrowserRequest(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Cross-site browser writes to Focus APIs are blocked.");
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

        return !HttpMethods.IsGet(request.Method)
            && !HttpMethods.IsHead(request.Method)
            && !HttpMethods.IsOptions(request.Method)
            && !HttpMethods.IsTrace(request.Method);
    }

    private static bool IsCrossSiteBrowserRequest(HttpRequest request)
    {
        var fetchSite = request.Headers["Sec-Fetch-Site"].ToString();
        if (string.Equals(fetchSite, "cross-site", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (TryParseUri(request.Headers.Origin.ToString(), out var originUri) &&
            !IsSameOrigin(request, originUri))
        {
            return true;
        }

        if (TryParseUri(request.Headers.Referer.ToString(), out var refererUri) &&
            !IsSameOrigin(request, refererUri))
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
