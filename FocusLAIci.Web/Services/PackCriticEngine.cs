using FocusLAIci.Web.Models;

namespace FocusLAIci.Web.Services;

public enum PackCritiqueAction
{
    Accept = 1,
    Repair = 2,
    Unsupported = 3
}

public sealed record PackCritiqueIssue(string Code, string Message);

public sealed record PackCritiqueDirective(
    IReadOnlyCollection<Guid>? GroundedSkillIds = null,
    IReadOnlyCollection<Guid>? GroundedMemoryIds = null,
    bool SuppressCodeGraph = false);

public sealed record PackCritiqueContext(
    string NormalizedQuestion,
    IReadOnlyCollection<string> SearchTokens,
    ContextPackViewModel CandidatePack,
    bool HasFacetRoute,
    bool AllowCodeGraph,
    int AttemptNumber);

public sealed record PackCritiqueResult(
    PackCritiqueAction Action,
    PackCritiqueDirective Directive,
    IReadOnlyCollection<PackCritiqueIssue> Issues);

public interface IPackCriticEngine
{
    PackCritiqueResult Evaluate(PackCritiqueContext context);
}

public sealed class PackCriticEngine : IPackCriticEngine
{
    private static readonly HashSet<string> LowSignalTokens =
    [
        "build", "check", "checks", "command", "commands", "computer", "computers", "create", "find", "help", "line",
        "list", "local", "machine", "machines", "make", "need", "pc", "pcs", "please", "powershell", "run", "script",
        "show", "tell", "use", "using", "windows", "will", "with"
    ];

    public PackCritiqueResult Evaluate(PackCritiqueContext context)
    {
        var groundedSkillIds = context.CandidatePack.RecommendedSkills
            .Where(skill => HasSpecificGrounding(
                BuildGroundingText(skill),
                context.SearchTokens,
                context.NormalizedQuestion,
                allowSingleSpecificTokenMatch: context.HasFacetRoute))
            .Select(skill => skill.Id)
            .ToArray();
        var groundedMemoryIds = context.CandidatePack.Memories
            .Where(memory => HasSpecificGrounding(BuildGroundingText(memory), context.SearchTokens, context.NormalizedQuestion))
            .Select(memory => memory.Id)
            .ToArray();
        var groundedTopMatchCount = context.CandidatePack.TopMatches
            .Count(match => HasSpecificGrounding(BuildGroundingText(match), context.SearchTokens, context.NormalizedQuestion));

        var issues = new List<PackCritiqueIssue>();
        if (context.CandidatePack.RecommendedSkills.Count > groundedSkillIds.Length
            && (!context.HasFacetRoute || groundedSkillIds.Length > 0))
        {
            issues.Add(new PackCritiqueIssue("ungrounded-skills", "Recommended skills do not overlap enough with the specific subject of the query."));
        }

        if (context.CandidatePack.Memories.Count > groundedMemoryIds.Length)
        {
            issues.Add(new PackCritiqueIssue("ungrounded-memories", "Retrieved memories do not overlap enough with the specific subject of the query."));
        }

        if (!context.AllowCodeGraph
            && (context.CandidatePack.CodeGraphProjects.Count > 0
                || context.CandidatePack.CodeGraphFiles.Count > 0
                || context.CandidatePack.CodeGraphNodes.Count > 0))
        {
            issues.Add(new PackCritiqueIssue("unexpected-codegraph", "Code graph results appeared for a non-code request."));
        }

        var hasRetrievalContent =
            context.CandidatePack.TopMatches.Count > 0
            || context.CandidatePack.RecommendedSkills.Count > 0
            || context.CandidatePack.Memories.Count > 0
            || context.CandidatePack.CodeGraphProjects.Count > 0
            || context.CandidatePack.CodeGraphFiles.Count > 0
            || context.CandidatePack.CodeGraphNodes.Count > 0;
        var hasGroundedSupport = groundedSkillIds.Length > 0 || groundedMemoryIds.Length > 0 || groundedTopMatchCount > 0;
        if (!context.HasFacetRoute && hasRetrievalContent && !hasGroundedSupport)
        {
            issues.Add(new PackCritiqueIssue("generic-overlap-only", "The candidate pack is driven by generic overlap instead of specific subject grounding."));
        }

        if (issues.Count == 0)
        {
            return new PackCritiqueResult(
                PackCritiqueAction.Accept,
                new PackCritiqueDirective(),
                Array.Empty<PackCritiqueIssue>());
        }

        if (context.AttemptNumber >= 2)
        {
            return new PackCritiqueResult(
                PackCritiqueAction.Unsupported,
                new PackCritiqueDirective(groundedSkillIds, groundedMemoryIds, SuppressCodeGraph: true),
                issues);
        }

        return new PackCritiqueResult(
            PackCritiqueAction.Repair,
            new PackCritiqueDirective(groundedSkillIds, groundedMemoryIds, SuppressCodeGraph: true),
            issues);
    }

    private static bool HasSpecificGrounding(
        string text,
        IReadOnlyCollection<string> queryTokens,
        string normalizedQuestion,
        bool allowSingleSpecificTokenMatch = false)
    {
        var specificTokens = queryTokens
            .Where(token => !LowSignalTokens.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var specificPhrases = BuildSpecificPhrases(normalizedQuestion).ToArray();
        if (specificTokens.Length == 0 && specificPhrases.Length == 0)
        {
            return false;
        }

        var normalizedText = Normalize(text);
        if (specificPhrases.Any(phrase => normalizedText.Contains(phrase, StringComparison.Ordinal)))
        {
            return true;
        }

        var textTokens = Tokenize(text);
        var overlap = specificTokens.Count(token => textTokens.Contains(token));
        return allowSingleSpecificTokenMatch
            ? overlap >= 1
            : specificTokens.Length == 1
            ? overlap == 1
            : overlap >= 2;
    }

    private static IEnumerable<string> BuildSpecificPhrases(string normalizedQuestion)
    {
        var tokens = normalizedQuestion
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => !LowSignalTokens.Contains(token))
            .ToArray();
        if (tokens.Length < 2)
        {
            return Array.Empty<string>();
        }

        var phrases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < tokens.Length - 1; index++)
        {
            phrases.Add($"{tokens[index]} {tokens[index + 1]}");
        }

        if (tokens.Length >= 3)
        {
            for (var index = 0; index < tokens.Length - 2; index++)
            {
                phrases.Add($"{tokens[index]} {tokens[index + 1]} {tokens[index + 2]}");
            }
        }

        return phrases;
    }

    private static string BuildGroundingText(ContextRecordViewModel record)
        => string.Join(' ', new[]
        {
            record.Title,
            record.Subtitle,
            record.Preview,
            record.MatchReason,
            string.Join(' ', record.Provenance?.MatchedTokens ?? Array.Empty<string>())
        });

    private static string BuildGroundingText(SkillCardViewModel skill)
        => string.Join(' ', new[]
        {
            skill.Name,
            skill.Summary,
            skill.TriggerHintsText,
            skill.RecommendationReason
        });

    private static string Normalize(string value)
        => string.Join(' ', value
            .ToLowerInvariant()
            .Split([' ', '\r', '\n', '\t', ',', '.', ':', ';', '/', '\\', '(', ')', '[', ']', '{', '}', '-', '_', '"', '\''], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static HashSet<string> Tokenize(string value)
        => value
            .ToLowerInvariant()
            .Split([' ', '\r', '\n', '\t', ',', '.', ':', ';', '/', '\\', '(', ')', '[', ']', '{', '}', '-', '_', '"', '\''], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 1)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
