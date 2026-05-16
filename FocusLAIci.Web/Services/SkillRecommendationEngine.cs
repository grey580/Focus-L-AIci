using FocusLAIci.Web.Models;

namespace FocusLAIci.Web.Services;

internal static class SkillRecommendationEngine
{
    internal const int DefaultReviewWindowDays = 90;

    public static IReadOnlyCollection<SkillRecommendationMatch> Recommend(
        IEnumerable<SkillEntry> skills,
        string? question,
        Guid? wingId,
        SkillCategory? category,
        int limit)
    {
        var now = DateTime.UtcNow;
        var trimmedQuestion = question?.Trim() ?? string.Empty;
        var queryTokens = Tokenize(trimmedQuestion);
        var effectiveLimit = Math.Clamp(limit, 1, 12);
        var filteredSkills = skills
            .Where(skill => !category.HasValue || skill.Category == category.Value)
            .Where(skill => !wingId.HasValue || skill.WingId == wingId.Value || skill.WingId is null)
            .ToArray();

        if (filteredSkills.Length == 0)
        {
            return Array.Empty<SkillRecommendationMatch>();
        }

        var ranked = filteredSkills
            .Select(skill => ScoreSkill(skill, trimmedQuestion, queryTokens, wingId, now))
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Skill.IsPinned)
            .ThenByDescending(x => x.Skill.UseCount)
            .ThenByDescending(x => x.Skill.LastUsedUtc ?? DateTime.MinValue)
            .ThenBy(x => x.Skill.Name)
            .ToArray();

        if (queryTokens.Length > 0)
        {
            var matched = ranked.Where(x => x.Score > 0m).Take(effectiveLimit).ToArray();
            if (matched.Length > 0)
            {
                return matched;
            }
        }

        var fallback = ranked
            .Where(x => !NeedsReview(x.Skill, now) || x.Skill.IsPinned || x.Skill.UseCount > 0)
            .Take(effectiveLimit)
            .ToArray();
        return fallback.Length > 0
            ? fallback
            : ranked.Take(effectiveLimit).ToArray();
    }

    public static bool NeedsReview(SkillEntry skill, DateTime utcNow)
        => skill.ReviewAfterUtc.HasValue && skill.ReviewAfterUtc.Value <= utcNow;

    public static string GetReviewLabel(SkillEntry skill, DateTime utcNow)
    {
        if (!skill.ReviewAfterUtc.HasValue)
        {
            return "Review window not set";
        }

        if (NeedsReview(skill, utcNow))
        {
            return $"Review due since {skill.ReviewAfterUtc.Value:yyyy-MM-dd}";
        }

        return $"Review by {skill.ReviewAfterUtc.Value:yyyy-MM-dd}";
    }

    public static string BuildSuggestedQuestion(SkillEntry skill)
    {
        var example = SplitLines(skill.ExamplesText).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(example))
        {
            return example;
        }

        var triggerHints = SplitInline(skill.TriggerHintsText).Take(4).ToArray();
        if (triggerHints.Length > 0)
        {
            return $"{skill.Name}: {string.Join(", ", triggerHints)}";
        }

        return $"{skill.Name}: {skill.Summary}".Trim();
    }

    private static SkillRecommendationMatch ScoreSkill(
        SkillEntry skill,
        string question,
        IReadOnlyCollection<string> queryTokens,
        Guid? wingId,
        DateTime now)
    {
        var nameTokens = Tokenize(skill.Name);
        var summaryTokens = Tokenize(skill.Summary);
        var guidanceTokens = Tokenize($"{skill.WhenToUse} {skill.Flow}");
        var triggerTokens = Tokenize(skill.TriggerHintsText);
        var combinedText = $"{skill.Name} {skill.Summary} {skill.WhenToUse} {skill.Flow} {skill.TriggerHintsText}";

        decimal score = 0m;
        var reasons = new List<string>();

        if (!string.IsNullOrWhiteSpace(question))
        {
            if (ContainsPhrase(skill.Name, question))
            {
                score += 32m;
                reasons.Add("name");
            }

            if (ContainsPhrase(skill.TriggerHintsText, question))
            {
                score += 24m;
                reasons.Add("trigger hints");
            }

            if (ContainsPhrase(skill.Summary, question) || ContainsPhrase(skill.WhenToUse, question))
            {
                score += 18m;
                reasons.Add("guidance");
            }

            if (ContainsPhrase(combinedText, question))
            {
                score += 10m;
            }
        }

        var nameHits = CountOverlap(queryTokens, nameTokens);
        var triggerHits = CountOverlap(queryTokens, triggerTokens);
        var summaryHits = CountOverlap(queryTokens, summaryTokens);
        var guidanceHits = CountOverlap(queryTokens, guidanceTokens);

        if (nameHits > 0)
        {
            score += nameHits * 10m;
            reasons.Add("name");
        }

        if (triggerHits > 0)
        {
            score += triggerHits * 8m;
            reasons.Add("trigger hints");
        }

        if (summaryHits > 0)
        {
            score += summaryHits * 5m;
            reasons.Add("summary");
        }

        if (guidanceHits > 0)
        {
            score += guidanceHits * 3m;
            reasons.Add("flow");
        }

        if (wingId.HasValue && skill.WingId == wingId.Value)
        {
            score += 9m;
            reasons.Add("wing");
        }

        if (skill.IsPinned)
        {
            score += 2m;
        }

        if (skill.UseCount > 0)
        {
            score += Math.Min(skill.UseCount, 8) * 0.75m;
            reasons.Add("usage");
        }

        if (NeedsReview(skill, now))
        {
            score -= 8m;
        }

        if (queryTokens.Count == 0)
        {
            score += skill.IsPinned ? 6m : 0m;
            score += Math.Min(skill.UseCount, 8);
        }

        var reason = BuildReason(reasons, skill, wingId, now);
        return new SkillRecommendationMatch(skill, Math.Round(score, 2), reason);
    }

    private static string BuildReason(IReadOnlyCollection<string> reasons, SkillEntry skill, Guid? wingId, DateTime now)
    {
        if (reasons.Contains("name") && reasons.Contains("trigger hints"))
        {
            return "Name and trigger hints line up with this task.";
        }

        if (reasons.Contains("name"))
        {
            return "Name matches the task wording.";
        }

        if (reasons.Contains("trigger hints"))
        {
            return "Trigger hints line up with this task.";
        }

        if (reasons.Contains("guidance") || reasons.Contains("flow") || reasons.Contains("summary"))
        {
            return "Usage guidance overlaps the task.";
        }

        if (wingId.HasValue && skill.WingId == wingId.Value)
        {
            return "Pinned for the same wing.";
        }

        if (skill.UseCount > 0)
        {
            return NeedsReview(skill, now)
                ? "Frequently opened, but due for review."
                : "Frequently opened reusable workflow.";
        }

        return NeedsReview(skill, now)
            ? "Pinned fallback, but due for review."
            : "Pinned reusable workflow for cold start work.";
    }

    private static int CountOverlap(IEnumerable<string> left, IEnumerable<string> right)
    {
        var rightSet = right.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return left.Count(token => rightSet.Contains(token));
    }

    private static bool ContainsPhrase(string? text, string phrase)
        => !string.IsNullOrWhiteSpace(text) &&
           !string.IsNullOrWhiteSpace(phrase) &&
           text.Contains(phrase, StringComparison.OrdinalIgnoreCase);

    private static string[] Tokenize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return value
            .ToLowerInvariant()
            .Split([' ', '\r', '\n', '\t', ',', '.', ':', ';', '/', '\\', '(', ')', '[', ']', '{', '}', '-', '_', '"', '\''], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(token => token)
            .ToArray();
    }

    private static IReadOnlyCollection<string> SplitLines(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IReadOnlyCollection<string> SplitInline(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}

internal sealed record SkillRecommendationMatch(SkillEntry Skill, decimal Score, string Reason);
