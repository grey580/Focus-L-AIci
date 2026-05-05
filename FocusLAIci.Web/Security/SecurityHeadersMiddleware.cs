using Microsoft.Extensions.Primitives;

namespace FocusLAIci.Web.Security;

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var nonce = SecurityHeadersPolicy.CreateNonce();
        context.Items[SecurityHeadersPolicy.CspNonceItemKey] = nonce;

        context.Response.OnStarting(static state =>
        {
            var httpContext = (HttpContext)state;
            var nonceValue = httpContext.Items[SecurityHeadersPolicy.CspNonceItemKey] as string ?? string.Empty;
            SecurityHeadersPolicy.Apply(httpContext.Response.Headers, nonceValue);
            return Task.CompletedTask;
        }, context);

        await _next(context);
    }
}
