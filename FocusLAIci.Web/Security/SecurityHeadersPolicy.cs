using System.Security.Cryptography;
using Microsoft.Extensions.Primitives;

namespace FocusLAIci.Web.Security;

public static class SecurityHeadersPolicy
{
    public const string CspNonceItemKey = "Focus.Security.CspNonce";

    public static string CreateNonce()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    public static string BuildContentSecurityPolicy(string nonce)
    {
        if (string.IsNullOrWhiteSpace(nonce))
        {
            throw new ArgumentException("A nonce is required to build the content security policy.", nameof(nonce));
        }

        return string.Join("; ", new[]
        {
            "default-src 'self'",
            $"script-src 'self' 'nonce-{nonce}'",
            "style-src 'self' 'unsafe-inline'",
            "img-src 'self' data:",
            "font-src 'self'",
            "connect-src 'self'",
            "object-src 'none'",
            "frame-src 'none'",
            "frame-ancestors 'none'",
            "base-uri 'self'",
            "form-action 'self'"
        });
    }

    public static void Apply(IHeaderDictionary headers, string nonce)
    {
        headers["Content-Security-Policy"] = new StringValues(BuildContentSecurityPolicy(nonce));
        headers["Referrer-Policy"] = new StringValues("strict-origin-when-cross-origin");
        headers["X-Content-Type-Options"] = new StringValues("nosniff");
        headers["X-Frame-Options"] = new StringValues("DENY");
        headers["Cross-Origin-Opener-Policy"] = new StringValues("same-origin");
        headers["Cross-Origin-Resource-Policy"] = new StringValues("same-origin");
        headers["Permissions-Policy"] = new StringValues("accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()");
    }
}
