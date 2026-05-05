using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Threading.Channels;
using FocusLAIci.Web.Models;
using FocusLAIci.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace FocusLAIci.Web.Controllers.Api;

[ApiController]
[Route("api/mcp")]
public sealed class McpController(
    FocusMcpAuthService authService,
    FocusMcpSessionService sessionService,
    FocusMcpToolRegistry toolRegistry,
    FocusMcpResourceRegistry resourceRegistry,
    FocusMcpEventBus eventBus,
    ILogger<McpController> logger) : ControllerBase
{
    [HttpGet("manifest")]
    public IActionResult Manifest()
    {
        if (!TryAuthorize(out var authMode, out var unauthorized))
        {
            return unauthorized!;
        }

        return Ok(new
        {
            serverName = "Focus L-AIci MCP",
            protocolVersion = "focus-local-http-2026-05",
            authMode,
            messageEndpoint = "/api/mcp/message",
            streamEndpointTemplate = "/api/mcp/events/{sessionId}",
            tools = toolRegistry.GetTools(),
            resources = resourceRegistry.GetResources()
        });
    }

    [HttpPost("message")]
    public async Task<ActionResult<FocusMcpResponseEnvelope>> Message([FromBody] FocusMcpRequestEnvelope request, CancellationToken cancellationToken)
    {
        if (!TryAuthorize(out var authMode, out var unauthorized))
        {
            return unauthorized!;
        }

        try
        {
            var response = request.Type.Trim().ToLowerInvariant() switch
            {
                "initialize" => HandleInitialize(request, authMode),
                "ping" => HandlePing(request),
                "complete" => HandleComplete(request),
                "call_tool" => await HandleToolCallAsync(request, cancellationToken),
                "resource_list" => HandleResourceList(request),
                "resource_get" => await HandleResourceGetAsync(request, authMode, cancellationToken),
                "resource_subscribe" => HandleResourceSubscribe(request),
                "resource_unsubscribe" => HandleResourceUnsubscribe(request),
                _ => BuildError(request, "unsupported_message", $"Unsupported MCP message type '{request.Type}'.")
            };

            logger.LogInformation("Focus MCP {Type} handled for session {SessionId}", request.Type, response.SessionId);
            return Ok(response);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(exception, "Focus MCP request {Type} failed", request.Type);
            return BadRequest(BuildError(request, "invalid_request", exception.Message));
        }
    }

    [HttpGet("events/{sessionId}")]
    public async Task Events(string sessionId, CancellationToken cancellationToken)
    {
        if (!TryAuthorize(out _, out var unauthorized))
        {
            Response.StatusCode = unauthorized?.StatusCode ?? StatusCodes.Status401Unauthorized;
            return;
        }

        if (!sessionService.Exists(sessionId))
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        var channel = Channel.CreateUnbounded<FocusMcpPublishedEvent>();
        using var subscription = eventBus.Subscribe(eventItem =>
        {
            if (sessionService.IsSubscribed(sessionId, eventItem.ResourceUris))
            {
                channel.Writer.TryWrite(eventItem);
            }
        });

        await WriteServerSentEventAsync("connected", new
        {
            type = "connected",
            sessionId,
            serverTimeUtc = DateTime.UtcNow
        }, cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            var readTask = channel.Reader.ReadAsync(cancellationToken).AsTask();
            var completedTask = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(15), cancellationToken));
            if (completedTask == readTask)
            {
                var eventItem = await readTask;
                await WriteServerSentEventAsync("resource_updated", new
                {
                    type = "resource_updated",
                    sessionId,
                    result = eventItem
                }, cancellationToken);
                continue;
            }

            await Response.WriteAsync(": keepalive\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
    }

    private FocusMcpResponseEnvelope HandleInitialize(FocusMcpRequestEnvelope request, string authMode)
    {
        var input = Deserialize<FocusMcpInitializeInput>(request.Payload);
        Validate(input);
        var remoteAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var session = sessionService.CreateSession(input, remoteAddress, authMode);
        return new FocusMcpResponseEnvelope
        {
            Id = request.Id,
            Type = "initialize",
            SessionId = session.SessionId,
            Result = new
            {
                serverName = "Focus L-AIci MCP",
                protocolVersion = "focus-local-http-2026-05",
                authMode,
                session,
                capabilities = new
                {
                    tools = true,
                    resources = true,
                    streaming = "sse"
                },
                tools = toolRegistry.GetTools(),
                resources = resourceRegistry.GetResources(),
                streamEndpoint = $"/api/mcp/events/{session.SessionId}"
            }
        };
    }

    private FocusMcpResponseEnvelope HandlePing(FocusMcpRequestEnvelope request)
    {
        var session = EnsureSession(request.SessionId);
        return new FocusMcpResponseEnvelope
        {
            Id = request.Id,
            Type = "ping",
            SessionId = session.SessionId,
            Result = new
            {
                status = "ok",
                serverTimeUtc = DateTime.UtcNow
            }
        };
    }

    private FocusMcpResponseEnvelope HandleComplete(FocusMcpRequestEnvelope request)
    {
        var session = EnsureSession(request.SessionId);
        var input = Deserialize<FocusMcpCompleteInput>(request.Payload);
        var matches = string.Equals(input.Scope, "resources", StringComparison.OrdinalIgnoreCase)
            ? resourceRegistry.Complete(input.Prefix)
            : toolRegistry.Complete(input.Prefix);

        return new FocusMcpResponseEnvelope
        {
            Id = request.Id,
            Type = "complete",
            SessionId = session.SessionId,
            Result = new
            {
                scope = input.Scope,
                matches
            }
        };
    }

    private async Task<FocusMcpResponseEnvelope> HandleToolCallAsync(FocusMcpRequestEnvelope request, CancellationToken cancellationToken)
    {
        var session = EnsureSession(request.SessionId);
        var input = Deserialize<FocusMcpToolCallInput>(request.Payload);
        Validate(input);
        var result = await toolRegistry.InvokeAsync(input.Name, input.Arguments, cancellationToken);

        return new FocusMcpResponseEnvelope
        {
            Id = request.Id,
            Type = "tool_result",
            SessionId = session.SessionId,
            Result = new
            {
                tool = input.Name,
                output = result
            }
        };
    }

    private FocusMcpResponseEnvelope HandleResourceList(FocusMcpRequestEnvelope request)
    {
        var session = EnsureSession(request.SessionId);
        return new FocusMcpResponseEnvelope
        {
            Id = request.Id,
            Type = "resource_list",
            SessionId = session.SessionId,
            Result = new
            {
                resources = resourceRegistry.GetResources()
            }
        };
    }

    private async Task<FocusMcpResponseEnvelope> HandleResourceGetAsync(FocusMcpRequestEnvelope request, string authMode, CancellationToken cancellationToken)
    {
        var session = EnsureSession(request.SessionId);
        var input = Deserialize<FocusMcpResourceGetInput>(request.Payload);
        Validate(input);
        var resource = await resourceRegistry.GetResourceAsync(input.Uri, authMode, cancellationToken);
        return new FocusMcpResponseEnvelope
        {
            Id = request.Id,
            Type = "resource_get",
            SessionId = session.SessionId,
            Result = resource
        };
    }

    private FocusMcpResponseEnvelope HandleResourceSubscribe(FocusMcpRequestEnvelope request)
    {
        var session = EnsureSession(request.SessionId);
        var input = Deserialize<FocusMcpResourceSubscriptionInput>(request.Payload);
        Validate(input);
        var unknownResources = input.ResourceUris.Where(x => !resourceRegistry.IsKnownResource(x) && !x.EndsWith('*')).ToArray();
        if (unknownResources.Length > 0)
        {
            throw new InvalidOperationException($"Unknown resource subscription: {string.Join(", ", unknownResources)}");
        }

        var subscriptions = sessionService.Subscribe(session.SessionId, input.ResourceUris);
        return new FocusMcpResponseEnvelope
        {
            Id = request.Id,
            Type = "resource_subscribe",
            SessionId = session.SessionId,
            Result = new
            {
                subscriptions
            }
        };
    }

    private FocusMcpResponseEnvelope HandleResourceUnsubscribe(FocusMcpRequestEnvelope request)
    {
        var session = EnsureSession(request.SessionId);
        var input = Deserialize<FocusMcpResourceSubscriptionInput>(request.Payload);
        Validate(input);
        var subscriptions = sessionService.Unsubscribe(session.SessionId, input.ResourceUris);
        return new FocusMcpResponseEnvelope
        {
            Id = request.Id,
            Type = "resource_unsubscribe",
            SessionId = session.SessionId,
            Result = new
            {
                subscriptions
            }
        };
    }

    private bool TryAuthorize(out string authMode, out ObjectResult? unauthorized)
    {
        if (authService.TryAuthorize(HttpContext, out authMode, out var errorMessage))
        {
            unauthorized = null;
            return true;
        }

        unauthorized = StatusCode(StatusCodes.Status401Unauthorized, new FocusMcpResponseEnvelope
        {
            Type = "error",
            Error = new FocusMcpErrorPayload
            {
                Code = "unauthorized",
                Message = errorMessage
            }
        });
        return false;
    }

    private FocusMcpSessionSummaryViewModel EnsureSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("An MCP session must be initialized first.");
        }

        if (!sessionService.TryTouch(sessionId, out var session))
        {
            throw new InvalidOperationException("That MCP session no longer exists.");
        }

        return session;
    }

    private async Task WriteServerSentEventAsync(string eventName, object payload, CancellationToken cancellationToken)
    {
        await Response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await Response.WriteAsync($"data: {JsonSerializer.Serialize(payload)}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private static T Deserialize<T>(JsonElement payload)
    {
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return Activator.CreateInstance<T>();
        }

        return JsonSerializer.Deserialize<T>(payload.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("Unable to parse the MCP payload.");
    }

    private static void Validate(object instance)
    {
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(instance, new ValidationContext(instance), results, true);
        if (isValid)
        {
            return;
        }

        throw new InvalidOperationException(results.First().ErrorMessage ?? "The MCP payload is invalid.");
    }

    private static FocusMcpResponseEnvelope BuildError(FocusMcpRequestEnvelope request, string code, string message)
    {
        return new FocusMcpResponseEnvelope
        {
            Id = request.Id,
            Type = "error",
            SessionId = request.SessionId,
            Error = new FocusMcpErrorPayload
            {
                Code = code,
                Message = message
            }
        };
    }
}
