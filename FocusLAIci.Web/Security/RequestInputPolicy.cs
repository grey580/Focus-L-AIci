using FocusLAIci.Web.Models;

namespace FocusLAIci.Web.Security;

public static class RequestInputPolicy
{
    public const int MinResultsPerSection = 3;
    public const int MaxResultsPerSection = 10;
    public const int DefaultResultsPerSection = 6;
    public const int MaxQuestionLength = 400;

    public static ContextBriefInput NormalizeBoundContextBriefInput(ContextBriefInput input)
    {
        return new ContextBriefInput
        {
            Question = input.Question.Trim(),
            IncludeCompletedWork = input.IncludeCompletedWork,
            ExpandHistory = input.ExpandHistory,
            ResultsPerSection = Math.Clamp(input.ResultsPerSection, MinResultsPerSection, MaxResultsPerSection),
            PackGoal = Enum.IsDefined(input.PackGoal) ? input.PackGoal : ContextPackGoal.General
        };
    }

    public static bool TryCreateOptionalContextBriefInput(
        string? question,
        bool includeCompletedWork,
        bool expandHistory,
        int? resultsPerSection,
        out ContextBriefInput? input,
        out string? error)
    {
        input = null;
        error = null;

        var hasContextOverrides =
            !string.IsNullOrWhiteSpace(question) ||
            includeCompletedWork ||
            !expandHistory ||
            resultsPerSection.HasValue;

        if (!hasContextOverrides)
        {
            return true;
        }

        var normalizedQuestion = question?.Trim() ?? string.Empty;
        if (normalizedQuestion.Length > MaxQuestionLength)
        {
            error = $"Question cannot exceed {MaxQuestionLength} characters.";
            return false;
        }

        if (resultsPerSection.HasValue &&
            (resultsPerSection.Value < MinResultsPerSection || resultsPerSection.Value > MaxResultsPerSection))
        {
            error = $"ResultsPerSection must be between {MinResultsPerSection} and {MaxResultsPerSection}.";
            return false;
        }

        input = new ContextBriefInput
        {
            Question = normalizedQuestion,
            IncludeCompletedWork = includeCompletedWork,
            ExpandHistory = expandHistory,
            ResultsPerSection = resultsPerSection.GetValueOrDefault(DefaultResultsPerSection)
        };

        return true;
    }
}
