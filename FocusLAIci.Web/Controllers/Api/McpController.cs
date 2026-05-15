using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
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
    private const string StandardProtocolVersion = "2025-03-26";
    private const string SessionHeaderName = "Mcp-Session-Id";

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    [HttpGet("")]
    public async Task Stream(CancellationToken cancellationToken)
    {
        if (!TryAuthorize(out _, out var unauthorized))
        {
            Response.StatusCode = unauthorized?.StatusCode ?? StatusCodes.Status401Unauthorized;
            return;
        }

        var sessionId = TryGetSessionIdFromHeader();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (!sessionService.Exists(sessionId))
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await RunEventStreamAsync(sessionId, cancellationToken);
    }

    [HttpPost("")]
    public async Task<IActionResult> Rpc([FromBody] JsonElement message, CancellationToken cancellationToken)
    {
        if (!TryAuthorize(out var auth, out var unauthorized))
        {
            return unauthorized!;
        }

        try
        {
            return await HandleJsonRpcAsync(message, auth, cancellationToken);
        }
        catch (FocusMcpInputException exception)
        {
            logger.LogWarning(exception, "Focus MCP JSON-RPC request failed");
            return BadRequest(BuildJsonRpcError(default, -32602, exception.Message, exception.Details));
        }
    }

    [HttpDelete("")]
    public IActionResult DeleteSession()
    {
        if (!TryAuthorize(out _, out var unauthorized))
        {
            return unauthorized!;
        }

        var sessionId = TryGetSessionIdFromHeader();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return BadRequest();
        }

        return sessionService.Remove(sessionId) ? NoContent() : NotFound();
    }

    [HttpGet("manifest")]
    public IActionResult Manifest()
    {
        if (!TryAuthorize(out var auth, out var unauthorized))
        {
            return unauthorized!;
        }

        return Ok(new
        {
            serverName = "Focus L-AIci MCP",
            protocolVersion = StandardProtocolVersion,
            transport = "streamable-http",
            authMode = auth.AuthMode,
            authLabel = auth.Label,
            canWrite = auth.CanWrite,
            messageEndpoint = "/api/mcp",
            streamEndpointTemplate = "/api/mcp",
            legacyMessageEndpoint = "/api/mcp/message",
            legacyStreamEndpointTemplate = "/api/mcp/events/{sessionId}",
            tools = toolRegistry.GetTools(),
            resources = resourceRegistry.GetResources()
        });
    }

    [HttpPost("message")]
    public async Task<ActionResult<FocusMcpResponseEnvelope>> Message([FromBody] FocusMcpRequestEnvelope request, CancellationToken cancellationToken)
    {
        if (!TryAuthorize(out var auth, out var unauthorized))
        {
            return unauthorized!;
        }

        try
        {
            var response = request.Type.Trim().ToLowerInvariant() switch
            {
                "initialize" => HandleInitialize(request, auth.AuthMode),
                "ping" => HandlePing(request),
                "complete" => HandleComplete(request),
                "call_tool" => await HandleToolCallAsync(request, auth, cancellationToken),
                "resource_list" => HandleResourceList(request),
                "resource_get" => await HandleResourceGetAsync(request, auth.AuthMode, cancellationToken),
                "resource_subscribe" => HandleResourceSubscribe(request),
                "resource_unsubscribe" => HandleResourceUnsubscribe(request),
                _ => BuildError(request, "unsupported_message", $"Unsupported MCP message type '{request.Type}'.")
            };

            logger.LogInformation("Focus legacy MCP {Type} handled for session {SessionId}", request.Type, response.SessionId);
            return Ok(response);
        }
        catch (FocusMcpInputException exception)
        {
            logger.LogWarning(exception, "Focus legacy MCP request {Type} failed", request.Type);
            return BadRequest(BuildError(request, "invalid_request", exception.Message, exception.Details));
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(exception, "Focus legacy MCP request {Type} failed", request.Type);
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

        await RunEventStreamAsync(sessionId, cancellationToken);
    }

    private async Task<IActionResult> HandleJsonRpcAsync(JsonElement payload, FocusMcpAuthorizationResult auth, CancellationToken cancellationToken)
    {
        if (payload.ValueKind == JsonValueKind.Object)
        {
            var response = await HandleJsonRpcMessageAsync(payload, auth, cancellationToken);
            return response is null ? Accepted() : Ok(response);
        }

        if (payload.ValueKind != JsonValueKind.Array)
        {
            return BadRequest(BuildJsonRpcError(default, -32600, "The request body must be a JSON-RPC object or batch."));
        }

        var responses = new JsonArray();
        foreach (var item in payload.EnumerateArray())
        {
            var response = await HandleJsonRpcMessageAsync(item, auth, cancellationToken);
            if (response is not null)
            {
                responses.Add(response);
            }
        }

        return responses.Count == 0 ? Accepted() : Ok(responses);
    }

    private async Task<JsonObject?> HandleJsonRpcMessageAsync(JsonElement message, FocusMcpAuthorizationResult auth, CancellationToken cancellationToken)
    {
        if (message.ValueKind != JsonValueKind.Object)
        {
            return BuildJsonRpcError(default, -32600, "Each JSON-RPC message must be an object.");
        }

        if (message.TryGetProperty("jsonrpc", out var versionElement)
            && versionElement.ValueKind != JsonValueKind.String
            || versionElement.ValueKind == JsonValueKind.String && !string.Equals(versionElement.GetString(), "2.0", StringComparison.Ordinal))
        {
            return BuildJsonRpcError(GetId(message), -32600, "Only JSON-RPC 2.0 is supported.");
        }

        if (!message.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var method = methodElement.GetString() ?? string.Empty;
        var hasId = message.TryGetProperty("id", out var id);
        var parameters = message.TryGetProperty("params", out var paramsElement) ? paramsElement : default;

        if (!hasId)
        {
            await HandleJsonRpcNotificationAsync(method, parameters, auth, cancellationToken);
            return null;
        }

        try
        {
            var result = method switch
            {
                "initialize" => HandleInitialize(auth, parameters),
                "ping" => new { },
                "tools/list" => HandleToolsList(),
                "tools/call" => await HandleJsonRpcToolCallAsync(parameters, auth, cancellationToken),
                "resources/list" => HandleResourcesList(),
                "resources/templates/list" => HandleResourceTemplatesList(),
                "resources/read" => await HandleJsonRpcResourceReadAsync(parameters, auth.AuthMode, cancellationToken),
                "resources/subscribe" => HandleJsonRpcResourceSubscribe(parameters),
                "resources/unsubscribe" => HandleJsonRpcResourceUnsubscribe(parameters),
                "completion/complete" => HandleJsonRpcCompletion(parameters),
                _ => throw new FocusMcpMethodException(-32601, $"Method '{method}' is not supported.")
            };

            return BuildJsonRpcResult(id, result);
        }
        catch (FocusMcpMethodException exception)
        {
            logger.LogWarning(exception, "Focus MCP JSON-RPC method {Method} failed", method);
            return BuildJsonRpcError(id, exception.Code, exception.Message, MergeErrorData(method, exception.ErrorData));
        }
        catch (FocusMcpInputException exception)
        {
            logger.LogWarning(exception, "Focus MCP JSON-RPC method {Method} failed", method);
            return BuildJsonRpcError(id, -32602, exception.Message, MergeErrorData(method, exception.Details));
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(exception, "Focus MCP JSON-RPC method {Method} failed", method);
            return BuildJsonRpcError(id, IsSessionError(exception.Message) ? -32001 : -32602, exception.Message, BuildErrorContext(method));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Focus MCP JSON-RPC method {Method} failed", method);
            return BuildJsonRpcError(id, -32603, "Internal server error.", new
            {
                context = BuildErrorContext(method),
                exception = exception.GetType().Name
            });
        }
    }

    private async Task HandleJsonRpcNotificationAsync(string method, JsonElement parameters, FocusMcpAuthorizationResult auth, CancellationToken cancellationToken)
    {
        switch (method)
        {
            case "notifications/initialized":
                EnsureProtocolSession();
                return;
            case "notifications/cancelled":
                return;
            default:
                if (!method.StartsWith("notifications/", StringComparison.Ordinal))
                {
                    throw new FocusMcpMethodException(-32601, $"Method '{method}' is not supported.");
                }

                return;
        }
    }

    private object HandleInitialize(FocusMcpAuthorizationResult auth, JsonElement parameters)
    {
        var input = Deserialize<FocusMcpJsonRpcInitializeParams>(parameters);
        Validate(input);

        var session = sessionService.CreateSession(
            new FocusMcpInitializeInput
            {
                ClientName = input.ClientInfo.Name,
                ClientVersion = input.ClientInfo.Version
            },
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            auth.AuthMode);

        Response.Headers[SessionHeaderName] = session.SessionId;

        return new
        {
            protocolVersion = StandardProtocolVersion,
            capabilities = new
            {
                tools = new { },
                resources = new { subscribe = true },
                completions = new { }
            },
            serverInfo = new
            {
                name = "Focus L-AIci MCP",
                version = typeof(McpController).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"
            },
            instructions = "Start with Focus. Search memories, read workspace context, review recent changes or tickets when relevant, then write back durable outcomes."
        };
    }

    private object HandleToolsList()
    {
        return new
        {
            tools = toolRegistry.GetTools().Select(descriptor => new
            {
                name = descriptor.Name,
                description = descriptor.Description,
                inputSchema = descriptor.InputSchema,
                annotations = new
                {
                    category = descriptor.Category,
                    readOnlyHint = !descriptor.Mutating
                }
            }).ToArray()
        };
    }

    private async Task<object> HandleJsonRpcToolCallAsync(JsonElement parameters, FocusMcpAuthorizationResult auth, CancellationToken cancellationToken)
    {
        EnsureProtocolSession();

        var input = Deserialize<FocusMcpToolCallInput>(parameters);
        Validate(input);
        if (!toolRegistry.TryGetDescriptor(input.Name, out var descriptor) || descriptor is null)
        {
            throw new FocusMcpMethodException(-32602, $"Unknown tool '{input.Name}'.");
        }

        if (descriptor.Mutating && !auth.CanWrite)
        {
            return new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = $"The MCP client '{auth.Label}' is read-only and cannot invoke mutating tools."
                    }
                },
                isError = true
            };
        }

        var result = await toolRegistry.InvokeAsync(input.Name, input.Arguments, cancellationToken);
        return new
        {
            content = new[]
            {
                new
                {
                    type = "text",
                    text = JsonSerializer.Serialize(result, SerializerOptions)
                }
            },
            structuredContent = result,
            isError = false
        };
    }

    private object HandleResourcesList()
    {
        return new
        {
            resources = resourceRegistry.GetResources()
                .Where(descriptor => !IsTemplateDescriptor(descriptor))
                .Select(descriptor => new
                {
                    uri = descriptor.Uri,
                    name = BuildResourceName(descriptor.Uri),
                    description = descriptor.Description,
                    mimeType = descriptor.MimeType
                })
                .ToArray()
        };
    }

    private object HandleResourceTemplatesList()
    {
        return new
        {
            resourceTemplates = resourceRegistry.GetResources()
                .Where(IsTemplateDescriptor)
                .Select(descriptor => new
                {
                    uriTemplate = descriptor.Uri,
                    name = BuildResourceName(descriptor.Uri),
                    description = descriptor.Description,
                    mimeType = descriptor.MimeType
                })
                .ToArray()
        };
    }

    private async Task<object> HandleJsonRpcResourceReadAsync(JsonElement parameters, string authMode, CancellationToken cancellationToken)
    {
        EnsureProtocolSession();

        var input = Deserialize<FocusMcpResourceGetInput>(parameters);
        Validate(input);
        var resource = await resourceRegistry.GetResourceAsync(input.Uri, authMode, cancellationToken);
        return new
        {
            contents = new[]
            {
                new
                {
                    uri = resource.Uri,
                    mimeType = resource.MimeType,
                    text = JsonSerializer.Serialize(resource.Data, SerializerOptions)
                }
            }
        };
    }

    private object HandleJsonRpcResourceSubscribe(JsonElement parameters)
    {
        var session = EnsureProtocolSession();
        var input = Deserialize<FocusMcpResourceSubscriptionInputSingle>(parameters);
        Validate(input);
        if (!resourceRegistry.IsKnownResource(input.Uri) && !input.Uri.EndsWith('*'))
        {
            throw new InvalidOperationException($"Unknown resource subscription: {input.Uri}");
        }

        sessionService.Subscribe(session.SessionId, [input.Uri]);
        return new { };
    }

    private object HandleJsonRpcResourceUnsubscribe(JsonElement parameters)
    {
        var session = EnsureProtocolSession();
        var input = Deserialize<FocusMcpResourceSubscriptionInputSingle>(parameters);
        Validate(input);
        sessionService.Unsubscribe(session.SessionId, [input.Uri]);
        return new { };
    }

    private object HandleJsonRpcCompletion(JsonElement parameters)
    {
        EnsureProtocolSession();

        var input = Deserialize<FocusMcpCompletionParams>(parameters);
        Validate(input);
        var values = input.Ref.Type switch
        {
            "ref/resource" => resourceRegistry.Complete(input.Argument.Value),
            _ => Array.Empty<string>()
        };

        return new
        {
            completion = new
            {
                values,
                total = values.Count,
                hasMore = false
            }
        };
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

    private async Task<FocusMcpResponseEnvelope> HandleToolCallAsync(FocusMcpRequestEnvelope request, FocusMcpAuthorizationResult auth, CancellationToken cancellationToken)
    {
        var session = EnsureSession(request.SessionId);
        var input = Deserialize<FocusMcpToolCallInput>(request.Payload);
        Validate(input);
        if (toolRegistry.TryGetDescriptor(input.Name, out var descriptor) && descriptor!.Mutating && !auth.CanWrite)
        {
            return BuildError(request, "unauthorized", $"The MCP client '{auth.Label}' is read-only and cannot invoke mutating tools.");
        }
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

    private bool TryAuthorize(out FocusMcpAuthorizationResult auth, out ObjectResult? unauthorized)
    {
        if (authService.TryAuthorize(HttpContext, out auth))
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
                Message = auth.ErrorMessage,
                Details = new
                {
                    authMode = auth.AuthMode,
                    authLabel = auth.Label,
                    canWrite = auth.CanWrite
                }
            }
        });
        return false;
    }

    private FocusMcpSessionSummaryViewModel EnsureProtocolSession()
    {
        var sessionId = TryGetSessionIdFromHeader();
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("An MCP session must be initialized first.");
        }

        return EnsureSession(sessionId);
    }

    private FocusMcpSessionSummaryViewModel EnsureSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new InvalidOperationException("An MCP session must be initialized first.");
        }

        if (!sessionService.TryTouch(sessionId, out var session))
        {
            throw new InvalidOperationException("That MCP session no longer exists. Reinitialize the MCP session and retry.");
        }

        return session;
    }

    private string TryGetSessionIdFromHeader()
    {
        return Request.Headers.TryGetValue(SessionHeaderName, out var values)
            ? values.ToString().Trim()
            : string.Empty;
    }

    private async Task RunEventStreamAsync(string sessionId, CancellationToken cancellationToken)
    {
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
                await WriteServerSentEventAsync("message", new
                {
                    jsonrpc = "2.0",
                    method = "notifications/resources/updated",
                    @params = new
                    {
                        uri = eventItem.ResourceUris.FirstOrDefault() ?? "focus://system/events"
                    }
                }, cancellationToken);
                continue;
            }

            await Response.WriteAsync(": keepalive\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
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

        try
        {
            return JsonSerializer.Deserialize<T>(payload.GetRawText(), SerializerOptions)
                ?? throw new FocusMcpInputException("Unable to parse the MCP payload.");
        }
        catch (JsonException exception)
        {
            throw new FocusMcpInputException(
                $"Unable to parse the MCP payload at '{exception.Path ?? "$"}'.",
                new
                {
                    path = exception.Path,
                    lineNumber = exception.LineNumber,
                    bytePositionInLine = exception.BytePositionInLine
                });
        }
    }

    private static void Validate(object instance)
    {
        var results = new List<ValidationResult>();
        var isValid = Validator.TryValidateObject(instance, new ValidationContext(instance), results, true);
        if (isValid)
        {
            return;
        }

        throw new FocusMcpInputException(
            "The MCP payload is invalid.",
            results
                .GroupBy(result => result.MemberNames.FirstOrDefault() ?? string.Empty)
                .ToDictionary(
                    group => string.IsNullOrWhiteSpace(group.Key) ? "$" : group.Key,
                    group => group.Select(result => result.ErrorMessage ?? "Invalid value.").ToArray()));
    }

    private static FocusMcpResponseEnvelope BuildError(FocusMcpRequestEnvelope request, string code, string message)
        => BuildError(request, code, message, null);

    private static FocusMcpResponseEnvelope BuildError(FocusMcpRequestEnvelope request, string code, string message, object? details)
    {
        return new FocusMcpResponseEnvelope
        {
            Id = request.Id,
            Type = "error",
            SessionId = request.SessionId,
            Error = new FocusMcpErrorPayload
            {
                Code = code,
                Message = message,
                Details = details
            }
        };
    }

    private static JsonObject BuildJsonRpcResult(JsonElement id, object result)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = ToJsonNode(id),
            ["result"] = JsonSerializer.SerializeToNode(result, SerializerOptions)
        };
    }

    private static JsonObject BuildJsonRpcError(JsonElement id, int code, string message, object? data = null)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
                ["data"] = data is null ? null : JsonSerializer.SerializeToNode(data, SerializerOptions)
            }
        };

        if (id.ValueKind != JsonValueKind.Undefined)
        {
            response["id"] = ToJsonNode(id);
        }

        return response;
    }

    private static JsonNode? ToJsonNode(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Undefined
            ? null
            : JsonNode.Parse(element.GetRawText());
    }

    private static JsonElement GetId(JsonElement message)
    {
        return message.TryGetProperty("id", out var id) ? id : default;
    }

    private object BuildErrorContext(string method)
    {
        return new
        {
            method,
            sessionId = TryGetSessionIdFromHeader(),
            traceIdentifier = HttpContext.TraceIdentifier
        };
    }

    private object MergeErrorData(string method, object? data)
    {
        return new
        {
            context = BuildErrorContext(method),
            details = data
        };
    }

    private static bool IsSessionError(string message)
        => message.Contains("session", StringComparison.OrdinalIgnoreCase);

    private static bool IsTemplateDescriptor(FocusMcpResourceDescriptor descriptor)
    {
        return descriptor.Uri.Contains('{');
    }

    private static string BuildResourceName(string uri)
    {
        var segment = uri.TrimEnd('}').TrimEnd('/').Split('/').LastOrDefault() ?? uri;
        return segment.Replace('{', ' ').Replace('}', ' ').Replace('-', ' ').Replace('_', ' ').Trim();
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class FocusMcpMethodException(int code, string message, object? errorData = null) : Exception(message)
    {
        public int Code { get; } = code;
        public object? ErrorData { get; } = errorData;
    }
}
