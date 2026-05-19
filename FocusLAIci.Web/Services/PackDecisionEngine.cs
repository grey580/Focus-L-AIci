namespace FocusLAIci.Web.Services;

public enum PackDecisionKind
{
    Proceed = 1,
    Clarify = 2,
    Unsupported = 3
}

public enum PackDecisionCause
{
    InsufficientContext = 1,
    MissingFamily = 2,
    NearNeighborCollision = 3,
    RetrievalPollution = 4
}

public sealed record PackDecisionScorecard(
    decimal TopScore,
    decimal TopMargin,
    bool IsAmbiguous,
    bool QueryNeedsMoreContext,
    int InformativeTokenCount,
    int SpecificInformativeTokenCount,
    int FacetSignalCount,
    int TopMatchCount = 0,
    int RecommendedSkillCount = 0,
    int RetrievalYield = 0,
    decimal? RetrievalAgreementRatio = null,
    decimal? SpecificGroundingRatio = null,
    IReadOnlyCollection<PackDecisionCause>? Causes = null,
    IReadOnlyCollection<string>? Reasons = null)
{
    public IReadOnlyCollection<PackDecisionCause> EffectiveCauses => Causes ?? Array.Empty<PackDecisionCause>();
    public IReadOnlyCollection<string> EffectiveReasons => Reasons ?? Array.Empty<string>();
}

public sealed record PackDecision(PackDecisionKind Kind, PackDecisionScorecard Scorecard)
{
    public bool ShouldProceed => Kind == PackDecisionKind.Proceed;
}

public interface IPackDecisionEngine
{
    PackDecision EvaluateQuery(
        PackIntentPrediction prediction,
        bool hasTokens,
        bool hasStrongDomainSignals);

    PackDecision EvaluateRetrieval(
        PackIntentPrediction prediction,
        int topMatchCount,
        int recommendedSkillCount,
        decimal? retrievalAgreementRatio,
        decimal? specificGroundingRatio,
        bool hasFacetRoute);
}

public sealed class PackDecisionEngine : IPackDecisionEngine
{
    public PackDecision EvaluateQuery(
        PackIntentPrediction prediction,
        bool hasTokens,
        bool hasStrongDomainSignals)
    {
        var reasons = new List<string>();
        var causes = new List<PackDecisionCause>();
        if (!hasTokens)
        {
            reasons.Add("No query tokens were available to route.");
            causes.Add(PackDecisionCause.InsufficientContext);
            return new PackDecision(
                PackDecisionKind.Clarify,
                BuildScorecard(prediction, causes, reasons));
        }

        if (prediction.NeedsMoreContext && !hasStrongDomainSignals)
        {
            reasons.Add("The query does not provide enough grounded signal to route safely.");
            causes.Add(PackDecisionCause.InsufficientContext);
            if (prediction.IsAmbiguous)
            {
                reasons.Add("The routed family is ambiguous.");
                causes.Add(PackDecisionCause.NearNeighborCollision);
            }

            if (prediction.FacetSignalCount == 0)
            {
                reasons.Add("No concrete task facet was detected.");
                if (prediction.SpecificInformativeTokenCount >= 2)
                {
                    causes.Add(PackDecisionCause.MissingFamily);
                }
            }

            return new PackDecision(
                PackDecisionKind.Clarify,
                BuildScorecard(prediction, causes, reasons));
        }

        reasons.Add("The query has enough signal to attempt retrieval.");
        return new PackDecision(
            PackDecisionKind.Proceed,
            BuildScorecard(prediction, Array.Empty<PackDecisionCause>(), reasons));
    }

    public PackDecision EvaluateRetrieval(
        PackIntentPrediction prediction,
        int topMatchCount,
        int recommendedSkillCount,
        decimal? retrievalAgreementRatio,
        decimal? specificGroundingRatio,
        bool hasFacetRoute)
    {
        var reasons = new List<string>();
        var causes = new List<PackDecisionCause>();
        var retrievalYield = topMatchCount + recommendedSkillCount;
        if (retrievalYield == 0)
        {
            reasons.Add("No grounded retrieval support was found for the routed request.");
            var kind = prediction.FacetSignalCount > 0
                       || prediction.SpecificInformativeTokenCount >= 2
                       || prediction.TopScore >= 0.60m
                ? PackDecisionKind.Unsupported
                : PackDecisionKind.Clarify;
            causes.Add(kind == PackDecisionKind.Unsupported
                ? PackDecisionCause.MissingFamily
                : PackDecisionCause.InsufficientContext);

            return new PackDecision(
                kind,
                BuildScorecard(
                    prediction,
                    causes,
                    reasons,
                    topMatchCount,
                    recommendedSkillCount,
                    retrievalYield,
                    retrievalAgreementRatio,
                    specificGroundingRatio));
        }

        var requiresAgreement =
            !hasFacetRoute
            && (prediction.IsExternalOperationsQuery
                || prediction.IsDirectoryAdminQuery
                || prediction.IsGenericAutomationQuery
                || prediction.IsRepositoryArchitectureQuery);
        if (requiresAgreement
            && specificGroundingRatio.HasValue
            && specificGroundingRatio.Value < 0.40m)
        {
            reasons.Add("Retrieved results do not overlap enough with the query's specific subject matter.");
            causes.Add(PackDecisionCause.RetrievalPollution);
            if (prediction.IsAmbiguous)
            {
                causes.Add(PackDecisionCause.NearNeighborCollision);
            }
            return new PackDecision(
                prediction.SpecificInformativeTokenCount >= 2 || prediction.TopScore >= 0.60m
                    ? PackDecisionKind.Unsupported
                    : PackDecisionKind.Clarify,
                BuildScorecard(
                    prediction,
                    causes,
                    reasons,
                    topMatchCount,
                    recommendedSkillCount,
                    retrievalYield,
                    retrievalAgreementRatio,
                    specificGroundingRatio));
        }

        if (requiresAgreement
            && retrievalAgreementRatio.HasValue
            && retrievalAgreementRatio.Value < 0.40m
            && prediction.IsAmbiguous
            && topMatchCount == 0)
        {
            reasons.Add("Retrieved skills did not agree strongly enough with the routed family.");
            causes.Add(PackDecisionCause.NearNeighborCollision);
            return new PackDecision(
                PackDecisionKind.Clarify,
                BuildScorecard(
                    prediction,
                    causes,
                    reasons,
                    topMatchCount,
                    recommendedSkillCount,
                    retrievalYield,
                    retrievalAgreementRatio,
                    specificGroundingRatio));
        }

        reasons.Add("Query routing and retrieval support are strong enough to proceed.");
        return new PackDecision(
            PackDecisionKind.Proceed,
            BuildScorecard(
                prediction,
                Array.Empty<PackDecisionCause>(),
                reasons,
                topMatchCount,
                recommendedSkillCount,
                retrievalYield,
                retrievalAgreementRatio,
                specificGroundingRatio));
    }

    private static PackDecisionScorecard BuildScorecard(
        PackIntentPrediction prediction,
        IReadOnlyCollection<PackDecisionCause> causes,
        IReadOnlyCollection<string> reasons,
        int topMatchCount = 0,
        int recommendedSkillCount = 0,
        int retrievalYield = 0,
        decimal? retrievalAgreementRatio = null,
        decimal? specificGroundingRatio = null)
        => new(
            prediction.TopScore,
            prediction.TopMargin,
            prediction.IsAmbiguous,
            prediction.NeedsMoreContext,
            prediction.InformativeTokenCount,
            prediction.SpecificInformativeTokenCount,
            prediction.FacetSignalCount,
            topMatchCount,
            recommendedSkillCount,
            retrievalYield,
            retrievalAgreementRatio,
            specificGroundingRatio,
            causes
                .Distinct()
                .ToArray(),
            reasons);
}
