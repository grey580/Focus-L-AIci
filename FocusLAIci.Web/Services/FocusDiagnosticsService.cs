using System.Text.Json;
using FocusLAIci.Web.Models;

namespace FocusLAIci.Web.Services;

public sealed class FocusDiagnosticsService(
    IHostEnvironment environment,
    FocusDatabaseTargetService databaseTargetService,
    FocusMcpSessionService sessionService,
    IServiceScopeFactory scopeFactory)
{
    private static readonly JsonElement EmptyArguments = JsonDocument.Parse("{}").RootElement.Clone();

    public async Task<FocusMcpSelfTestViewModel> RunMcpSelfTestAsync(string authMode, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var toolRegistry = scope.ServiceProvider.GetRequiredService<FocusMcpToolRegistry>();
        var resourceRegistry = scope.ServiceProvider.GetRequiredService<FocusMcpResourceRegistry>();
        var checks = new List<FocusMcpCheckResultViewModel>();

        async Task RecordAsync(string name, Func<Task<object?>> action)
        {
            try
            {
                var details = await action();
                checks.Add(new FocusMcpCheckResultViewModel
                {
                    Name = name,
                    Passed = true,
                    Summary = "Passed",
                    Details = details
                });
            }
            catch (Exception exception)
            {
                checks.Add(new FocusMcpCheckResultViewModel
                {
                    Name = name,
                    Passed = false,
                    Summary = exception.Message,
                    Details = new
                    {
                        exception = exception.GetType().Name
                    }
                });
            }
        }

        await RecordAsync("system-health-resource", async () =>
        {
            var health = await resourceRegistry.GetResourceAsync("focus://system/health", authMode, cancellationToken);
            return health.Data;
        });

        await RecordAsync("workspace-bootstrap-resource", async () =>
        {
            var workspace = await resourceRegistry.GetResourceAsync("focus://workspace/bootstrap", authMode, cancellationToken);
            return workspace.Data;
        });

        await RecordAsync("todo-board-tool", async () =>
            await toolRegistry.InvokeAsync("focus.todo.board", EmptyArguments, cancellationToken));

        await RecordAsync("memory-governance-tool", async () =>
            await toolRegistry.InvokeAsync("focus.memory.governance-queue", EmptyArguments, cancellationToken));

        var passedCount = checks.Count(x => x.Passed);
        var failedCount = checks.Count - passedCount;

        return new FocusMcpSelfTestViewModel
        {
            GeneratedUtc = DateTime.UtcNow,
            Status = failedCount == 0 ? "ready" : "degraded",
            Summary = failedCount == 0
                ? "Focus MCP is ready for initialize, discovery, context reads, and governance reads."
                : "Focus MCP is partially degraded. Review the failing checks before treating this instance as ready.",
            PassedCount = passedCount,
            FailedCount = failedCount,
            Checks = checks
        };
    }

    public async Task<FocusOperatorDiagnosticsViewModel> GetOperatorDiagnosticsAsync(string authMode, CancellationToken cancellationToken)
    {
        var sessions = sessionService.GetSessions();
        await using var scope = scopeFactory.CreateAsyncScope();
        var toolRegistry = scope.ServiceProvider.GetRequiredService<FocusMcpToolRegistry>();
        var resourceRegistry = scope.ServiceProvider.GetRequiredService<FocusMcpResourceRegistry>();
        return new FocusOperatorDiagnosticsViewModel
        {
            GeneratedUtc = DateTime.UtcNow,
            EnvironmentName = environment.EnvironmentName,
            ContentRootPath = environment.ContentRootPath,
            ApplicationVersion = typeof(FocusDiagnosticsService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
            AuthMode = authMode,
            ToolCount = toolRegistry.GetTools().Count,
            ResourceCount = resourceRegistry.GetResources().Count,
            ActiveSessionCount = sessions.Count,
            RecoveredSessionCount = sessions.Count(x => x.IsRecovered),
            PublishScriptPath = Path.Combine(environment.ContentRootPath, "scripts", "publish-focus-iis.ps1"),
            DatabaseTarget = databaseTargetService.GetCurrentTarget(),
            SelfTest = await RunMcpSelfTestAsync(authMode, cancellationToken)
        };
    }
}
