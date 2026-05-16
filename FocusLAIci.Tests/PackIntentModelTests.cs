using FocusLAIci.Web.Services;

namespace FocusLAIci.Tests;

public sealed class PackIntentModelTests
{
    public static TheoryData<string, bool?, bool?, bool?, bool?, bool?> RepresentativeQueries => BuildRepresentativeQueries();

    private static TheoryData<string, bool?, bool?, bool?, bool?, bool?> BuildRepresentativeQueries()
    {
        var data = new TheoryData<string, bool?, bool?, bool?, bool?, bool?>();

        static void Add(
            TheoryData<string, bool?, bool?, bool?, bool?, bool?> target,
            string question,
            bool? expectedExternal,
            bool? expectedDirectoryAdmin,
            bool? expectedCode,
            bool? expectedGenericAutomation,
            bool? expectedRepositoryArchitecture)
            => target.Add(question, expectedExternal, expectedDirectoryAdmin, expectedCode, expectedGenericAutomation, expectedRepositoryArchitecture);

        // Directory admin and external operations
        Add(data, "I need a powershell script to check that users have emails set in active directory.", true, true, false, null, false);
        Add(data, "Find users missing mail attributes in Active Directory with PowerShell.", true, true, false, null, false);
        Add(data, "Audit LDAP users for missing proxyAddresses and email values.", true, true, false, null, false);
        Add(data, "Check Entra and mailbox sync issues for user email addresses.", true, true, false, null, false);
        Add(data, "Review Graph and Entra directory email mapping for users.", true, true, false, null, false);
        Add(data, "Export users missing mailboxes from Active Directory.", true, true, false, null, false);
        Add(data, "Find domain users without email attributes and report them.", true, true, false, null, false);
        Add(data, "Use PowerShell to audit AD users with blank email fields.", true, true, false, null, false);
        Add(data, "Check mailbox values in the directory and export missing ones.", true, true, false, null, false);
        Add(data, "Verify DNS and domain configuration related to AD user lookup.", true, true, false, null, false);
        Add(data, "Audit blank proxy addresses and user principal name values with PowerShell.", true, true, false, null, false);
        Add(data, "Create a PowerShell report of Entra users missing mailNickname.", true, true, false, null, false);
        Add(data, "List Exchange Online mailboxes and recipient types for the tenant.", true, true, false, null, false);
        Add(data, "Generate a script to export Office 365 mailbox inventory.", true, true, false, null, false);
        Add(data, "Find on prem Active Directory users missing a title.", true, true, false, null, false);
        Add(data, "Audit department and OfficePhone values for LDAP accounts.", true, true, false, null, false);
        Add(data, "Check userPrincipalName cleanup work for the Microsoft directory.", null, true, false, null, false);
        Add(data, "Review proxy address drift between Graph and Exchange.", true, true, false, null, false);
        Add(data, "Trace forest DNS forwarder issues affecting ADMT.", null, true, false, null, false);
        Add(data, "Find blank Active Directory profile attributes for users.", true, true, false, null, false);
        Add(data, "Show me the command line command to see when a user's password is expiring.", true, true, false, null, false);

        // Generic automation and scripting
        Add(data, "Write a PowerShell script to export disabled users to CSV.", true, false, false, true, false);
        Add(data, "Create a script to list disabled users and export them to CSV.", true, false, false, true, false);
        Add(data, "Generate a PowerShell report of disabled accounts.", true, false, false, true, false);
        Add(data, "Build a PowerShell automation to export stale users.", true, false, false, true, false);
        Add(data, "Write a PowerShell query to list users and save CSV output.", true, false, false, true, false);
        Add(data, "Automate a CSV export for user account data with PowerShell.", true, false, false, true, false);
        Add(data, "Create a reporting script for inactive users.", null, false, false, true, false);
        Add(data, "List users to a CSV file with PowerShell automation.", true, false, false, true, false);
        Add(data, "Query disabled accounts and export a CSV report.", null, false, false, true, false);
        Add(data, "Write an automation script to list accounts and export a report.", null, false, false, true, false);
        Add(data, "Need a PowerShell script that will compare two different folders files and show the differences.", null, false, false, true, false);
        Add(data, "Compare two folders with PowerShell and report changed files.", null, false, false, true, false);
        Add(data, "Create a script that hashes two directory trees and shows file differences.", null, false, false, true, false);
        Add(data, "Build a local folder diff report and export the results to CSV.", null, false, false, true, false);
        Add(data, "Write a script to compare two file lists and show what only exists on each side.", null, false, false, true, false);
        Add(data, "Generate a PowerShell diff for two backup folders.", null, false, false, true, false);
        Add(data, "Automate a comparison of source and destination files after a copy job.", null, false, false, true, false);
        Add(data, "Create a script to compare folders and flag mismatched file hashes.", null, false, false, true, false);
        Add(data, "Build a file inventory diff between two local directories.", null, false, false, true, false);
        Add(data, "Export the differences between two Windows folders.", null, false, false, true, false);
        Add(data, "Create a PowerShell script that checks whether TCP port 443 is open.", null, false, false, true, false);

        // Explicit code intent
        Add(data, "In Focus L-AIci, find the ContextService code and improve current project ranking.", false, false, true, false, false);
        Add(data, "Update the ContextService code in this repo.", false, false, true, false, false);
        Add(data, "Find the controller file in the project and fix the code.", false, false, true, false, false);
        Add(data, "Review the service implementation in this repository.", false, false, true, false, false);
        Add(data, "Search the repo for the symbol and update the method.", false, false, true, false, false);
        Add(data, "Open the project files and adjust the implementation.", false, false, true, false, false);
        Add(data, "Find the source file and patch the controller.", false, false, true, false, false);
        Add(data, "Improve file ranking in the repo code.", false, false, true, false, false);
        Add(data, "Trace the service class in this project.", false, false, true, false, false);
        Add(data, "Fix the code path in the repository.", false, false, true, false, false);
        Add(data, "Find the startup service in the repo and adjust the implementation.", false, false, true, false, false);
        Add(data, "Locate the API controller source and patch the method.", false, false, true, false, false);
        Add(data, "Search the repository for the file that handles authentication.", false, false, true, false, false);
        Add(data, "Review the project code behind the dashboard controller.", false, false, true, false, false);
        Add(data, "Open the repo source and find the service symbol for ranking.", false, false, true, false, false);
        Add(data, "Patch the controller implementation in this project.", false, false, true, false, false);
        Add(data, "Trace the code file that builds the context pack.", false, false, true, false, false);
        Add(data, "Show the repository symbols involved in startup.", false, false, true, false, false);
        Add(data, "Find the service class responsible for search scoring.", false, false, true, false, false);
        Add(data, "Update the source implementation for the current project workflow.", false, false, true, false, false);

        // Repository architecture
        Add(data, "Map this repo before we start a risky refactor and explain the architecture.", false, false, true, false, true);
        Add(data, "Create a codebase map and architecture overview for the repository.", false, false, true, false, true);
        Add(data, "Explain the system design of this repo before refactoring.", false, false, true, false, true);
        Add(data, "Map the repository structure and architecture.", false, false, true, false, true);
        Add(data, "Build an onboarding architecture view for this codebase.", false, false, true, false, true);
        Add(data, "Review the component structure of this repository.", false, false, true, false, true);
        Add(data, "Describe the module layout and architecture for this repo.", false, false, true, false, true);
        Add(data, "Create a repository map before a refactor.", false, false, true, false, true);
        Add(data, "Explain the codebase structure and design.", false, false, true, false, true);
        Add(data, "Map the system architecture for onboarding.", false, false, null, false, true);
        Add(data, "Generate an architecture blueprint for the repo service boundaries.", false, false, true, false, true);
        Add(data, "Explain repository modules and how the system is designed.", false, false, true, false, true);
        Add(data, "Create an onboarding map of the codebase architecture.", false, false, true, false, true);
        Add(data, "Review the repo design and major module boundaries.", false, false, true, false, true);
        Add(data, "Show the architectural structure of this repository.", false, false, true, false, true);
        Add(data, "Map the project architecture before a cleanup pass.", false, false, true, false, true);
        Add(data, "Describe the application blueprint and component relationships.", false, false, true, false, true);
        Add(data, "Create a high level repository structure overview.", false, false, true, false, true);
        Add(data, "Explain how the codebase is organized across modules and services.", false, false, true, false, true);
        Add(data, "Build a repo architecture map for new developers.", false, false, true, false, true);

        // Negative and adjacent scenarios
        Add(data, "Build a PowerShell script to uninstall the Grey Canary endpoint and report status back to the platform.", true, false, false, false, false);
        Add(data, "Review Grey Canary uninstall job flow and callback behavior.", true, false, false, false, false);
        Add(data, "Fix the Grey Canary endpoint removal PowerShell flow.", true, false, false, false, false);
        Add(data, "Trace the Focus runtime startup path from the local project.", false, false, true, false, false);
        Add(data, "Review Microsoft mailbox and Entra integration flow for alert delivery.", true, true, false, false, false);
        Add(data, "Inspect Microsoft Graph mailbox alert delivery configuration.", true, true, false, false, false);
        Add(data, "Look at Office 365 mailbox alert integration.", true, true, false, false, false);
        Add(data, "Audit Active-Directory users for blank proxy addresses and UPN values.", true, true, false, null, false);
        Add(data, "Review Exchange and M365 mailNickname attribute mapping for users.", true, true, false, null, false);
        Add(data, "Check Office 365 userPrincipalName and mailbox attributes for account cleanup.", true, true, false, null, false);
        Add(data, "Explain the app architecture and service boundaries.", false, false, true, false, true);
        Add(data, "Create a repository architecture blueprint.", false, false, true, false, true);
        Add(data, "Find the repo files involved in startup and architecture.", false, false, true, false, true);
        Add(data, "How do I troubleshoot a local Windows PC that is running slow and having network issues?", false, false, false, false, false);
        Add(data, "The website layout is broken in dark mode and the CSS spacing is off.", false, false, false, false, false);
        Add(data, "Review the homepage UI spacing and button contrast.", false, false, false, false, false);
        Add(data, "Instrument Azure App Insights telemetry for this service.", false, false, false, false, false);
        Add(data, "Check cloud deployment drift in the Azure subscription.", false, false, false, false, false);
        Add(data, "Why is the local desktop app blurry on a high DPI monitor?", false, false, false, false, false);
        Add(data, "Compare two folders after a backup job and export only the changed files.", null, false, false, true, false);

        if (data.Count != 102)
        {
            throw new InvalidOperationException($"Expected 102 representative queries but found {data.Count}.");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RepresentativeQueries))]
    public void TinyLocalPackIntentModel_Classifies_RepresentativeQueries(
        string question,
        bool? expectedExternal,
        bool? expectedDirectoryAdmin,
        bool? expectedCode,
        bool? expectedGenericAutomation,
        bool? expectedRepositoryArchitecture)
    {
        var prediction = TinyLocalPackIntentModel.Shared.Predict(question);

        if (expectedExternal.HasValue)
        {
            Assert.Equal(expectedExternal.Value, prediction.IsExternalOperationsQuery);
        }

        if (expectedDirectoryAdmin.HasValue)
        {
            Assert.Equal(expectedDirectoryAdmin.Value, prediction.IsDirectoryAdminQuery);
        }

        if (expectedCode.HasValue)
        {
            Assert.Equal(expectedCode.Value, prediction.HasExplicitCodeIntent);
        }

        if (expectedGenericAutomation.HasValue)
        {
            Assert.Equal(expectedGenericAutomation.Value, prediction.IsGenericAutomationQuery);
        }

        if (expectedRepositoryArchitecture.HasValue)
        {
            Assert.Equal(expectedRepositoryArchitecture.Value, prediction.IsRepositoryArchitectureQuery);
        }
    }

    [Fact]
    public void TinyLocalPackIntentModel_PrefersDirectoryAdminForOnPremAttributeQueries()
    {
        var prediction = TinyLocalPackIntentModel.Shared.Predict("Audit on prem LDAP users for blank proxy address and title values.");

        Assert.True(prediction.IsDirectoryAdminQuery);
        Assert.True(prediction.DirectoryAdminScore > prediction.GenericAutomationScore);
    }

    [Fact]
    public void TinyLocalPackIntentModel_StaysGenericForAccountsAutomation()
    {
        var prediction = TinyLocalPackIntentModel.Shared.Predict("Create a script to export stale accounts to CSV.");

        Assert.True(prediction.IsGenericAutomationQuery);
        Assert.False(prediction.HasExplicitCodeIntent);
    }

    [Fact]
    public void TinyLocalPackIntentModel_FlagsRepositoryArchitectureWhenServiceBoundariesAreRequested()
    {
        var prediction = TinyLocalPackIntentModel.Shared.Predict("Give me an architecture overview of the repository and its service boundaries.");

        Assert.True(prediction.IsRepositoryArchitectureQuery);
        Assert.True(prediction.CodeFamilyScore >= prediction.OperationsFamilyScore);
    }

    [Fact]
    public void TinyLocalPackIntentModel_FlagsProjectHistoryQueries()
    {
        var prediction = TinyLocalPackIntentModel.Shared.Predict("What changed recently around C:\\Copilot\\Sophos-XGS?");

        Assert.True(prediction.IsProjectHistoryQuery);
        Assert.True(prediction.CodeFamilyScore >= prediction.OperationsFamilyScore);
    }

    [Fact]
    public void TinyLocalPackIntentModel_FlagsCurrentStateProjectQueries()
    {
        var prediction = TinyLocalPackIntentModel.Shared.Predict("Summarize the current state of project Focus L-AIci.");

        Assert.True(prediction.IsProjectHistoryQuery);
        Assert.False(prediction.IsGenericAutomationQuery);
    }

    [Fact]
    public void TinyLocalPackIntentModel_PrefersGenericAutomationForPortChecks()
    {
        var prediction = TinyLocalPackIntentModel.Shared.Predict("Create a PowerShell script that checks whether TCP port 443 is open.");

        Assert.True(prediction.IsGenericAutomationQuery);
        Assert.False(prediction.IsDirectoryAdminQuery);
        Assert.False(prediction.HasExplicitCodeIntent);
    }

    [Fact]
    public void TinyLocalPackIntentModel_PrefersDirectoryAdminForPasswordExpiryQueries()
    {
        var prediction = TinyLocalPackIntentModel.Shared.Predict("Show me the command line command to see when a user's password is expiring.");

        Assert.True(prediction.IsDirectoryAdminQuery);
        Assert.True(prediction.DirectoryAdminScore > prediction.GenericAutomationScore);
    }

    [Fact]
    public void TinyLocalPackIntentModel_FlagsThinPowershellPromptsForMoreContext()
    {
        var prediction = TinyLocalPackIntentModel.Shared.Predict("powershell script");

        Assert.True(prediction.NeedsMoreContext);
    }

    [Fact]
    public void TinyLocalPackIntentModel_FlagsSingleTokenPromptsForMoreContext()
    {
        var prediction = TinyLocalPackIntentModel.Shared.Predict("project");

        Assert.True(prediction.NeedsMoreContext);
    }
}
