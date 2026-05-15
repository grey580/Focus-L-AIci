using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace FocusLAIci.Web.Models;

public sealed class FocusMcpRequestEnvelope
{
    [StringLength(80)]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [Required]
    [StringLength(80)]
    public string Type { get; set; } = string.Empty;

    [StringLength(80)]
    public string SessionId { get; set; } = string.Empty;

    public JsonElement Payload { get; set; }
}

public sealed class FocusMcpResponseEnvelope
{
    public string Id { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public DateTime GeneratedUtc { get; init; } = DateTime.UtcNow;
    public object? Result { get; init; }
    public FocusMcpErrorPayload? Error { get; init; }
}

public sealed class FocusMcpErrorPayload
{
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public object? Details { get; init; }
}

public sealed class FocusMcpInputException : InvalidOperationException
{
    public FocusMcpInputException(string message, object? details = null)
        : base(message)
    {
        Details = details;
    }

    public object? Details { get; }
}

public sealed class FocusMcpInitializeInput
{
    [Required]
    [StringLength(120)]
    public string ClientName { get; set; } = "Unknown client";

    [StringLength(40)]
    public string ClientVersion { get; set; } = "1.0";
}

public sealed class FocusMcpJsonRpcInitializeParams
{
    [Required]
    [StringLength(40)]
    public string ProtocolVersion { get; set; } = string.Empty;

    public JsonElement Capabilities { get; set; }

    [Required]
    public FocusMcpClientInfo ClientInfo { get; set; } = new();
}

public sealed class FocusMcpClientInfo
{
    [Required]
    [StringLength(120)]
    public string Name { get; set; } = "Unknown client";

    [StringLength(40)]
    public string Version { get; set; } = "1.0";
}

public sealed class FocusMcpCompleteInput
{
    [StringLength(160)]
    public string Prefix { get; set; } = string.Empty;

    [StringLength(40)]
    public string Scope { get; set; } = "tools";
}

public sealed class FocusMcpToolCallInput
{
    [Required]
    [StringLength(160)]
    public string Name { get; set; } = string.Empty;

    public JsonElement Arguments { get; set; }
}

public sealed class FocusMcpCompletionParams
{
    [Required]
    public FocusMcpCompletionReference Ref { get; set; } = new();

    [Required]
    public FocusMcpCompletionArgument Argument { get; set; } = new();
}

public sealed class FocusMcpCompletionReference
{
    [Required]
    [StringLength(40)]
    public string Type { get; set; } = string.Empty;

    [StringLength(160)]
    public string Name { get; set; } = string.Empty;

    [StringLength(400)]
    public string Uri { get; set; } = string.Empty;
}

public sealed class FocusMcpCompletionArgument
{
    [Required]
    [StringLength(80)]
    public string Name { get; set; } = string.Empty;

    [StringLength(400)]
    public string Value { get; set; } = string.Empty;
}

public sealed class FocusMcpResourceGetInput
{
    [Required]
    [StringLength(400)]
    public string Uri { get; set; } = string.Empty;
}

public sealed class FocusMcpResourceSubscriptionInputSingle
{
    [Required]
    [StringLength(400)]
    public string Uri { get; set; } = string.Empty;
}

public sealed class FocusMcpResourceSubscriptionInput
{
    [Required]
    [MinLength(1)]
    public IReadOnlyCollection<string> ResourceUris { get; set; } = Array.Empty<string>();
}

public sealed class FocusMcpToolDescriptor
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public bool Mutating { get; init; }
    public object InputSchema { get; init; } = new();
    public object OutputSchema { get; init; } = new();
}

public sealed class FocusMcpResourceDescriptor
{
    public string Uri { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string MimeType { get; init; } = "application/json";
    public bool SupportsSubscription { get; init; }
}

public sealed class FocusMcpResourceContent
{
    public string Uri { get; init; } = string.Empty;
    public string MimeType { get; init; } = "application/json";
    public object? Data { get; init; }
}

public sealed class FocusMcpSessionSummaryViewModel
{
    public string SessionId { get; init; } = string.Empty;
    public string ClientName { get; init; } = string.Empty;
    public string ClientVersion { get; init; } = string.Empty;
    public string RemoteAddress { get; init; } = string.Empty;
    public string AuthMode { get; init; } = string.Empty;
    public bool IsRecovered { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime LastSeenUtc { get; init; }
    public IReadOnlyCollection<string> ResourceSubscriptions { get; init; } = Array.Empty<string>();
}

public sealed class FocusMcpPublishedEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string EventType { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Severity { get; init; } = "info";
    public DateTime OccurredUtc { get; init; } = DateTime.UtcNow;
    public IReadOnlyCollection<string> ResourceUris { get; init; } = Array.Empty<string>();
    public object? Payload { get; init; }
}

public sealed class FocusMcpConsoleViewModel
{
    public string AuthMode { get; init; } = string.Empty;
    public string MessageEndpointUrl { get; init; } = string.Empty;
    public string StreamEndpointTemplate { get; init; } = string.Empty;
    public string ManifestEndpointUrl { get; init; } = string.Empty;
    public string SelfTestApiUrl { get; init; } = string.Empty;
    public string SampleRequestJson { get; init; } = string.Empty;
    public IReadOnlyCollection<FocusMcpToolDescriptor> Tools { get; init; } = Array.Empty<FocusMcpToolDescriptor>();
    public IReadOnlyCollection<FocusMcpResourceDescriptor> Resources { get; init; } = Array.Empty<FocusMcpResourceDescriptor>();
    public IReadOnlyCollection<FocusMcpSessionSummaryViewModel> Sessions { get; init; } = Array.Empty<FocusMcpSessionSummaryViewModel>();
    public IReadOnlyCollection<FocusMcpPublishedEvent> RecentEvents { get; init; } = Array.Empty<FocusMcpPublishedEvent>();
    public FocusOperatorDiagnosticsViewModel OperatorDiagnostics { get; init; } = new();
}

public sealed class FocusMcpAuthorizationResult
{
    public bool IsAuthorized { get; init; }
    public string AuthMode { get; init; } = string.Empty;
    public string ErrorMessage { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public bool CanWrite { get; init; } = true;
}

public sealed class FocusMcpCheckResultViewModel
{
    public string Name { get; init; } = string.Empty;
    public bool Passed { get; init; }
    public string Summary { get; init; } = string.Empty;
    public object? Details { get; init; }
}

public sealed class FocusMcpSelfTestViewModel
{
    public DateTime GeneratedUtc { get; init; } = DateTime.UtcNow;
    public string Status { get; init; } = "unknown";
    public string Summary { get; init; } = string.Empty;
    public int PassedCount { get; init; }
    public int FailedCount { get; init; }
    public IReadOnlyCollection<FocusMcpCheckResultViewModel> Checks { get; init; } = Array.Empty<FocusMcpCheckResultViewModel>();
}

public sealed class FocusOperatorDiagnosticsViewModel
{
    public DateTime GeneratedUtc { get; init; } = DateTime.UtcNow;
    public string EnvironmentName { get; init; } = string.Empty;
    public string ContentRootPath { get; init; } = string.Empty;
    public string ApplicationVersion { get; init; } = string.Empty;
    public string AuthMode { get; init; } = string.Empty;
    public int ToolCount { get; init; }
    public int ResourceCount { get; init; }
    public int ActiveSessionCount { get; init; }
    public int RecoveredSessionCount { get; init; }
    public string PublishScriptPath { get; init; } = string.Empty;
    public FocusDatabaseTargetSnapshot DatabaseTarget { get; init; } = new();
    public FocusMcpSelfTestViewModel SelfTest { get; init; } = new();
}
