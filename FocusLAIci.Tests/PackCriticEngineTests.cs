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
}
