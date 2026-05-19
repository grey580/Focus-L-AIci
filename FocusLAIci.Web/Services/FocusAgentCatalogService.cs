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
            "What context should I gather in Focus before I start this task?",
            ContextPackGoal.General,
            ["context", "memory", "bootstrap", "question", "cold start", "tickets", "todos", "recent changes"]),
        new(
            "triage-agent",
            "Triage Agent",
            "Turns raw asks, notes, and backlog noise into routed, deduped, prioritized Focus work before execution begins.",
            "Give Focus a front door that normalizes intake and routes work into the right system-of-record path.",
            "Write-limited intake",
            "Canonical work statements, duplicate flags, and priority-backed next steps",
            true,
            ["routing a new request", "deduping overlapping work", "prioritizing backlog intake", "turning raw notes into tickets or todos"],
            ["Preserves the original input instead of rewriting history.", "Flags duplicates and priority with explicit rationale.", "Does not auto-close or silently re-route work."],
            ["Raw request, note, or issue summary", "Optional target wing, room, or urgency hint"],
            ["Canonical problem statement", "Duplicate candidates and routing hints", "Suggested tickets, todos, or next agent"],
            "Triage this raw work item in Focus, route it to the right place, and produce the next bounded actions.",
            "How should I triage, route, and prioritize this new work inside Focus?",
            ContextPackGoal.General,
            ["triage", "intake", "prioritize", "route", "dedupe", "backlog", "inbox", "request"]),
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
            "What does Focus already know about this issue, subsystem, or investigation?",
            ContextPackGoal.Research,
            ["research", "investigate", "analyze", "docs", "history", "incident", "architecture"]),
        new(
            "impact-agent",
            "Impact Agent",
            "Maps likely blast radius, dependencies, and validation targets before a change, fix, or migration starts.",
            "Use Focus code graph and recent work to name what a task could touch before execution begins.",
            "Read-only impact analysis",
            "Blast-radius maps, risk checklists, and validation targets",
            false,
            ["pre-change risk mapping", "finding affected files or rooms", "validation planning", "dependency-aware scoping"],
            ["Shows evidence and confidence instead of pretending the graph is complete.", "Calls out unknowns when coverage is thin.", "Does not mutate Focus state or run changes."],
            ["Proposed change, fix, or subsystem", "Optional file, wing, or room scope"],
            ["Likely impact map", "Risk-ranked validation checklist", "Suggested follow-on agents or skills"],
            "Analyze the likely impact of this change using Focus context, code graph signals, and recent changes before execution.",
            "What is the likely blast radius, risk, and validation plan for this change?",
            ContextPackGoal.Architecture,
            ["impact", "blast-radius", "dependencies", "validation", "risk", "graph", "touchpoints", "migration"]),
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
            "What bounded delivery steps should I execute once the Focus context is clear?",
            ContextPackGoal.Delivery,
            ["execute", "run", "apply", "ship", "deliver", "build", "test", "maintenance"]),
        new(
            "curation-agent",
            "Curation Agent",
            "Turns finished work into durable Focus knowledge and keeps memories from drifting, duplicating, or going stale.",
            "Keep Focus trustworthy by capturing durable outcomes, proposing merges, and refreshing stale context after work ships.",
            "Write-limited curation",
            "Memory candidates, merge suggestions, and freshness updates",
            true,
            ["capturing shipped decisions", "refreshing bootstrap context", "deduping overlapping memories", "turning task outcomes into durable knowledge"],
            ["Only promotes durable facts, not transient chatter.", "Proposes merges or retirements with evidence.", "Avoids broad cleanup without a bounded source task."],
            ["Completed task summary or outcome", "Optional related ticket, todo, or memory scope"],
            ["Durable memory candidates", "Merge or retire suggestions", "Updated knowledge follow-up plan"],
            "Curate the durable outcome of this work into Focus memories, merges, and freshness updates.",
            "What durable knowledge should I write back into Focus after this work is done?",
            ContextPackGoal.Delivery,
            ["curation", "memory", "merge", "canonical", "bootstrap", "durable", "freshness", "supersede"]),
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
            "What should I review for risk, missing wiring, or unsafe assumptions before this work is done?",
            ContextPackGoal.Debugging,
            ["review", "regression", "risk", "validate", "check", "qa", "audit"])
    ];

    public IReadOnlyCollection<AgentCardViewModel> GetCatalog(string? query = null, ContextPackGoal? goal = null, bool supportsWriteActionsOnly = false)
    {
        var normalizedQuery = query?.Trim() ?? string.Empty;
        IEnumerable<FocusAgentDefinition> agents = Definitions;
        if (goal.HasValue)
        {
            agents = agents.Where(agent => agent.DefaultGoal == goal.Value);
        }

        if (supportsWriteActionsOnly)
        {
            agents = agents.Where(agent => agent.SupportsWriteActions);
        }

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
                SuggestedPrompt = agent.SuggestedPrompt,
                SuggestedQuestion = agent.SuggestedQuestion
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
                    ContextPackGoal.General when agent.Slug is "context-agent" or "triage-agent" or "research-agent" => 2m,
                    ContextPackGoal.Debugging when agent.Slug is "research-agent" or "impact-agent" or "review-agent" => 2m,
                    ContextPackGoal.Delivery when agent.Slug is "execution-agent" or "curation-agent" or "review-agent" => 2m,
                    ContextPackGoal.Research when agent.Slug is "research-agent" or "context-agent" or "impact-agent" => 2m,
                    ContextPackGoal.Architecture when agent.Slug is "context-agent" or "impact-agent" or "review-agent" => 2m,
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

                if (string.IsNullOrWhiteSpace(normalizedQuestion) && agent.Slug is "context-agent" or "triage-agent" or "research-agent")
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
            DefaultGoal = agent.DefaultGoal,
            DefaultGoalLabel = agent.DefaultGoal.ToString(),
            SupportsWriteActions = agent.SupportsWriteActions,
            BestFor = agent.BestFor,
            Guardrails = agent.Guardrails,
            KeywordHints = agent.Keywords.Take(6).ToArray(),
            RecommendationReason = recommendationReason,
            RecommendationScore = recommendationScore,
            RecommendationScoreLabel = recommendationScore <= 0 ? string.Empty : $"{recommendationScore:0.#} fit",
            SuggestedQuestion = agent.SuggestedQuestion
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
            "triage-agent" => "Best when raw work needs routing, dedupe checks, and priority before deeper analysis.",
            "research-agent" => "Strong fit for synthesis and evidence gathering before acting.",
            "impact-agent" => "Best for naming blast radius, risk, and validation scope before making the change.",
            "execution-agent" => "Best when the task already has a bounded plan and needs follow-through.",
            "curation-agent" => "Best for turning completed work into durable Focus knowledge and cleanup actions.",
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
        string SuggestedQuestion,
        ContextPackGoal DefaultGoal,
        IReadOnlyCollection<string> Keywords);
}
