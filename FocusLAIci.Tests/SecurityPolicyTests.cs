using FocusLAIci.Web.Models;
using FocusLAIci.Web.Security;

namespace FocusLAIci.Tests;

public sealed class SecurityPolicyTests
{
    [Fact]
    public void BuildContentSecurityPolicy_IncludesNonceAndStrictDirectives()
    {
        var csp = SecurityHeadersPolicy.BuildContentSecurityPolicy("nonce-value");

        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("script-src 'self' 'nonce-nonce-value'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("object-src 'none'", csp);
        Assert.Contains("form-action 'self'", csp);
    }

    [Fact]
    public void NormalizeBoundContextBriefInput_TrimsQuestionAndClampsResults()
    {
        var normalized = RequestInputPolicy.NormalizeBoundContextBriefInput(new ContextBriefInput
        {
            Question = "  investigate memory security  ",
            IncludeCompletedWork = true,
            ExpandHistory = false,
            ResultsPerSection = 999,
            PackGoal = ContextPackGoal.Architecture
        });

        Assert.Equal("investigate memory security", normalized.Question);
        Assert.Equal(RequestInputPolicy.MaxResultsPerSection, normalized.ResultsPerSection);
        Assert.True(normalized.IncludeCompletedWork);
        Assert.False(normalized.ExpandHistory);
        Assert.Equal(ContextPackGoal.Architecture, normalized.PackGoal);
    }

    [Fact]
    public void TryCreateOptionalContextBriefInput_RejectsOutOfRangeResults()
    {
        var success = RequestInputPolicy.TryCreateOptionalContextBriefInput(
            question: "security audit",
            includeCompletedWork: false,
            expandHistory: true,
            resultsPerSection: 99,
            input: out var input,
            error: out var error);

        Assert.False(success);
        Assert.Null(input);
        Assert.Equal("ResultsPerSection must be between 3 and 10.", error);
    }

    [Fact]
    public void TryCreateOptionalContextBriefInput_ReturnsNullWhenDefaultsAreRequested()
    {
        var success = RequestInputPolicy.TryCreateOptionalContextBriefInput(
            question: null,
            includeCompletedWork: false,
            expandHistory: true,
            resultsPerSection: null,
            input: out var input,
            error: out var error);

        Assert.True(success);
        Assert.Null(input);
        Assert.Null(error);
    }

    [Fact]
    public void LocalPathPolicy_AllowsApprovedDescendantPaths()
    {
        var policy = new LocalPathPolicy(
            approvedProjectRoots: [@"C:\Copilot", @"C:\Users\grey5\source"],
            approvedDatabaseRoots: [@"C:\Copilot", @"C:\Users\grey5\AppData\Local\FocusLAIci"]);

        var projectPath = policy.EnsureApprovedProjectRoot(@"C:\Copilot\Grey Canary");
        var databasePath = policy.EnsureApprovedDatabasePath(@"C:\Users\grey5\AppData\Local\FocusLAIci\focus-palace.db");

        Assert.Equal(@"C:\Copilot\Grey Canary", projectPath);
        Assert.Equal(@"C:\Users\grey5\AppData\Local\FocusLAIci\focus-palace.db", databasePath);
    }

    [Fact]
    public void LocalPathPolicy_RejectsPathsOutsideApprovedRoots()
    {
        var policy = new LocalPathPolicy(
            approvedProjectRoots: [@"C:\Copilot"],
            approvedDatabaseRoots: [@"C:\Copilot"]);

        var projectException = Assert.Throws<InvalidOperationException>(() => policy.EnsureApprovedProjectRoot(@"C:\Windows\System32"));
        var databaseException = Assert.Throws<InvalidOperationException>(() => policy.EnsureApprovedDatabasePath(@"C:\Windows\system.db"));

        Assert.Contains("approved local work roots", projectException.Message);
        Assert.Contains("approved local data roots", databaseException.Message);
    }

    [Fact]
    public void LocalPathPolicy_RejectsNonSqliteDatabaseExtensions()
    {
        var policy = new LocalPathPolicy(
            approvedProjectRoots: [@"C:\Copilot"],
            approvedDatabaseRoots: [@"C:\Copilot"]);

        var exception = Assert.Throws<InvalidOperationException>(() => policy.EnsureApprovedDatabasePath(@"C:\Copilot\focus-palace.txt"));

        Assert.Contains("SQLite file extension", exception.Message);
    }

    [Fact]
    public void LocalPathPolicy_RejectsDatabasePathsThatEscapeThroughSymlinks()
    {
        var sandboxRoot = Path.Combine(Path.GetTempPath(), "FocusLAIci-SecurityPolicyTests", Guid.NewGuid().ToString("N"));
        var approvedRoot = Path.Combine(sandboxRoot, "approved");
        var outsideRoot = Path.Combine(sandboxRoot, "outside");
        var outsideDatabasePath = Path.Combine(outsideRoot, "focus-palace.db");
        var escapeLinkPath = Path.Combine(approvedRoot, "escape");

        Directory.CreateDirectory(approvedRoot);
        Directory.CreateDirectory(outsideRoot);
        File.WriteAllText(outsideDatabasePath, "seed");

        try
        {
            try
            {
                Directory.CreateSymbolicLink(escapeLinkPath, outsideRoot);
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
            catch (PlatformNotSupportedException)
            {
                return;
            }

            var policy = new LocalPathPolicy(
                approvedProjectRoots: [approvedRoot],
                approvedDatabaseRoots: [approvedRoot]);

            var exception = Assert.Throws<InvalidOperationException>(() =>
                policy.EnsureApprovedDatabasePath(Path.Combine(escapeLinkPath, "focus-palace.db")));

            Assert.Contains("approved local data roots", exception.Message);
        }
        finally
        {
            TryDeleteDirectory(escapeLinkPath);
            TryDeleteDirectory(outsideRoot);
            TryDeleteDirectory(approvedRoot);
            TryDeleteDirectory(sandboxRoot);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
