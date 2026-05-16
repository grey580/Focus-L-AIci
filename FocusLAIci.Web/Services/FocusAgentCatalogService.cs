using FocusLAIci.Web.Models;

namespace FocusLAIci.Web.Services;

public sealed class FocusAgentCatalogService
{
    private static readonly IReadOnlyCollection<FocusAgentDefinition> Definitions =
    [
        new(
            "context-agent",
            "Context Agent",
            "Builds a ranked task context pack from memories, tickets, todos, recent changes, and code graph signals before work starts.",
            "Reduce cold starts and missing-context mistakes by collecting the right Focus evidence first.",
            "Read-only retrieval",
            "Context packs, follow-up questions, and routing hints",
            false,
            ["cold starts", "task framing", "finding prior decisions", "routing work into the right wing or room"],
            ["Does not mutate Focus data.", "Ranks existing Focus context before suggesting action.", "Biases toward recent and pinned project memory."],
            ["Current task or question", "Optional wing, room, or goal hint"],
            ["Ranked context pack", "Suggested questions", "Recommended downstream agents and skills"],
            "Start with Focus. Build a context pack for this task before making changes.",
            ContextPackGoal.General,
            ["context", "memory", "bootstrap", "question", "cold start", "tickets", "todos", "recent changes"]),
        new(
            "research-agent",
            "Research Agent",
            "Synthesizes relevant memories, docs, code graph metadata, and related artifacts into a concise investigative brief.",
            "Turn scattered project evidence into a focused explanation before debugging or design work.",
            "Read-only investigation",
            "Investigative briefs, source lists, and distilled findings",
            false,
            ["incident triage", "documentation review", "design history", "cross-area investigations"],
            ["Stays read-only.", "Prefers durable Focus records over guesses.", "Highlights gaps instead of inventing missing facts."],
            ["Target question or subsystem", "Optional date, wing, or room scope"],
            ["Summarized findings", "Relevant evidence list", "Suggested next checks"],
            "Research this issue using Focus memories, recent changes, tickets, and related code graph context.",
            ContextPackGoal.Research,
            ["research", "investigate", "analyze", "docs", "history", "incident", "architecture"]),
        new(
            "execution-agent",
            "Execution Agent",
            "Runs bounded delivery workflows after context is established, such as builds, tests, exports, maintenance steps, and structured updates.",
            "Convert a known plan into a controlled sequence of concrete actions.",
            "Write-limited execution",
            "Executed steps, status updates, and operator-facing outcomes",
            true,
            ["running builds or tests", "structured maintenance", "repetitive delivery steps", "safe operational follow-through"],
            ["Should only run against a bounded plan.", "Avoids open-ended autonomy.", "Requires context first when the task is ambiguous."],
            ["Approved task or checklist", "Boundaries for what may change"],
            ["Completed step log", "Result summary", "Escalations when blocked"],
            "Execute this bounded task using Focus context, then report the outcome and any blockers.",
            ContextPackGoal.Delivery,
            ["execute", "run", "apply", "ship", "deliver", "build", "test", "maintenance"]),
        new(
            "review-agent",
            "Review Agent",
            "Reviews planned or completed work for regressions, missing wiring, risky assumptions, and local-first violations.",
            "Catch meaningful issues before a task is treated as done.",
            "Read-only review",
            "High-signal risks, gaps, and validation prompts",
            false,
            ["change review", "regression checks", "release readiness", "design sanity checks"],
            ["Focuses on meaningful risk instead of style nitpicks.", "Does not mutate state.", "Prefers exact file, memory, or ticket references."],
            ["Change summary, diff, or task plan", "Optional target files or subsystem"],
            ["Risk list", "Missing-wiring notes", "Suggested follow-up checks"],
            "Review this work for material regressions, missing wiring, and unsafe assumptions before finalizing it.",
            ContextPackGoal.Debugging,
            ["review", "regression", "risk", "validate", "check", "qa", "audit"])
    ];

    public IReadOnlyCollection<AgentCardViewModel> GetCatalog(string? query = null)
    {
        var normalizedQuery = query?.Trim() ?? string.Empty;
        IEnumerable<FocusAgentDefinition> agents = Definitions;
        if (!string.IsNullOrWhiteSpace(normalizedQuery))
        {
            agents = agents.Where(agent =>
                Contains(agent.Name, normalizedQuery)
                || Contains(agent.Summary, normalizedQuery)
                || Contains(agent.Mission, normalizedQuery)
                || agent.BestFor.Any(item => Contains(item, normalizedQuery))
                || agent.Keywords.Any(item => Contains(item, normalizedQuery)));
        }

        return agents.Select(agent => Map(agent)).ToArray();
    }

    public IReadOnlyCollection<AgentCardViewModel> GetFeaturedAgents()
        => Definitions.Select(agent => Map(agent)).ToArray();

    public AgentDetailViewModel? GetAgent(string slug)
    {
        var agent = Definitions.FirstOrDefault(x => string.Equals(x.Slug, slug, StringComparison.OrdinalIgnoreCase));
        return agent is null
            ? null
            : new AgentDetailViewModel
            {
                Agent = Map(agent),
                Inputs = agent.Inputs,
                Outputs = agent.Outputs,
                SuggestedPrompt = agent.SuggestedPrompt
            };
    }

    public IReadOnlyCollection<AgentCardViewModel> RecommendAgents(string? question, ContextPackGoal goal, int limit = 4)
    {
        var normalizedQuestion = question?.Trim() ?? string.Empty;
        var tokens = normalizedQuestion
            .Split([' ', ',', '.', ';', ':', '/', '\\', '-', '_', '(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Definitions
            .Select(agent =>
            {
                decimal score = agent.DefaultGoal == goal ? 5m : 0m;
                score += goal switch
                {
                    ContextPackGoal.General when agent.Slug is "context-agent" or "research-agent" => 2m,
                    ContextPackGoal.Debugging when agent.Slug is "research-agent" or "review-agent" => 2m,
                    ContextPackGoal.Delivery when agent.Slug is "execution-agent" or "review-agent" => 2m,
                    ContextPackGoal.Research when agent.Slug is "research-agent" or "context-agent" => 2m,
                    ContextPackGoal.Architecture when agent.Slug is "context-agent" or "review-agent" => 2m,
                    _ => 0m
                };

                foreach (var token in tokens)
                {
                    if (agent.Keywords.Any(keyword => string.Equals(keyword, token, StringComparison.OrdinalIgnoreCase)))
                    {
                        score += 2m;
                    }
                    else if (agent.BestFor.Any(item => item.Contains(token, StringComparison.OrdinalIgnoreCase))
                             || agent.Summary.Contains(token, StringComparison.OrdinalIgnoreCase)
                             || agent.Mission.Contains(token, StringComparison.OrdinalIgnoreCase))
                    {
                        score += 1m;
                    }
                }

                if (string.IsNullOrWhiteSpace(normalizedQuestion) && agent.Slug is "context-agent" or "research-agent")
                {
                    score += 2m;
                }

                return new { agent, score };
            })
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.agent.Name)
            .Take(Math.Clamp(limit, 1, 4))
            .Select(x => Map(x.agent, BuildRecommendationReason(x.agent, goal, normalizedQuestion, x.score), x.score))
            .ToArray();
    }

    private static AgentCardViewModel Map(FocusAgentDefinition agent, string recommendationReason = "", decimal recommendationScore = 0m)
    {
        return new AgentCardViewModel
        {
            Slug = agent.Slug,
            Name = agent.Name,
            Summary = agent.Summary,
            Mission = agent.Mission,
            ScopeLabel = agent.ScopeLabel,
            OutputLabel = agent.OutputLabel,
            SupportsWriteActions = agent.SupportsWriteActions,
            BestFor = agent.BestFor,
            Guardrails = agent.Guardrails,
            RecommendationReason = recommendationReason,
            RecommendationScore = recommendationScore,
            RecommendationScoreLabel = recommendationScore <= 0 ? string.Empty : $"{recommendationScore:0.#} fit"
        };
    }

    private static string BuildRecommendationReason(FocusAgentDefinition agent, ContextPackGoal goal, string question, decimal score)
    {
        if (score <= 0)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(question))
        {
            return $"Recommended for {goal.ToString().ToLowerInvariant()} work in Focus.";
        }

        return agent.Slug switch
        {
            "context-agent" => "Best first step when the task needs grounded Focus context.",
            "research-agent" => "Strong fit for synthesis and evidence gathering before acting.",
            "execution-agent" => "Best when the task already has a bounded plan and needs follow-through.",
            "review-agent" => "Best for catching risk and missing wiring before calling the task done.",
            _ => $"Recommended for {goal.ToString().ToLowerInvariant()} work."
        };
    }

    private static bool Contains(string text, string query)
        => text.Contains(query, StringComparison.OrdinalIgnoreCase);

    private sealed record FocusAgentDefinition(
        string Slug,
        string Name,
        string Summary,
        string Mission,
        string ScopeLabel,
        string OutputLabel,
        bool SupportsWriteActions,
        IReadOnlyCollection<string> BestFor,
        IReadOnlyCollection<string> Guardrails,
        IReadOnlyCollection<string> Inputs,
        IReadOnlyCollection<string> Outputs,
        string SuggestedPrompt,
        ContextPackGoal DefaultGoal,
        IReadOnlyCollection<string> Keywords);
}
