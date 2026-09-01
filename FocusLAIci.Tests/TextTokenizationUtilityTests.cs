using FocusLAIci.Web.Models;

namespace FocusLAIci.Tests;

public sealed class TextTokenizationUtilityTests
{
    [Fact]
    public void SplitIntoRawTokens_LowercasesAndSplitsOnPunctuationAndWhitespace()
    {
        var tokens = TextTokenizationUtility.SplitIntoRawTokens("Check-WMI Health, on a PC.").ToArray();

        Assert.Equal(["check", "wmi", "health", "on", "a", "pc"], tokens);
    }

    [Fact]
    public void SplitIntoRawTokens_ReturnsEmptyForNullOrWhitespace()
    {
        Assert.Empty(TextTokenizationUtility.SplitIntoRawTokens(null));
        Assert.Empty(TextTokenizationUtility.SplitIntoRawTokens("   "));
    }

    [Fact]
    public void LowSignalGroundingTokens_ContainsCommonAutomationFillerWords()
    {
        // This shared list is what both PackCriticEngine and ContextService use
        // to decide whether a token match is specific enough to count as
        // grounding evidence - it must keep containing the generic
        // automation/filler words both call sites originally hard-coded so
        // consolidating them into one list didn't silently drop coverage.
        Assert.Contains("powershell", TextTokenizationUtility.LowSignalGroundingTokens);
        Assert.Contains("windows", TextTokenizationUtility.LowSignalGroundingTokens);
        Assert.Contains("script", TextTokenizationUtility.LowSignalGroundingTokens);
        Assert.Contains("please", TextTokenizationUtility.LowSignalGroundingTokens);
        Assert.DoesNotContain("printer", TextTokenizationUtility.LowSignalGroundingTokens);
    }
}
