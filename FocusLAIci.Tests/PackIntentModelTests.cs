using FocusLAIci.Web.Services;

namespace FocusLAIci.Tests;

public sealed class PackIntentModelTests
{
    public static TheoryData<string, bool?, bool?, bool?, bool?, bool?> RepresentativeQueries => new()
    {
        { "I need a powershell script to check that users have emails set in active directory.", true, true, false, null, false },
        { "Find users missing mail attributes in Active Directory with PowerShell.", true, true, false, null, false },
        { "Audit LDAP users for missing proxyAddresses and email values.", true, true, false, null, false },
        { "Check Entra and mailbox sync issues for user email addresses.", true, true, false, null, false },
        { "Review Graph and Entra directory email mapping for users.", true, true, false, null, false },
        { "Export users missing mailboxes from Active Directory.", true, true, false, null, false },
        { "Find domain users without email attributes and report them.", true, true, false, null, false },
        { "Use PowerShell to audit AD users with blank email fields.", true, true, false, null, false },
        { "Check mailbox values in the directory and export missing ones.", true, true, false, null, false },
        { "Verify DNS and domain configuration related to AD user lookup.", true, true, false, null, false },
        { "Audit blank proxy addresses and user principal name values with PowerShell.", true, true, false, null, false },

        { "Write a PowerShell script to export disabled users to CSV.", true, false, false, true, false },
        { "Create a script to list disabled users and export them to CSV.", true, false, false, true, false },
        { "Generate a PowerShell report of disabled accounts.", true, false, false, true, false },
        { "Build a PowerShell automation to export stale users.", true, false, false, true, false },
        { "Write a PowerShell query to list users and save CSV output.", true, false, false, true, false },
        { "Automate a CSV export for user account data with PowerShell.", true, false, false, true, false },
        { "Create a reporting script for inactive users.", null, false, false, true, false },
        { "List users to a CSV file with PowerShell automation.", true, false, false, true, false },
        { "Query disabled accounts and export a CSV report.", null, false, false, true, false },
        { "Write an automation script to list accounts and export a report.", null, false, false, true, false },

        { "In Focus L-AIci, find the ContextService code and improve current project ranking.", false, false, true, false, false },
        { "Update the ContextService code in this repo.", false, false, true, false, false },
        { "Find the controller file in the project and fix the code.", false, false, true, false, false },
        { "Review the service implementation in this repository.", false, false, true, false, false },
        { "Search the repo for the symbol and update the method.", false, false, true, false, false },
        { "Open the project files and adjust the implementation.", false, false, true, false, false },
        { "Find the source file and patch the controller.", false, false, true, false, false },
        { "Improve file ranking in the repo code.", false, false, true, false, false },
        { "Trace the service class in this project.", false, false, true, false, false },
        { "Fix the code path in the repository.", false, false, true, false, false },

        { "Map this repo before we start a risky refactor and explain the architecture.", false, false, true, false, true },
        { "Create a codebase map and architecture overview for the repository.", false, false, true, false, true },
        { "Explain the system design of this repo before refactoring.", false, false, true, false, true },
        { "Map the repository structure and architecture.", false, false, true, false, true },
        { "Build an onboarding architecture view for this codebase.", false, false, true, false, true },
        { "Review the component structure of this repository.", false, false, true, false, true },
        { "Describe the module layout and architecture for this repo.", false, false, true, false, true },
        { "Create a repository map before a refactor.", false, false, true, false, true },
        { "Explain the codebase structure and design.", false, false, true, false, true },
        { "Map the system architecture for onboarding.", false, false, null, false, true },

        { "Build a PowerShell script to uninstall the Grey Canary endpoint and report status back to the platform.", true, false, false, false, false },
        { "Review Grey Canary uninstall job flow and callback behavior.", true, false, false, false, false },
        { "Fix the Grey Canary endpoint removal PowerShell flow.", true, false, false, false, false },
        { "Trace the Focus runtime startup path from the local project.", false, false, true, false, false },
        { "Review Microsoft mailbox and Entra integration flow for alert delivery.", true, true, false, false, false },
        { "Inspect Microsoft Graph mailbox alert delivery configuration.", true, true, false, false, false },
        { "Look at Office 365 mailbox alert integration.", true, true, false, false, false },
        { "Audit Active-Directory users for blank proxy addresses and UPN values.", true, true, false, null, false },
        { "Review Exchange and M365 mailNickname attribute mapping for users.", true, true, false, null, false },
        { "Check Office 365 userPrincipalName and mailbox attributes for account cleanup.", true, true, false, null, false },
        { "Explain the app architecture and service boundaries.", false, false, true, false, true },
        { "Create a repository architecture blueprint.", false, false, true, false, true },
        { "Find the repo files involved in startup and architecture.", false, false, true, false, true }
    };

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
}
