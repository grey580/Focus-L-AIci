using FocusLAIci.Web.Models;
using FocusLAIci.Web.Services;

namespace FocusLAIci.Tests;

public sealed class PackCriticEngineTests
{
    private static readonly PackCriticEngine Engine = new();

    [Fact]
    public void Evaluate_RepairsUngroundedCandidateOnFirstAttempt()
    {
        var pack = new ContextPackViewModel
        {
            Question = "make a powershell that will audit installed printer drivers on a pc",
            SearchTokens = ["audit", "drivers", "installed", "pc", "powershell", "printer", "will"],
            RecommendedSkills =
            [
                new SkillCardViewModel
                {
                    Id = Guid.NewGuid(),
                    Name = "Check WMI health on a Windows PC",
                    Slug = "check-wmi-health-on-a-windows-pc",
                    Summary = "Check WMI and CIM health.",
                    TriggerHintsText = "wmi, windows, pc, powershell"
                }
            ],
            Memories =
            [
                new ContextRecordViewModel
                {
                    Kind = ContextRecordKind.Memory,
                    Id = Guid.NewGuid(),
                    Title = "Grey Canary endpoint bootstrap depends on PowerShell",
                    Preview = "Bootstrap and service prerequisites.",
                    MatchReason = "Title shares your search terms."
                }
            ]
        };

        var critique = Engine.Evaluate(new PackCritiqueContext(
            "make a powershell that will audit installed printer drivers on a pc",
            pack.SearchTokens,
            pack,
            HasFacetRoute: false,
            AllowCodeGraph: false,
            AttemptNumber: 1));

        Assert.Equal(PackCritiqueAction.Repair, critique.Action);
        Assert.NotEmpty(critique.Issues);
    }

    [Fact]
    public void Evaluate_ReturnsUnsupportedOnSecondFailedAttempt()
    {
        var pack = new ContextPackViewModel
        {
            Question = "make a powershell that will audit installed printer drivers on a pc",
            SearchTokens = ["audit", "drivers", "installed", "pc", "powershell", "printer", "will"],
            RecommendedSkills =
            [
                new SkillCardViewModel
                {
                    Id = Guid.NewGuid(),
                    Name = "Check whether a port is open with PowerShell",
                    Slug = "check-whether-a-port-is-open-with-powershell",
                    Summary = "Check TCP or UDP ports.",
                    TriggerHintsText = "powershell, port, tcp, udp, windows"
                }
            ]
        };

        var critique = Engine.Evaluate(new PackCritiqueContext(
            "make a powershell that will audit installed printer drivers on a pc",
            pack.SearchTokens,
            pack,
            HasFacetRoute: false,
            AllowCodeGraph: false,
            AttemptNumber: 2));

        Assert.Equal(PackCritiqueAction.Unsupported, critique.Action);
        Assert.NotEmpty(critique.Issues);
    }

    [Fact]
    public void Evaluate_AcceptsWellGroundedPackWithNoIssues()
    {
        var pack = new ContextPackViewModel
        {
            Question = "check wmi health on a windows pc",
            SearchTokens = ["check", "health", "pc", "wmi", "windows"],
            RecommendedSkills =
            [
                new SkillCardViewModel
                {
                    Id = Guid.NewGuid(),
                    Name = "Check WMI health on a Windows PC",
                    Slug = "check-wmi-health-on-a-windows-pc",
                    Summary = "Check WMI and CIM health end to end.",
                    TriggerHintsText = "wmi, windows, pc, health"
                }
            ],
            Memories =
            [
                new ContextRecordViewModel
                {
                    Kind = ContextRecordKind.Memory,
                    Id = Guid.NewGuid(),
                    Title = "WMI health check notes",
                    Preview = "Notes about WMI diagnostics on Windows machines.",
                    MatchReason = "Title shares your search terms."
                }
            ]
        };

        var critique = Engine.Evaluate(new PackCritiqueContext(
            "check wmi health on a windows pc",
            pack.SearchTokens,
            pack,
            HasFacetRoute: false,
            AllowCodeGraph: false,
            AttemptNumber: 1));

        Assert.Equal(PackCritiqueAction.Accept, critique.Action);
        Assert.Empty(critique.Issues);
    }

    [Fact]
    public void Evaluate_AcceptsEmptyPackWithNoRetrievalContent()
    {
        var pack = new ContextPackViewModel
        {
            Question = "something with no matches at all",
            SearchTokens = ["something", "matches"]
        };

        var critique = Engine.Evaluate(new PackCritiqueContext(
            "something with no matches at all",
            pack.SearchTokens,
            pack,
            HasFacetRoute: false,
            AllowCodeGraph: false,
            AttemptNumber: 1));

        Assert.Equal(PackCritiqueAction.Accept, critique.Action);
        Assert.Empty(critique.Issues);
    }

    [Fact]
    public void Evaluate_FlagsUnexpectedCodeGraphResultsForNonCodeQuery()
    {
        var pack = new ContextPackViewModel
        {
            Question = "check wmi health on a windows pc",
            SearchTokens = ["check", "health", "pc", "wmi", "windows"],
            CodeGraphProjects =
            [
                new ContextRecordViewModel
                {
                    Kind = ContextRecordKind.CodeGraphProject,
                    Id = Guid.NewGuid(),
                    Title = "Focus L-AIci"
                }
            ]
        };

        var critique = Engine.Evaluate(new PackCritiqueContext(
            "check wmi health on a windows pc",
            pack.SearchTokens,
            pack,
            HasFacetRoute: false,
            AllowCodeGraph: false,
            AttemptNumber: 1));

        Assert.Contains(critique.Issues, issue => issue.Code == "unexpected-codegraph");
    }

    [Fact]
    public void Evaluate_AllowsCodeGraphResultsWhenAllowCodeGraphIsTrue()
    {
        var pack = new ContextPackViewModel
        {
            Question = "review the ContextService code in this repo",
            SearchTokens = ["contextservice", "code", "repo", "review"],
            CodeGraphProjects =
            [
                new ContextRecordViewModel
                {
                    Kind = ContextRecordKind.CodeGraphProject,
                    Id = Guid.NewGuid(),
                    Title = "Focus L-AIci"
                }
            ]
        };

        var critique = Engine.Evaluate(new PackCritiqueContext(
            "review the ContextService code in this repo",
            pack.SearchTokens,
            pack,
            HasFacetRoute: false,
            AllowCodeGraph: true,
            AttemptNumber: 1));

        Assert.DoesNotContain(critique.Issues, issue => issue.Code == "unexpected-codegraph");
    }

    [Fact]
    public void Evaluate_FlagsGenericOverlapOnlyWhenNoGroundedSupportExists()
    {
        var pack = new ContextPackViewModel
        {
            Question = "please help me run a command on my computer",
            SearchTokens = ["command", "computer", "help", "please", "run"],
            TopMatches =
            [
                new ContextRecordViewModel
                {
                    Kind = ContextRecordKind.Memory,
                    Id = Guid.NewGuid(),
                    Title = "Unrelated onboarding checklist",
                    Preview = "Generic onboarding steps with no specific overlap.",
                    MatchReason = "Generic overlap."
                }
            ]
        };

        var critique = Engine.Evaluate(new PackCritiqueContext(
            "please help me run a command on my computer",
            pack.SearchTokens,
            pack,
            HasFacetRoute: false,
            AllowCodeGraph: false,
            AttemptNumber: 1));

        Assert.Contains(critique.Issues, issue => issue.Code == "generic-overlap-only");
    }

    [Fact]
    public void Evaluate_FacetRouteWithNoGroundedSkillsStillSuppressesUngroundedSkillsIssue()
    {
        // Facet-route queries (e.g. structured lookups) are allowed to recommend
        // skills purely on facet match with zero token/phrase grounding - the
        // "ungrounded-skills" issue is only suppressed when NONE of the
        // recommended skills ground at all, not for partial grounding.
        var pack = new ContextPackViewModel
        {
            Question = "show me the facet browse view",
            SearchTokens = ["browse", "facet", "show", "view"],
            RecommendedSkills =
            [
                new SkillCardViewModel
                {
                    Id = Guid.NewGuid(),
                    Name = "Unrelated skill with no overlap",
                    Slug = "unrelated-skill-with-no-overlap",
                    Summary = "Totally different subject.",
                    TriggerHintsText = "unrelated, other"
                }
            ]
        };

        var critique = Engine.Evaluate(new PackCritiqueContext(
            "show me the facet browse view",
            pack.SearchTokens,
            pack,
            HasFacetRoute: true,
            AllowCodeGraph: false,
            AttemptNumber: 1));

        Assert.DoesNotContain(critique.Issues, issue => issue.Code == "ungrounded-skills");
    }

    [Fact]
    public void Evaluate_GroundsOnMultiWordPhraseMatchEvenWithoutIndividualTokenOverlap()
    {
        // "printer drivers" as a two-word phrase should ground the record even
        // though individually "printer" and "drivers" alone wouldn't meet the
        // >=2 specific-token overlap threshold on their own if only one token matched.
        var pack = new ContextPackViewModel
        {
            Question = "audit installed printer drivers on a pc",
            SearchTokens = ["audit", "drivers", "installed", "pc", "printer"],
            Memories =
            [
                new ContextRecordViewModel
                {
                    Kind = ContextRecordKind.Memory,
                    Id = Guid.NewGuid(),
                    Title = "Printer drivers inventory notes",
                    Preview = "How we track printer drivers across the fleet.",
                    MatchReason = "Title shares your search terms."
                }
            ]
        };

        var critique = Engine.Evaluate(new PackCritiqueContext(
            "audit installed printer drivers on a pc",
            pack.SearchTokens,
            pack,
            HasFacetRoute: false,
            AllowCodeGraph: false,
            AttemptNumber: 1));

        Assert.DoesNotContain(critique.Issues, issue => issue.Code == "ungrounded-memories");
    }

    [Fact]
    public void Evaluate_PhraseGroundingCanFalsePositiveOnStopwordConnectorPhrases()
    {
        // Documents a real quirk: common connector words ("a", "on", "that") are
        // not in LowSignalTokens, so two-word phrases built purely from
        // connectors (e.g. "on a") can coincidentally substring-match unrelated
        // grounding text and cause a false "grounded" result. This is a known
        // limitation of the current phrase-based grounding heuristic, not
        // desired behavior - captured here so a future fix has a regression
        // test to update instead of silently changing behavior.
        var pack = new ContextPackViewModel
        {
            Question = "make a powershell that will audit installed printer drivers on a pc",
            SearchTokens = ["audit", "drivers", "installed", "pc", "powershell", "printer", "will"],
            RecommendedSkills =
            [
                new SkillCardViewModel
                {
                    Id = Guid.NewGuid(),
                    Name = "Check WMI health on a Windows PC",
                    Slug = "check-wmi-health-on-a-windows-pc",
                    Summary = "Check WMI and CIM health.",
                    TriggerHintsText = "wmi, windows, pc, powershell"
                }
            ]
        };

        var critique = Engine.Evaluate(new PackCritiqueContext(
            "make a powershell that will audit installed printer drivers on a pc",
            pack.SearchTokens,
            pack,
            HasFacetRoute: false,
            AllowCodeGraph: false,
            AttemptNumber: 1));

        // The skill's title contains the connector phrase "on a" (from
        // "health on a Windows PC"), which coincidentally matches the "on a"
        // phrase built from the question's own connector words - so the
        // engine currently treats this skill as grounded despite having no
        // actual topical overlap with "audit installed printer drivers".
        Assert.DoesNotContain(critique.Issues, issue => issue.Code == "ungrounded-skills");
        Assert.Equal(PackCritiqueAction.Accept, critique.Action);
    }

    [Fact]
    public void Evaluate_RepairDirectiveAlwaysSuppressesCodeGraph()
    {
        var pack = new ContextPackViewModel
        {
            Question = "make a powershell that will audit installed printer drivers on a computer",
            SearchTokens = ["audit", "computer", "drivers", "installed", "powershell", "printer", "will"],
            RecommendedSkills =
            [
                new SkillCardViewModel
                {
                    Id = Guid.NewGuid(),
                    Name = "Check whether a port is open with PowerShell",
                    Slug = "check-whether-a-port-is-open-with-powershell",
                    Summary = "Check TCP or UDP ports.",
                    TriggerHintsText = "powershell, port, tcp, udp, windows"
                }
            ]
        };

        var critique = Engine.Evaluate(new PackCritiqueContext(
            "make a powershell that will audit installed printer drivers on a computer",
            pack.SearchTokens,
            pack,
            HasFacetRoute: false,
            AllowCodeGraph: false,
            AttemptNumber: 1));

        Assert.Equal(PackCritiqueAction.Repair, critique.Action);
        Assert.True(critique.Directive.SuppressCodeGraph);
    }
}
