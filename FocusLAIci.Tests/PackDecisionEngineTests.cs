using FocusLAIci.Web.Services;

namespace FocusLAIci.Tests;

public sealed class PackDecisionEngineTests
{
    private static readonly PackDecisionEngine Engine = new();

    [Fact]
    public void EvaluateQuery_ClarifiesThinAmbiguousPrompts()
    {
        var prediction = TinyLocalPackIntentModel.Shared.Predict("powershell script");

        var decision = Engine.EvaluateQuery(prediction, hasTokens: true, hasStrongDomainSignals: false);

        Assert.Equal(PackDecisionKind.Clarify, decision.Kind);
        Assert.True(decision.Scorecard.QueryNeedsMoreContext);
        Assert.Contains(PackDecisionCause.InsufficientContext, decision.Scorecard.EffectiveCauses);
    }

    [Fact]
    public void EvaluateRetrieval_UsesUnsupportedWhenConcreteRouteHasNoSupport()
    {
        var prediction = TinyLocalPackIntentModel.Shared.Predict("Check ADMT forest DNS forwarder issues affecting trust.");

        var decision = Engine.EvaluateRetrieval(
            prediction,
            topMatchCount: 0,
            recommendedSkillCount: 0,
            retrievalAgreementRatio: null,
            specificGroundingRatio: null,
            hasFacetRoute: false);

        Assert.Equal(PackDecisionKind.Unsupported, decision.Kind);
        Assert.Equal(0, decision.Scorecard.RetrievalYield);
        Assert.Contains(PackDecisionCause.MissingFamily, decision.Scorecard.EffectiveCauses);
    }

    [Fact]
    public void EvaluateRetrieval_ClarifiesWhenAmbiguousRoutingAndSkillAgreementAreWeak()
    {
        var prediction = new PackIntentPrediction(
            ExternalOperationsScore: 0.58m,
            DirectoryAdminScore: 0.55m,
            CodeIntentScore: 0.10m,
            GenericAutomationScore: 0.14m,
            RepositoryArchitectureScore: 0.11m,
            ModelId: "test",
            ObservedTokenCount: 4,
            InformativeTokenCount: 2,
            SpecificInformativeTokenCount: 1,
            HasTaskFrame: true,
            FacetSignalCount: 0);

        var decision = Engine.EvaluateRetrieval(
            prediction,
            topMatchCount: 0,
            recommendedSkillCount: 2,
            retrievalAgreementRatio: 0.33m,
            specificGroundingRatio: 0.60m,
            hasFacetRoute: false);

        Assert.Equal(PackDecisionKind.Clarify, decision.Kind);
        Assert.Contains(PackDecisionCause.NearNeighborCollision, decision.Scorecard.EffectiveCauses);
    }

    [Fact]
    public void EvaluateRetrieval_RejectsBroadRetrievalWithoutSpecificGrounding()
    {
        var prediction = TinyLocalPackIntentModel.Shared.Predict("make a powershell that will check for missing windows updates on a pc");

        var decision = Engine.EvaluateRetrieval(
            prediction,
            topMatchCount: 3,
            recommendedSkillCount: 2,
            retrievalAgreementRatio: 0.50m,
            specificGroundingRatio: 0.17m,
            hasFacetRoute: false);

        Assert.Equal(PackDecisionKind.Unsupported, decision.Kind);
        Assert.Contains(PackDecisionCause.RetrievalPollution, decision.Scorecard.EffectiveCauses);
    }
}
