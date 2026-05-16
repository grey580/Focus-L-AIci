using FocusLAIci.Web.Data;
using FocusLAIci.Web.Models;
using FocusLAIci.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace FocusLAIci.Tests;

public sealed class PalaceServiceTests
{
    [Fact]
    public void CreateSlug_NormalizesReadableSlugs()
    {
        var slug = SlugUtility.CreateSlug("  Focus L-AIci: Installer & UI  ");

        Assert.Equal("focus-l-aici-installer-ui", slug);
    }

    [Theory]
    [InlineData("engineering-operations", true)]
    [InlineData("grey-canary", true)]
    [InlineData("Engineering-Operations", false)]
    [InlineData("../grey-canary", false)]
    [InlineData("grey_canary", false)]
    [InlineData("", false)]
    public void IsNormalizedSlug_RecognizesExpectedSlugShapes(string value, bool expected)
    {
        Assert.Equal(expected, SlugUtility.IsNormalizedSlug(value));
    }

    [Fact]
    public void TinyLocalPackIntentModel_ClassifiesDirectoryAdminPowershellQueries()
    {
        var prediction = TinyLocalPackIntentModel.Shared.Predict("I need a powershell script to check that users have emails set in active directory.");

        Assert.True(prediction.IsExternalOperationsQuery);
        Assert.True(prediction.IsDirectoryAdminQuery);
        Assert.False(prediction.HasExplicitCodeIntent);
    }

    [Fact]
    public void TinyLocalPackIntentModel_ClassifiesCodeQueriesSeparately()
    {
        var prediction = TinyLocalPackIntentModel.Shared.Predict("Update the ContextService code in this repo to improve project file ranking.");

        Assert.True(prediction.HasExplicitCodeIntent);
        Assert.False(prediction.IsDirectoryAdminQuery);
    }

    [Fact]
    public async Task MemorySeeder_SeedsStarterPalace()
    {
        await using var harness = await TestHarness.CreateAsync();

        await MemorySeeder.SeedSampleDataAsync(harness.Services);

        await using var scope = harness.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FocusMemoryContext>();

        Assert.Equal(3, await dbContext.Wings.CountAsync());
        Assert.Equal(3, await dbContext.Rooms.CountAsync());
        Assert.Equal(3, await dbContext.Memories.CountAsync());
        Assert.True(await dbContext.MemoryLinks.AnyAsync());
        Assert.Contains(await dbContext.Tags.Select(x => x.Slug).ToListAsync(), slug => slug == "installer");
    }

    [Fact]
    public async Task CreateWingAsync_RejectsDuplicateNames()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new PalaceService(serviceContext);

        await service.CreateWingAsync(new WingEditorInput
        {
            Name = "Release Knowledge",
            Description = "Primary release wing."
        }, CancellationToken.None);

        await using var dbContext = harness.CreateDbContext();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateWingAsync(new WingEditorInput
        {
            Name = "release knowledge",
            Description = "Duplicate display name."
        }, CancellationToken.None));

        Assert.Equal("A wing with that name already exists.", exception.Message);
        Assert.Equal(1, await dbContext.Wings.CountAsync());
    }

    [Fact]
    public async Task CleanupConcurrentTestWingsAsync_RemovesOnlyEmptyRaceArtifacts()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();
        dbContext.Wings.AddRange(
            new Wing
            {
                Name = "Concurrent Wing",
                Slug = "concurrent-wing",
                Description = "race"
            },
            new Wing
            {
                Name = "Concurrent Wing",
                Slug = "concurrent-wing-2",
                Description = "race"
            },
            new Wing
            {
                Name = "Concurrent Wing",
                Slug = "concurrent-wing-keeper",
                Description = "real"
            });
        await dbContext.SaveChangesAsync();

        await using var settingsContext = harness.CreateDbContext();
        var settingsService = new SiteSettingsService(settingsContext, harness.Services.GetRequiredService<FocusDatabaseTargetService>());
        var removedCount = await settingsService.CleanupConcurrentTestWingsAsync(CancellationToken.None);

        await using var verifyContext = harness.CreateDbContext();
        var remainingSlugs = await verifyContext.Wings.OrderBy(x => x.Slug).Select(x => x.Slug).ToListAsync();

        Assert.Equal(2, removedCount);
        Assert.Equal(["concurrent-wing-keeper"], remainingSlugs);
    }

    [Fact]
    public async Task SaveMemoryAsync_PersistsRoomBindingAndSearchableTags()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new PalaceService(serviceContext);

        var wingId = await service.CreateWingAsync(new WingEditorInput
        {
            Name = "Grey Canary",
            Description = "Endpoint and platform memory."
        }, CancellationToken.None);

        var roomId = await service.CreateRoomAsync(new RoomEditorInput
        {
            WingId = wingId,
            Name = "Endpoint Installer",
            Description = "Installer notes and registration fixes."
        }, CancellationToken.None);

        var memoryId = await service.SaveMemoryAsync(new MemoryEditorInput
        {
            Title = "Reinstalls must rotate secrets cleanly",
            Summary = "Accept valid reinstalls and clear stale bans.",
            Content = "When a legitimate reinstall happens, rotate the registration secret and drop the temporary IP ban so the endpoint can recover.",
            Kind = MemoryKind.Decision,
            SourceKind = SourceKind.DebugSession,
            Importance = 5,
            IsPinned = true,
            WingId = wingId,
            RoomId = roomId,
            TagsText = "installer, security, reinstall"
        }, CancellationToken.None);

        var detail = await service.GetMemoryAsync(memoryId, CancellationToken.None);
        var byTag = await service.SearchMemoriesAsync(null, null, null, null, "security", null, CancellationToken.None);
        var byQuery = await service.SearchMemoriesAsync("rotate secrets", null, null, null, null, null, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("Grey Canary", detail!.Memory.WingName);
        Assert.Equal("Endpoint Installer", detail.Memory.RoomName);
        Assert.Contains("security", detail.Memory.Tags);
        Assert.Contains(byTag, memory => memory.Id == memoryId);
        Assert.Contains(byQuery, memory => memory.Id == memoryId);
    }

    [Fact]
    public async Task UpdateMemoryTagsAsync_ReplacesSuggestedTagsWithExplicitTags()
    {
        await using var harness = await TestHarness.CreateAsync();
        Guid memoryId;

        await using (var setupContext = harness.CreateDbContext())
        {
            var service = new PalaceService(setupContext);
            var wingId = await service.CreateWingAsync(new WingEditorInput
            {
                Name = "Grey Canary",
                Description = "Endpoint and platform memory."
            }, CancellationToken.None);

            var roomId = await service.CreateRoomAsync(new RoomEditorInput
            {
                WingId = wingId,
                Name = "Platform",
                Description = "Platform delivery notes."
            }, CancellationToken.None);

            memoryId = await service.SaveMemoryAsync(new MemoryEditorInput
            {
                Title = "Grey Canary incidents page uses POST-backed AJAX partial refresh",
                Summary = "Grey Canary incidents now refresh through a POST-backed AJAX partial flow with bulk close, page-size, and status filters.",
                Content = "The Grey Canary incidents page was refactored so GET renders the shell and POST returns partial refreshes. Bulk close now binds incident IDs reliably, and recent branch state also includes IP-inclusive auth logs in AuthController.",
                Kind = MemoryKind.Fact,
                SourceKind = SourceKind.ChatSession,
                SourceReference = "regression test",
                Importance = 4,
                WingId = wingId,
                RoomId = roomId
            }, CancellationToken.None);
        }

        await using (var updateContext = harness.CreateDbContext())
        {
            var service = new PalaceService(updateContext);
            await service.UpdateMemoryTagsAsync(memoryId, "grey-canary, incidents, ajax, paging, auth-logs", CancellationToken.None);
        }

        await using var verifyContext = harness.CreateDbContext();
        var verifyService = new PalaceService(verifyContext);
        var detail = await verifyService.GetMemoryAsync(memoryId, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(["ajax", "auth-logs", "grey-canary", "incidents", "paging"], detail!.Memory.Tags.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task SaveSkillAsync_PersistsSearchableSkillDetails()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new PalaceService(serviceContext);

        var wingId = await service.CreateWingAsync(new WingEditorInput
        {
            Name = "Local System",
            Description = "Machine-specific workflows."
        }, CancellationToken.None);

        var skillId = await service.SaveSkillAsync(new SkillEditorInput
        {
            Name = "Investigate CSS regressions",
            Summary = "Trace static asset failures and MIME issues.",
            Category = SkillCategory.Task,
            WhenToUse = "Use this when CSS or JS stops loading.",
            Flow = "Check the launch root.\nCheck rendered asset URLs.\nFetch browser-visible asset GET responses.",
            ExamplesText = "Investigate why site.css is not loading.",
            TriggerHintsText = "css, static files, mime",
            WingId = wingId,
            IsPinned = true
        }, CancellationToken.None);

        var detail = await service.GetSkillAsync("investigate-css-regressions", CancellationToken.None);
        var search = await service.GetSkillSummariesAsync("mime", null, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, skillId);
        Assert.NotNull(detail);
        Assert.Equal("Investigate CSS regressions", detail!.Skill.Name);
        Assert.Equal("Local System", detail.Skill.WingName);
        Assert.Contains("Check the launch root.", detail.FlowSteps);
        Assert.Contains(search, skill => skill.Id == skillId);
    }

    [Fact]
    public async Task SkillRecommendationsAndUsageMetadata_AreSurfacedAcrossCatalogAndDetail()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new PalaceService(serviceContext);

        var wingId = await service.CreateWingAsync(new WingEditorInput
        {
            Name = "Platform Delivery",
            Description = "Platform delivery workflows."
        }, CancellationToken.None);

        var recommendedId = await service.SaveSkillAsync(new SkillEditorInput
        {
            Name = "Stabilize CSS pipeline",
            Summary = "Trace broken styles and static asset failures.",
            Category = SkillCategory.Task,
            WhenToUse = "Use this when CSS or JavaScript stops loading in production.",
            Flow = "Check launch root.\nInspect rendered asset URLs.\nFetch browser-visible static files.",
            ExamplesText = "Fix why the dashboard CSS is missing.",
            TriggerHintsText = "css, javascript, static files, mime",
            WingId = wingId,
            IsPinned = true
        }, CancellationToken.None);

        var staleId = await service.SaveSkillAsync(new SkillEditorInput
        {
            Name = "Legacy static fallback",
            Summary = "Legacy fallback workflow.",
            Category = SkillCategory.Task,
            WhenToUse = "Use this only for old static fallback checks.",
            Flow = "Open fallback checks.",
            ExamplesText = "Review old static fallback behavior.",
            TriggerHintsText = "legacy, fallback",
            WingId = wingId,
            IsPinned = false
        }, CancellationToken.None);

        await using (var staleContext = harness.CreateDbContext())
        {
            var staleSkill = await staleContext.Skills.FirstAsync(x => x.Id == staleId);
            staleSkill.ReviewAfterUtc = DateTime.UtcNow.AddDays(-2);
            await staleContext.SaveChangesAsync();
        }

        var recommendations = await service.RecommendSkillsAsync("css asset mime issue", wingId, null, 3, CancellationToken.None);
        var detail = await service.GetSkillAsync("stabilize-css-pipeline", CancellationToken.None, trackUsage: true);
        var catalog = await service.GetSkillCatalogAsync(null, null, wingId, false, true, CancellationToken.None);

        Assert.NotEmpty(recommendations);
        Assert.Equal(recommendedId, recommendations.First().Id);
        Assert.NotNull(detail);
        Assert.Equal(1, detail!.Skill.UseCount);
        Assert.NotNull(detail.RelatedContext);
        Assert.Single(catalog.Skills);
        Assert.Equal(staleId, catalog.Skills.Single().Id);
        Assert.True(catalog.Skills.Single().NeedsReview);
    }

    [Fact]
    public async Task EnsureDatabaseAsync_SeedsImportedStarterSkills()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var scope = harness.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FocusMemoryContext>();
        var repoSkillCatalogService = scope.ServiceProvider.GetRequiredService<RepoSkillCatalogService>();

        await MemorySeeder.EnsureDatabaseAsync(dbContext, CancellationToken.None);

        var service = new PalaceService(
            dbContext,
            new ContextService(dbContext),
            NullFocusEventPublisher.Instance,
            null,
            new FocusAgentCatalogService(),
            repoSkillCatalogService);

        var skills = await service.GetSkillSummariesAsync(null, null, CancellationToken.None);

        Assert.Contains(skills, x => x.Slug == "acquire-codebase-knowledge");
        Assert.Contains(skills, x => x.Slug == "generate-architecture-blueprint");
        Assert.Contains(skills, x => x.Slug == "design-agent-governance");
        Assert.Contains(skills, x => x.Slug == "check-agent-owasp-compliance");
        Assert.Contains(skills, x => x.Slug == "orchestrate-ai-delivery-team");
        Assert.Contains(skills, x => x.Slug == "instrument-app-insights-telemetry");
        Assert.Contains(skills, x => x.Slug == "apply-dotnet-best-practices");
        Assert.Contains(skills, x => x.Slug == "review-dotnet-design-patterns");
        Assert.Contains(skills, x => x.Slug == "plan-dotnet-upgrade");
        Assert.Contains(skills, x => x.Slug == "review-csharp-async-workflows");
        Assert.Contains(skills, x => x.Slug == "review-ef-core-data-access");
        Assert.Contains(skills, x => x.Slug == "work-as-web-coder");
        Assert.Contains(skills, x => x.Slug == "review-web-design-quality");
        Assert.Contains(skills, x => x.Slug == "test-web-application-flows");
        Assert.Contains(skills, x => x.Slug == "review-sql-code-safety");
        Assert.Contains(skills, x => x.Slug == "optimize-sql-performance");
        Assert.Contains(skills, x => x.Slug == "run-security-review");
        Assert.Contains(skills, x => x.Slug == "plan-threat-model-analysis");
        Assert.Contains(skills, x => x.Slug == "manage-secret-scanning");
        Assert.Contains(skills, x => x.Slug == "configure-codeql-scanning");
        Assert.Contains(skills, x => x.Slug == "audit-on-prem-active-directory-user-attributes");
        Assert.Contains(skills, x => x.Slug == "compare-folder-contents-with-powershell");
        Assert.Contains(skills, x => x.Slug == "check-wmi-health-on-a-windows-pc");
        Assert.Contains(skills, x => x.Slug == "get-exchange-online-mailbox-inventory");
        Assert.All(skills.Where(x =>
            x.Slug is "acquire-codebase-knowledge"
                or "generate-architecture-blueprint"
                or "design-agent-governance"
                or "check-agent-owasp-compliance"
                or "orchestrate-ai-delivery-team"
                or "instrument-app-insights-telemetry"
                or "apply-dotnet-best-practices"
                or "review-dotnet-design-patterns"
                or "plan-dotnet-upgrade"
                or "review-csharp-async-workflows"
                or "review-ef-core-data-access"
                or "work-as-web-coder"
                or "review-web-design-quality"
                or "test-web-application-flows"
                or "review-sql-code-safety"
                or "optimize-sql-performance"
                or "run-security-review"
                or "plan-threat-model-analysis"
                or "manage-secret-scanning"
                or "audit-on-prem-active-directory-user-attributes"
                or "compare-folder-contents-with-powershell"
                or "check-wmi-health-on-a-windows-pc"
                or "configure-codeql-scanning"
                or "get-exchange-online-mailbox-inventory"), x => Assert.NotNull(x.ReviewAfterUtc));
    }

    [Fact]
    public async Task DashboardAndWorkspaceBootstrap_SurfaceScopedAgents()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new PalaceService(serviceContext);

        var dashboard = await service.GetDashboardAsync(new ContextBriefInput
        {
            Question = "review the riskiest delivery changes before shipping",
            PackGoal = ContextPackGoal.Delivery
        }, CancellationToken.None);
        var workspace = await service.GetWorkspaceExportAsync(CancellationToken.None);
        var bootstrap = await service.GetWorkspaceBootstrapAsync("operator", CancellationToken.None);

        Assert.Equal(4, dashboard.FeaturedAgents.Count);
        Assert.Contains(dashboard.RecommendedAgents, x => x.Slug == "review-agent");
        Assert.Equal(4, workspace.RecommendedAgents.Count);
        Assert.Equal(4, bootstrap.FeaturedAgents.Count);
        Assert.Contains(bootstrap.RecommendedAgents, x => x.Slug == "context-agent");
        Assert.Contains("Scoped agents:", workspace.ExportText);
    }

    [Fact]
    public async Task RecommendSkillsAsync_PrefersExchangeOnlineMailboxInventorySkill()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var scope = harness.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FocusMemoryContext>();
        var repoSkillCatalogService = scope.ServiceProvider.GetRequiredService<RepoSkillCatalogService>();

        await MemorySeeder.EnsureDatabaseAsync(dbContext, CancellationToken.None);

        var service = new PalaceService(
            dbContext,
            new ContextService(dbContext),
            NullFocusEventPublisher.Instance,
            null,
            new FocusAgentCatalogService(),
            repoSkillCatalogService);

        var recommendations = await service.RecommendSkillsAsync(
            "need to create a powershell script to get all mailboxes and their types from exchange online",
            null,
            null,
            5,
            CancellationToken.None);

        Assert.NotEmpty(recommendations);
        Assert.Equal("get-exchange-online-mailbox-inventory", recommendations.First().Slug);
    }

    [Fact]
    public async Task RecommendSkillsAsync_PrefersOnPremActiveDirectoryAttributeAuditSkill()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var scope = harness.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FocusMemoryContext>();
        var repoSkillCatalogService = scope.ServiceProvider.GetRequiredService<RepoSkillCatalogService>();

        await MemorySeeder.EnsureDatabaseAsync(dbContext, CancellationToken.None);

        var service = new PalaceService(
            dbContext,
            new ContextService(dbContext),
            NullFocusEventPublisher.Instance,
            null,
            new FocusAgentCatalogService(),
            repoSkillCatalogService);

        var recommendations = await service.RecommendSkillsAsync(
            "need a powershell script that can get on prem active directory users who are missing a title",
            null,
            null,
            5,
            CancellationToken.None);

        Assert.NotEmpty(recommendations);
        Assert.Equal("audit-on-prem-active-directory-user-attributes", recommendations.First().Slug);
    }

    [Fact]
    public async Task RecommendSkillsAsync_PrefersFolderComparisonPowerShellSkill()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var scope = harness.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FocusMemoryContext>();
        var repoSkillCatalogService = scope.ServiceProvider.GetRequiredService<RepoSkillCatalogService>();

        await MemorySeeder.EnsureDatabaseAsync(dbContext, CancellationToken.None);

        var service = new PalaceService(
            dbContext,
            new ContextService(dbContext),
            NullFocusEventPublisher.Instance,
            null,
            new FocusAgentCatalogService(),
            repoSkillCatalogService);

        var recommendations = await service.RecommendSkillsAsync(
            "need a powershell script that will compare two different folders files and show the differences",
            null,
            null,
            5,
            CancellationToken.None);

        Assert.NotEmpty(recommendations);
        Assert.Equal("compare-folder-contents-with-powershell", recommendations.First().Slug);
        Assert.DoesNotContain(recommendations, x => x.Slug == "get-exchange-online-mailbox-inventory");
        Assert.DoesNotContain(recommendations, x => x.Slug == "audit-on-prem-active-directory-user-attributes");
    }

    [Fact]
    public async Task RecommendSkillsAsync_PrefersWindowsWmiHealthSkill()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var scope = harness.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FocusMemoryContext>();
        var repoSkillCatalogService = scope.ServiceProvider.GetRequiredService<RepoSkillCatalogService>();

        await MemorySeeder.EnsureDatabaseAsync(dbContext, CancellationToken.None);

        var service = new PalaceService(
            dbContext,
            new ContextService(dbContext),
            NullFocusEventPublisher.Instance,
            null,
            new FocusAgentCatalogService(),
            repoSkillCatalogService);

        var recommendations = await service.RecommendSkillsAsync(
            "create a powershell script that can check and see if wmi is working on a pc",
            null,
            null,
            5,
            CancellationToken.None);

        Assert.NotEmpty(recommendations);
        Assert.Equal("check-wmi-health-on-a-windows-pc", recommendations.First().Slug);
        Assert.DoesNotContain(recommendations, x => x.Slug == "get-exchange-online-mailbox-inventory");
        Assert.DoesNotContain(recommendations, x => x.Slug == "audit-on-prem-active-directory-user-attributes");
    }

    [Fact]
    public async Task SaveMemoryAsync_AssignsWingMemoriesToGeneralRoom()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new PalaceService(serviceContext);

        var wingId = await service.CreateWingAsync(new WingEditorInput
        {
            Name = "Reusable Pattern Defaults",
            Description = "Shared implementation patterns."
        }, CancellationToken.None);

        var memoryId = await service.SaveMemoryAsync(new MemoryEditorInput
        {
            Title = "Default to General room",
            Summary = "Wing-level memories should normalize into a real room.",
            Content = "This memory should land in the General room automatically.",
            Kind = MemoryKind.Decision,
            SourceKind = SourceKind.Architecture,
            Importance = 4,
            WingId = wingId
        }, CancellationToken.None);

        var detail = await service.GetMemoryAsync(memoryId, CancellationToken.None);
        var visualizer = await service.GetVisualizerAsync(CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("General", detail!.Memory.RoomName);
        Assert.Contains(visualizer.Wings, wing => wing.Name == "Reusable Pattern Defaults" && wing.Rooms.Any(room => room.Name == "General"));
        Assert.DoesNotContain(visualizer.Wings.SelectMany(wing => wing.GeneralMemories), memory => memory.Id == memoryId);
    }

    [Fact]
    public async Task GetWingAsync_SelectingRoomSlugFiltersMemoriesAndReturnsRoomPanel()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new PalaceService(serviceContext);

        var wingId = await service.CreateWingAsync(new WingEditorInput
        {
            Name = "Local System",
            Description = "Machine-specific knowledge."
        }, CancellationToken.None);

        var focusRoomId = await service.CreateRoomAsync(new RoomEditorInput
        {
            WingId = wingId,
            Name = "Focus L-AIci",
            Description = "Local Focus runbooks."
        }, CancellationToken.None);

        var greyCanaryRoomId = await service.CreateRoomAsync(new RoomEditorInput
        {
            WingId = wingId,
            Name = "Grey Canary",
            Description = "Local Grey Canary runbooks."
        }, CancellationToken.None);

        await service.SaveMemoryAsync(new MemoryEditorInput
        {
            Title = "Focus startup",
            Summary = "How to start Focus locally.",
            Content = "Run the built DLL and bind localhost:5191.",
            Kind = MemoryKind.Reference,
            SourceKind = SourceKind.DebugSession,
            Importance = 5,
            WingId = wingId,
            RoomId = focusRoomId
        }, CancellationToken.None);

        await service.SaveMemoryAsync(new MemoryEditorInput
        {
            Title = "Grey Canary validation",
            Summary = "How to validate Grey Canary locally.",
            Content = "Run platform and domain tests.",
            Kind = MemoryKind.Reference,
            SourceKind = SourceKind.DebugSession,
            Importance = 4,
            WingId = wingId,
            RoomId = greyCanaryRoomId
        }, CancellationToken.None);

        var wing = await service.GetWingAsync("local-system", "focus-l-aici", CancellationToken.None);

        Assert.NotNull(wing);
        Assert.Equal("local-system", wing!.Slug);
        Assert.Equal("focus-l-aici", wing.SelectedRoomSlug);
        Assert.NotNull(wing.SelectedRoom);
        Assert.Equal("focus-l-aici", wing.SelectedRoom!.Slug);
        Assert.Equal("Focus L-AIci", wing.SelectedRoom!.Name);
        Assert.Single(wing.Memories);
        Assert.Equal("Focus startup", wing.Memories.Single().Title);
        Assert.Equal(2, wing.Rooms.Count);
    }

    [Fact]
    public async Task MemoryTrustLifecycle_VerifyAndEditDriveTrustState()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new PalaceService(serviceContext);

        var memoryId = await service.SaveMemoryAsync(new MemoryEditorInput
        {
            Title = "Trust baseline",
            Summary = "Initial memory trust state.",
            Content = "Original verified content.",
            Kind = MemoryKind.Decision,
            SourceKind = SourceKind.Architecture,
            Importance = 4,
            TagsText = "trust, memory"
        }, CancellationToken.None);

        await service.MarkMemoryVerifiedAsync(memoryId, CancellationToken.None);

        var verified = await service.GetMemoryAsync(memoryId, CancellationToken.None);
        Assert.NotNull(verified);
        Assert.Equal(MemoryVerificationStatus.Verified, verified!.Memory.VerificationStatus);
        Assert.False(verified.Memory.IsReviewDue);
        Assert.NotNull(verified.Memory.LastVerifiedUtc);
        Assert.NotNull(verified.Memory.ReviewAfterUtc);

        await service.SaveMemoryAsync(new MemoryEditorInput
        {
            Id = memoryId,
            Title = "Trust baseline",
            Summary = "Initial memory trust state.",
            Content = "Edited content that should require review.",
            Kind = MemoryKind.Decision,
            SourceKind = SourceKind.Architecture,
            Importance = 4,
            TagsText = "trust, memory"
        }, CancellationToken.None);

        var needsReview = await service.GetMemoryAsync(memoryId, CancellationToken.None);
        Assert.NotNull(needsReview);
        Assert.Equal(MemoryVerificationStatus.NeedsReview, needsReview!.Memory.VerificationStatus);
        Assert.True(needsReview.Memory.IsReviewDue);
        Assert.Equal("Needs review", needsReview.Memory.FreshnessLabel);
    }

    [Fact]
    public async Task ArchiveMemoryAsync_RemovesMemoryFromDefaultRetrievalButKeepsDetailVisible()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new PalaceService(serviceContext);

        var memoryId = await service.SaveMemoryAsync(new MemoryEditorInput
        {
            Title = "Archive candidate",
            Summary = "Should leave normal retrieval after archive.",
            Content = "Archive this memory during governance cleanup.",
            Kind = MemoryKind.Reference,
            SourceKind = SourceKind.ManualNote,
            Importance = 3,
            IsPinned = true,
            TagsText = "archive, governance"
        }, CancellationToken.None);

        await service.ArchiveMemoryAsync(memoryId, "No longer needed in active retrieval.", CancellationToken.None);

        var search = await service.SearchMemoriesAsync("archive candidate", null, null, null, null, null, CancellationToken.None);
        var workspace = await service.GetWorkspaceExportAsync(CancellationToken.None);
        var detail = await service.GetMemoryAsync(memoryId, CancellationToken.None);

        Assert.DoesNotContain(search, x => x.Id == memoryId);
        Assert.DoesNotContain("Archive candidate", workspace.ExportText);
        Assert.NotNull(detail);
        Assert.Equal(MemoryLifecycleState.Archived, detail!.Memory.LifecycleState);
        Assert.True(detail.Memory.IsRetired);
        Assert.Equal("No longer needed in active retrieval.", detail.Memory.LifecycleReason);
    }

    [Fact]
    public async Task SupersedeMemoryAsync_HidesOriginalAndLinksReplacement()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new PalaceService(serviceContext);

        var originalId = await service.SaveMemoryAsync(new MemoryEditorInput
        {
            Title = "Old install guidance",
            Summary = "Outdated answer.",
            Content = "Original guidance.",
            Kind = MemoryKind.Decision,
            SourceKind = SourceKind.ManualNote,
            Importance = 4,
            IsPinned = true,
            TagsText = "install, old"
        }, CancellationToken.None);

        var replacementId = await service.SaveMemoryAsync(new MemoryEditorInput
        {
            Title = "New install guidance",
            Summary = "Current answer.",
            Content = "Replacement guidance.",
            Kind = MemoryKind.Decision,
            SourceKind = SourceKind.ManualNote,
            Importance = 4,
            IsPinned = true,
            TagsText = "install, new"
        }, CancellationToken.None);

        await service.SupersedeMemoryAsync(originalId, replacementId, "Replacement memory is canonical.", CancellationToken.None);

        var search = await service.SearchMemoriesAsync("guidance", null, null, null, null, null, CancellationToken.None);
        var oldDetail = await service.GetMemoryAsync(originalId, CancellationToken.None);

        Assert.DoesNotContain(search, x => x.Id == originalId);
        Assert.Contains(search, x => x.Id == replacementId);
        Assert.NotNull(oldDetail);
        Assert.Equal(MemoryLifecycleState.Superseded, oldDetail!.Memory.LifecycleState);
        Assert.Equal(replacementId, oldDetail.Memory.SupersededByMemoryId);
        Assert.Equal("New install guidance", oldDetail.Memory.SupersededByTitle);
    }

    [Fact]
    public async Task GetInspectorAsync_IncludesGovernanceQueueForRetiredAndAgingMemories()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new PalaceService(serviceContext);

        var archivedId = await service.SaveMemoryAsync(new MemoryEditorInput
        {
            Title = "Archived memory",
            Summary = "Should appear in governance queue.",
            Content = "Retired content.",
            Kind = MemoryKind.Reference,
            SourceKind = SourceKind.ManualNote,
            Importance = 2,
            TagsText = "archive"
        }, CancellationToken.None);

        var staleId = await service.SaveMemoryAsync(new MemoryEditorInput
        {
            Title = "Aging memory",
            Summary = "Still unverified.",
            Content = "Needs triage.",
            Kind = MemoryKind.Reference,
            SourceKind = SourceKind.ManualNote,
            Importance = 2,
            TagsText = "aging"
        }, CancellationToken.None);

        await service.ArchiveMemoryAsync(archivedId, "Archived for test.", CancellationToken.None);

        await using (var updateContext = harness.CreateDbContext())
        {
            var stale = await updateContext.Memories.FirstAsync(x => x.Id == staleId, CancellationToken.None);
            stale.UpdatedUtc = DateTime.UtcNow.AddDays(-30);
            await updateContext.SaveChangesAsync(CancellationToken.None);
        }

        var inspector = await service.GetInspectorAsync(null, CancellationToken.None);

        Assert.Contains(inspector.GovernanceQueue.Items, x => x.Id == archivedId);
        Assert.Contains(inspector.GovernanceQueue.Items, x => x.Id == staleId);
        Assert.True(inspector.GovernanceQueue.ArchivedCount >= 1);
        Assert.True(inspector.GovernanceQueue.UnverifiedActiveCount >= 1);
    }

    [Fact]
    public async Task GetVisualizerAsync_GroupsMemoriesByWingRoomAndTag()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new PalaceService(serviceContext);

        var wingId = await service.CreateWingAsync(new WingEditorInput
        {
            Name = "Architecture",
            Description = "Cross-cutting design memory."
        }, CancellationToken.None);

        var roomId = await service.CreateRoomAsync(new RoomEditorInput
        {
            WingId = wingId,
            Name = "Runtime",
            Description = "Runtime behavior and operational notes."
        }, CancellationToken.None);

        var memoryId = await service.SaveMemoryAsync(new MemoryEditorInput
        {
            Title = "Visualizer nodes should stay clickable",
            Summary = "Map rooms to clickable memory nodes.",
            Content = "The visualizer needs an obvious path from room nodes to memory detail pages.",
            Kind = MemoryKind.Insight,
            SourceKind = SourceKind.Architecture,
            Importance = 4,
            WingId = wingId,
            RoomId = roomId,
            TagsText = "visualizer, frontend"
        }, CancellationToken.None);

        var unsortedId = await service.SaveMemoryAsync(new MemoryEditorInput
        {
            Title = "Unsorted palace note",
            Summary = "The palace graph should still surface unfiled memories.",
            Content = "Unsorted memories should appear in the 3D holding area instead of disappearing.",
            Kind = MemoryKind.Reference,
            SourceKind = SourceKind.ManualNote,
            Importance = 3,
            TagsText = "visualizer, backlog"
        }, CancellationToken.None);

        await using (var linkContext = harness.CreateDbContext())
        {
            linkContext.MemoryLinks.Add(new MemoryLink
            {
                FromMemoryEntryId = memoryId,
                ToMemoryEntryId = unsortedId,
                Label = "Related"
            });
            await linkContext.SaveChangesAsync(CancellationToken.None);
        }

        var todoId = await service.CreateTodoAsync(new TodoEditorInput
        {
            Title = "Add todo lane to palace visualizer",
            Details = "Operational work should show up without manual memory promotion.",
            Status = TodoStatus.InProgress
        }, CancellationToken.None);

        var model = await service.GetVisualizerAsync(CancellationToken.None);

        var wing = Assert.Single(model.Wings);
        var room = Assert.Single(wing.Rooms);
        var memory = Assert.Single(room.Memories);
        var unsorted = Assert.Single(model.UnsortedMemories);

        Assert.Equal("Architecture", wing.Name);
        Assert.Equal("Runtime", room.Name);
        Assert.Equal(memoryId, memory.Id);
        Assert.Equal(unsortedId, unsorted.Id);
        Assert.Contains(model.ActiveTodos, todo => todo.Id == todoId);
        Assert.Contains(model.Tags, tag => tag.Slug == "visualizer" && tag.MemoryCount == 2);
        Assert.Contains(model.Scene.Nodes, node => node.NodeTypeLabel == "Palace");
        var wingNode = Assert.Single(model.Scene.Nodes, node => node.NodeTypeLabel == "Wing" && node.Label == "Architecture");
        var roomNode = Assert.Single(model.Scene.Nodes, node => node.NodeTypeLabel == "Room" && node.Label == "Runtime");
        var memoryNode = Assert.Single(model.Scene.Nodes, node => node.NodeTypeLabel == "Memory" && node.Label == "Visualizer nodes should stay clickable");
        Assert.True(wingNode.Radius > 14d);
        Assert.Equal($"wing:{wingId}", roomNode.OrbitCenterNodeId);
        Assert.True(roomNode.OrbitRadius > 0d);
        Assert.Equal(Math.PI / 60d, roomNode.OrbitSpeed, precision: 10);
        Assert.Equal($"room:{roomId}", memoryNode.OrbitCenterNodeId);
        Assert.Equal(Math.PI / 120d, memoryNode.OrbitSpeed, precision: 10);
        Assert.Contains(model.Scene.Nodes, node => node.NodeTypeLabel == "Wing" && node.Label == "Unsorted wing");
        Assert.Contains(model.Scene.Nodes, node => node.NodeTypeLabel == "Wing" && node.Label == "Workboard");
        Assert.Contains(model.Scene.Nodes, node => node.NodeTypeLabel == "Todo" && node.Label == "Add todo lane to palace visualizer");
        Assert.Contains(model.Scene.Edges, edge => edge.FromNodeId == $"memory:{memoryId}" && edge.ToNodeId == $"memory:{unsortedId}");
    }

    [Fact]
    public async Task GetVisualizerAsync_DenseRoomMemoriesStayPacked()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new PalaceService(serviceContext);

        var wingId = await service.CreateWingAsync(new WingEditorInput
        {
            Name = "Ticketing",
            Description = "Operational work"
        }, CancellationToken.None);

        var roomId = await service.CreateRoomAsync(new RoomEditorInput
        {
            WingId = wingId,
            Name = "Completed tickets",
            Description = "High-volume closed work"
        }, CancellationToken.None);

        for (var index = 0; index < 30; index++)
        {
            await service.SaveMemoryAsync(new MemoryEditorInput
            {
                Title = $"Completed ticket {index + 1}",
                Summary = "Closed ticket summary",
                Content = "Closed ticket detail",
                Kind = MemoryKind.Reference,
                SourceKind = SourceKind.ManualNote,
                Importance = 2 + (index % 3),
                WingId = wingId,
                RoomId = roomId
            }, CancellationToken.None);
        }

        var model = await service.GetVisualizerAsync(CancellationToken.None);

        var roomMemoryNodes = model.Scene.Nodes
            .Where(node => node.NodeTypeLabel == "Memory" && node.OrbitCenterNodeId == $"room:{roomId}")
            .ToArray();

        Assert.Equal(30, roomMemoryNodes.Length);
        Assert.True(roomMemoryNodes.Max(node => node.OrbitRadius) < 150d);
    }

    [Fact]
    public async Task TodoBoardAndDashboard_SurfaceVisibleWorkState()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new PalaceService(serviceContext);

        var pendingId = await service.CreateTodoAsync(new TodoEditorInput
        {
            Title = "Capture next interruption-safe step",
            Details = "Leave enough detail that the next session can resume without guesswork.",
            Status = TodoStatus.Pending
        }, CancellationToken.None);

        var inProgressId = await service.CreateTodoAsync(new TodoEditorInput
        {
            Title = "Implement the visible Focus workboard",
            Details = "Add a page and dashboard preview for todos.",
            Status = TodoStatus.InProgress
        }, CancellationToken.None);

        var doneId = await service.CreateTodoAsync(new TodoEditorInput
        {
            Title = "Ship clickable dashboard cards",
            Status = TodoStatus.Done
        }, CancellationToken.None);

        await service.UpdateTodoStatusAsync(pendingId, TodoStatus.Blocked, CancellationToken.None);

        var board = await service.GetTodoBoardAsync(CancellationToken.None);
        var dashboard = await service.GetDashboardAsync(CancellationToken.None);

        Assert.Single(board.InProgressTodos);
        Assert.Single(board.BlockedTodos);
        Assert.Single(board.DoneTodos);
        Assert.Equal(inProgressId, board.InProgressTodos.Single().Id);
        Assert.Equal(pendingId, board.BlockedTodos.Single().Id);
        Assert.Equal(doneId, board.DoneTodos.Single().Id);
        Assert.Equal(2, dashboard.Stats.OpenTodoCount);
        Assert.Equal(1, dashboard.Stats.CompletedTodoCount);
        Assert.DoesNotContain(dashboard.CurrentTodos, todo => todo.Id == doneId);
        Assert.Contains(dashboard.CurrentTodos, todo => todo.Id == inProgressId);
    }

    [Fact]
    public async Task CreateTodoAsync_PersistsVeryLargeDetails()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new PalaceService(serviceContext);
        var largePrompt = string.Join(
            Environment.NewLine,
            Enumerable.Range(1, 600).Select(index => $"Line {index:D3}: preserve the full prompt and implementation context."));

        var todoId = await service.CreateTodoAsync(new TodoEditorInput
        {
            Title = "Store a large handoff prompt",
            Details = largePrompt,
            Status = TodoStatus.Pending
        }, CancellationToken.None);

        var board = await service.GetTodoBoardAsync(CancellationToken.None);
        var todo = Assert.Single(board.PendingTodos, x => x.Id == todoId);

        Assert.Equal(largePrompt, todo.Details);
        Assert.True(todo.Details.Length > 2000);
        Assert.NotEqual(todo.Details, todo.PreviewDetails);
        Assert.True(todo.HasMoreDetails);
    }

    [Fact]
    public async Task TodoDetailsFlow_PreservesStoredStatusAndSupportsEditDelete()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new PalaceService(serviceContext);

        var todoId = await service.CreateTodoAsync(new TodoEditorInput
        {
            Title = "Implement todo details workflow",
            Details = new string('x', 320),
            Status = TodoStatus.Pending
        }, CancellationToken.None);

        var detail = await service.GetTodoDetailsAsync(todoId, CancellationToken.None);

        Assert.Equal(TodoStatus.Pending, detail.Todo.Status);
        Assert.Equal(TodoStatus.Pending, detail.Input.Status);
        Assert.True(detail.Todo.HasMoreDetails);
        Assert.EndsWith("...", detail.Todo.PreviewDetails);
        Assert.Equal(243, detail.Todo.PreviewDetails.Length);

        await service.UpdateTodoAsync(todoId, new TodoEditorInput
        {
            Title = "Implement todo details page",
            Details = "Keep the full prompt here.",
            Status = TodoStatus.Blocked
        }, CancellationToken.None);

        var updated = await service.GetTodoDetailsAsync(todoId, CancellationToken.None);

        Assert.Equal("Implement todo details page", updated.Todo.Title);
        Assert.Equal(TodoStatus.Blocked, updated.Todo.Status);
        Assert.Equal(TodoStatus.Blocked, updated.Input.Status);
        Assert.Equal("Keep the full prompt here.", updated.Todo.Details);
        Assert.False(updated.Todo.HasMoreDetails);

        await service.DeleteTodoAsync(todoId, CancellationToken.None);

        await using var dbContext = harness.CreateDbContext();
        Assert.Equal(0, await dbContext.Todos.CountAsync());
    }

    [Fact]
    public async Task TicketingService_GeneratesInheritedSubticketsFromDescription()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new TicketingService(serviceContext);

        var ticketId = await service.CreateTicketAsync(new TicketEditorInput
        {
            Title = "Build Focus ticketing system",
            Description = """
                - Add the schema and service layer
                - Build the MVC pages
                - Cover the workflow with tests
                """,
            Status = TicketStatus.InProgress,
            Priority = TicketPriority.High,
            Assignee = "Copilot",
            TagsText = "focus, ticketing",
            GitBranch = "main",
            HasGitCommit = true
        }, CancellationToken.None);

        var createdCount = await service.GenerateSubTicketsAsync(ticketId, CancellationToken.None);
        var details = await service.GetDetailsAsync(ticketId, CancellationToken.None);

        Assert.Equal(3, createdCount);
        Assert.Equal(3, details.SubTickets.Count);
        Assert.All(details.SubTickets, subTicket =>
        {
            Assert.Equal(TicketPriority.High, subTicket.Priority);
            Assert.Equal("Copilot", subTicket.Assignee);
            Assert.Contains("focus", subTicket.Tags);
            Assert.Equal("main", subTicket.GitBranch);
            Assert.True(subTicket.HasGitCommit);
            Assert.Equal(TicketStatus.New, subTicket.Status);
        });
    }

    [Fact]
    public async Task TicketingService_TracksNotesTimeAndCompletionMemory()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new TicketingService(serviceContext);

        var ticketId = await service.CreateTicketAsync(new TicketEditorInput
        {
            Title = "Ship autonomous ticket workflow",
            Description = "Track notes, time, and completion summaries.",
            Status = TicketStatus.InProgress,
            Priority = TicketPriority.Critical,
            Assignee = "Copilot",
            TagsText = "automation, focus"
        }, CancellationToken.None);

        var noteId = await service.AddNoteAsync(ticketId, new TicketNoteInput
        {
            Author = "Copilot",
            Content = "Initial implementation is underway."
        }, CancellationToken.None);

        await service.UpdateNoteAsync(ticketId, noteId, new TicketNoteInput
        {
            Author = "Copilot",
            Content = "Implementation finished and ready to summarize."
        }, CancellationToken.None);

        await service.LogTimeAsync(ticketId, new TicketTimeLogInput
        {
            ModelName = "Copilot",
            Summary = "Implemented ticketing MVC flow",
            MinutesSpent = 45,
            LoggedUtc = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc)
        }, CancellationToken.None);

        await service.UpdateTicketAsync(ticketId, new TicketEditorInput
        {
            Id = ticketId,
            Title = "Ship autonomous ticket workflow",
            Description = "Track notes, time, and completion summaries.",
            Status = TicketStatus.Completed,
            Priority = TicketPriority.Critical,
            Assignee = "Copilot",
            TagsText = "automation, focus",
            GitBranch = "main",
            HasGitCommit = true
        }, CancellationToken.None);

        await using var verifyContext = harness.CreateDbContext();
        var ticket = await verifyContext.Tickets.FirstAsync(x => x.Id == ticketId);
        var memory = await verifyContext.Memories.FirstOrDefaultAsync(x => x.Id == ticket.SummaryMemoryId);
        var activities = await verifyContext.TicketActivities.Where(x => x.TicketId == ticketId).ToListAsync();

        Assert.NotNull(memory);
        Assert.Equal(TicketStatus.Completed, ticket.Status);
        Assert.NotNull(ticket.CompletedUtc);
        Assert.Contains("TKT-", memory!.Title);
        Assert.Contains("Track notes, time, and completion summaries.", memory.Content);
        Assert.True(memory.IsPinned);
        Assert.Contains(activities, activity => activity.ActivityType == "completed");
        Assert.Contains(activities, activity => activity.ActivityType == "time-logged");
        Assert.Contains(activities, activity => activity.ActivityType == "note-updated");
    }

    [Fact]
    public async Task TicketingService_UpdateTicketStatusAsync_CompletesTicketAndCreatesSummaryMemory()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new TicketingService(serviceContext);

        var ticketId = await service.CreateTicketAsync(new TicketEditorInput
        {
            Title = "Close ticket through status API",
            Description = "Exercise the dedicated ticket status update path.",
            Status = TicketStatus.InProgress,
            Priority = TicketPriority.High,
            Assignee = "Copilot",
            TagsText = "api, status"
        }, CancellationToken.None);

        await service.UpdateTicketStatusAsync(ticketId, TicketStatus.Completed, CancellationToken.None);

        await using var verifyContext = harness.CreateDbContext();
        var ticket = await verifyContext.Tickets.FirstAsync(x => x.Id == ticketId);
        var memory = await verifyContext.Memories.FirstOrDefaultAsync(x => x.Id == ticket.SummaryMemoryId);
        var activities = await verifyContext.TicketActivities.Where(x => x.TicketId == ticketId).ToListAsync();

        Assert.Equal(TicketStatus.Completed, ticket.Status);
        Assert.NotNull(ticket.CompletedUtc);
        Assert.NotNull(memory);
        Assert.Contains(activities, activity => activity.ActivityType == "status-updated");
    }

    [Fact]
    public async Task TicketingService_BoardSearchAndPagination_FilterCompletedTicketsAndSummarizeDescriptions()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new TicketingService(serviceContext);

        for (var index = 1; index <= 7; index++)
        {
            await service.CreateTicketAsync(new TicketEditorInput
            {
                Title = $"Completed ticket {index}",
                Description = index == 3
                    ? new string('A', 320) + " release search target"
                    : $"Routine completed work item {index}.",
                Status = TicketStatus.Completed,
                Priority = TicketPriority.Medium,
                Assignee = index == 3 ? "ReleaseBot" : "Copilot",
                TagsText = index == 3 ? "release, search" : "focus"
            }, CancellationToken.None);
        }

        var firstPage = await service.GetBoardAsync(null, 1, CancellationToken.None);
        var secondPage = await service.GetBoardAsync(null, 2, CancellationToken.None);
        var searched = await service.GetBoardAsync("release", 1, CancellationToken.None);

        Assert.Equal(TicketBoardViewModel.DefaultCompletedPageSize, firstPage.CompletedTickets.Count);
        Assert.Equal(2, firstPage.CompletedTotalPages);
        Assert.Equal(7, firstPage.CompletedFilteredCount);
        Assert.Equal(2, secondPage.CompletedTickets.Count);
        Assert.Single(searched.CompletedTickets);
        Assert.Equal(1, searched.CompletedTotalPages);
        Assert.Equal(1, searched.CompletedFilteredCount);

        var summarized = searched.CompletedTickets.Single();
        Assert.Equal("Completed ticket 3", summarized.Title);
        Assert.True(summarized.HasMoreDescription);
        Assert.Equal(243, summarized.PreviewDescription.Length);
        Assert.EndsWith("...", summarized.PreviewDescription);
    }

    [Fact]
    public async Task TicketingService_BoardCountsTopLevelTicketsSeparatelyFromOpenSubtickets()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new TicketingService(serviceContext);

        var parentTicketId = await service.CreateTicketAsync(new TicketEditorInput
        {
            Title = "Parent completed ticket",
            Description = "Parent work is complete.",
            Status = TicketStatus.Completed,
            Priority = TicketPriority.Medium,
            Assignee = "Copilot",
            TagsText = "focus, tickets"
        }, CancellationToken.None);

        await service.CreateSubTicketAsync(parentTicketId, new TicketSubTicketInput
        {
            Title = "Open child ticket",
            Description = "Still needs follow-up.",
            Status = TicketStatus.New
        }, CancellationToken.None);

        var board = await service.GetBoardAsync(null, 1, CancellationToken.None);

        Assert.Equal(0, board.OpenTopLevelTicketCount);
        Assert.Equal(1, board.CompletedTopLevelTicketCount);
        Assert.Equal(1, board.OpenSubTicketCount);
        Assert.Single(board.CompletedTickets);
        Assert.Empty(board.NewTickets);
        Assert.Empty(board.InProgressTickets);
        Assert.Empty(board.BlockedTickets);
    }

    [Fact]
    public async Task DatabaseTargetService_SwitchesToCustomDatabaseAndInitializesSchema()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"focus-db-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(contentRoot);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:FocusPalace"] = "Data Source=focus-palace.db"
                })
                .Build();
            var environment = new TestHostEnvironment
            {
                ContentRootPath = contentRoot,
                ContentRootFileProvider = new PhysicalFileProvider(contentRoot)
            };
            var service = new FocusDatabaseTargetService(configuration, environment);
            var targetPath = Path.Combine(contentRoot, "switched", "copilot-focus.db");

            var snapshot = await service.UpdateTargetAsync(new DatabaseTargetInput
            {
                DatabasePath = targetPath
            }, CancellationToken.None);

            Assert.False(snapshot.UsesDefaultDatabase);
            Assert.Equal(Path.GetFullPath(targetPath), snapshot.DatabasePath);
            Assert.True(File.Exists(targetPath));
            Assert.True(snapshot.DatabaseSizeBytes.HasValue);
            Assert.True(snapshot.DatabaseSizeBytes.Value > 0);
            Assert.NotEqual("Unavailable", snapshot.DatabaseSizeLabel);

            var options = new DbContextOptionsBuilder<FocusMemoryContext>()
                .UseSqlite(snapshot.ConnectionString)
                .Options;
            await using var dbContext = new FocusMemoryContext(options);

            Assert.True(await dbContext.Database.CanConnectAsync());
            Assert.True(await dbContext.SiteSettings.AnyAsync(x => x.Id == 1));
            Assert.Equal(0, await dbContext.Todos.CountAsync());
        }
        finally
        {
            TryDeleteDirectory(contentRoot);
        }
    }

    [Fact]
    public async Task DatabaseTargetService_CanResetBackToDefaultDatabase()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"focus-db-reset-{Guid.NewGuid():N}");
        Directory.CreateDirectory(contentRoot);

        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:FocusPalace"] = "Data Source=focus-palace.db"
                })
                .Build();
            var environment = new TestHostEnvironment
            {
                ContentRootPath = contentRoot,
                ContentRootFileProvider = new PhysicalFileProvider(contentRoot)
            };
            var service = new FocusDatabaseTargetService(configuration, environment);
            var targetPath = Path.Combine(contentRoot, "switched", "copilot-focus.db");

            await service.UpdateTargetAsync(new DatabaseTargetInput
            {
                DatabasePath = targetPath
            }, CancellationToken.None);

            var resetSnapshot = await service.UpdateTargetAsync(new DatabaseTargetInput
            {
                UseDefaultDatabase = true
            }, CancellationToken.None);

            Assert.True(resetSnapshot.UsesDefaultDatabase);
            Assert.Equal(Path.Combine(contentRoot, "focus-palace.db"), resetSnapshot.DatabasePath);
            Assert.False(File.Exists(Path.Combine(contentRoot, "focus-palace.database-target.json")));
            Assert.True(resetSnapshot.DatabaseSizeBytes.HasValue);
            Assert.True(resetSnapshot.DatabaseSizeBytes.Value > 0);
        }
        finally
        {
            TryDeleteDirectory(contentRoot);
        }
    }

    [Fact]
    public async Task CodeGraphService_ScansRepositoryAndPersistsRelationships()
    {
        var repositoryRoot = CreateCodeGraphFixture();

        try
        {
            await using var harness = await TestHarness.CreateAsync();
            await using var serviceContext = harness.CreateDbContext();
            var service = new CodeGraphService(serviceContext);

            var projectId = await service.CreateProjectAsync(new CodeGraphProjectInput
            {
                Name = "Code Graph Fixture",
                RootPath = repositoryRoot,
                Description = "Fixture for code graph scanning."
            }, CancellationToken.None);

            await using var verifyContext = harness.CreateDbContext();
            var project = await verifyContext.CodeGraphProjects.SingleAsync(x => x.Id == projectId);
            var nodes = await verifyContext.CodeGraphNodes.Where(x => x.ProjectId == projectId).ToListAsync();
            var edges = await verifyContext.CodeGraphEdges.Where(x => x.ProjectId == projectId).ToListAsync();

            Assert.Equal(2, project.FileCount);
            Assert.True(project.SymbolCount >= 4);
            Assert.True(project.RelationshipCount >= 6);
            Assert.Contains(nodes, node => node.Label == "AlphaService" && node.NodeType == CodeGraphNodeType.Type);
            Assert.Contains(nodes, node => node.Label == "Execute" && node.NodeType == CodeGraphNodeType.Method);
            Assert.Contains(edges, edge => edge.RelationshipType == "imports");
            Assert.Contains(edges, edge => edge.RelationshipType == "contains");
            Assert.Contains(edges, edge => edge.RelationshipType == "references");
        }
        finally
        {
            TryDeleteDirectory(repositoryRoot);
        }
    }

    [Fact]
    public async Task CodeGraphService_ProjectDetailsBuildSelectedNeighborhood()
    {
        var repositoryRoot = CreateCodeGraphFixture();

        try
        {
            await using var harness = await TestHarness.CreateAsync();
            await using var serviceContext = harness.CreateDbContext();
            var service = new CodeGraphService(serviceContext);

            var projectId = await service.CreateProjectAsync(new CodeGraphProjectInput
            {
                Name = "Code Graph Fixture",
                RootPath = repositoryRoot
            }, CancellationToken.None);

            var detail = await service.GetProjectAsync(projectId, "AlphaService", null, null, CancellationToken.None);

            Assert.NotNull(detail);
            Assert.Equal("AlphaService", detail!.Graph.SelectedNodeLabel);
            Assert.NotEmpty(detail.Graph.Nodes);
            Assert.NotEmpty(detail.Relationships);
            Assert.Contains(detail.Hotspots, item => item.Label == "AlphaService");
            Assert.NotEmpty(detail.Scene.Nodes);
            Assert.NotEmpty(detail.Scene.Edges);
            Assert.Contains(detail.Scene.Legend, item => item.Label == nameof(CodeGraphNodeType.Type));
            Assert.All(detail.Scene.Nodes, node => Assert.InRange(node.Radius, 1d, 18d));
        }
        finally
        {
            TryDeleteDirectory(repositoryRoot);
        }
    }

    [Fact]
    public async Task ContextService_PrioritizesExactSignalAndExplainsWhy()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();

        var exactMemory = new MemoryEntry
        {
            Title = "Installer token registration and deployment",
            Summary = "Exact context match for deployment troubleshooting.",
            Content = "Covers installer token registration and deployment flow.",
            Kind = MemoryKind.Decision,
            SourceKind = SourceKind.DebugSession,
            UpdatedUtc = DateTime.UtcNow,
            Importance = 5,
            Wing = new Wing
            {
                Name = "Grey Canary",
                Slug = "grey-canary",
                Description = "Primary product wing."
            }
        };

        var noisyMemory = new MemoryEntry
        {
            Title = "Deployment notes",
            Summary = "Only one broad token overlaps.",
            Content = "General deployment checklist without token registration detail.",
            Kind = MemoryKind.Reference,
            SourceKind = SourceKind.DebugSession,
            UpdatedUtc = DateTime.UtcNow.AddDays(-120),
            Wing = new Wing
            {
                Name = "Operations",
                Slug = "operations",
                Description = "General operations wing."
            }
        };

        dbContext.Memories.AddRange(exactMemory, noisyMemory);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var service = new ContextService(dbContext);
        var pack = await service.BuildContextPackAsync("installer token registration and deployment", CancellationToken.None);

        Assert.NotNull(pack);
        var topMemory = Assert.Single(pack!.Memories.Take(1));
        Assert.Equal(exactMemory.Title, topMemory.Title);
        Assert.Equal("Top match", topMemory.ScoreLabel);
        Assert.False(string.IsNullOrWhiteSpace(topMemory.MatchReason));
    }

    [Fact]
    public async Task ContextService_DemotesReviewDueMemoryAndShowsFreshnessWarning()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();

        var staleMemory = new MemoryEntry
        {
            Title = "Installer deployment trust check",
            Summary = "Older matching memory.",
            Content = "Older matching content.",
            Kind = MemoryKind.Decision,
            SourceKind = SourceKind.DebugSession,
            UpdatedUtc = DateTime.UtcNow.AddDays(-120),
            VerificationStatus = MemoryVerificationStatus.NeedsReview,
            ReviewAfterUtc = DateTime.UtcNow.AddDays(-1),
            Wing = new Wing
            {
                Name = "Grey Canary",
                Slug = "grey-canary-stale",
                Description = "Stale wing."
            }
        };

        var freshMemory = new MemoryEntry
        {
            Title = "Installer deployment trust check",
            Summary = "Fresh verified memory.",
            Content = "Fresh verified content.",
            Kind = MemoryKind.Decision,
            SourceKind = SourceKind.DebugSession,
            UpdatedUtc = DateTime.UtcNow.AddDays(-2),
            VerificationStatus = MemoryVerificationStatus.Verified,
            LastVerifiedUtc = DateTime.UtcNow.AddDays(-1),
            ReviewAfterUtc = DateTime.UtcNow.AddDays(89),
            Wing = new Wing
            {
                Name = "Grey Canary Fresh",
                Slug = "grey-canary-fresh",
                Description = "Fresh wing."
            }
        };

        dbContext.Memories.AddRange(staleMemory, freshMemory);
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var service = new ContextService(dbContext);
        var pack = await service.BuildContextPackAsync("installer deployment trust check", CancellationToken.None);

        Assert.NotNull(pack);
        Assert.True(string.IsNullOrWhiteSpace(pack!.Memories.First().FreshnessWarning));
        Assert.Contains(pack.Memories, memory => memory.Title == staleMemory.Title && memory.FreshnessWarning == "Needs review");
    }

    [Fact]
    public async Task ContextService_IgnoresStopwordOnlyDifferencesInRanking()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();

        dbContext.Todos.Add(new TodoEntry
        {
            Title = "Installer token deployment reliability",
            Details = "Track installer token deployment fixes and registration stability.",
            Status = TodoStatus.InProgress,
            UpdatedUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var service = new ContextService(dbContext);
        var concise = await service.BuildContextPackAsync("installer token deployment", CancellationToken.None);
        var withStopwords = await service.BuildContextPackAsync("the installer token and deployment", CancellationToken.None);

        Assert.NotNull(concise);
        Assert.NotNull(withStopwords);
        Assert.Equal(concise!.Todos.Select(x => x.Title), withStopwords!.Todos.Select(x => x.Title));
        Assert.DoesNotContain("and", withStopwords.SearchTokens);
        Assert.DoesNotContain("the", withStopwords.SearchTokens);
    }

    [Fact]
    public async Task ContextService_AppliesPackGoalAndSemanticScoring()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();

        dbContext.Memories.Add(new MemoryEntry
        {
            Title = "Platform architecture baseline",
            Summary = "Core system design decisions.",
            Content = "This memory captures the architecture structure for the platform.",
            Kind = MemoryKind.Decision,
            SourceKind = SourceKind.Architecture,
            UpdatedUtc = DateTime.UtcNow,
            Wing = new Wing
            {
                Name = "Architecture",
                Slug = "architecture",
                Description = "System design notes."
            }
        });
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var service = new ContextService(dbContext);
        var pack = await service.BuildContextPackAsync(new ContextBriefInput
        {
            Question = "system design",
            PackGoal = ContextPackGoal.Architecture,
            ResultsPerSection = 3
        }, CancellationToken.None);

        Assert.NotNull(pack);
        Assert.Equal("Architecture", pack!.GoalLabel);
        var topMemory = Assert.Single(pack.Memories);
        Assert.True(topMemory.SemanticScore > 0m);
        Assert.Equal("Top match", topMemory.ScoreLabel);
    }

    [Fact]
    public async Task ContextService_PrefersCurrentProjectForCodeGraphMatches()
    {
        var repoRoot = Path.Combine(Path.GetTempPath(), $"focus-current-repo-{Guid.NewGuid():N}");
        var contentRoot = Path.Combine(repoRoot, "FocusLAIci.Web");
        Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
        Directory.CreateDirectory(contentRoot);

        try
        {
            await using var harness = await TestHarness.CreateAsync(contentRoot);
            await using var scope = harness.Services.CreateAsyncScope();
            var contextService = scope.ServiceProvider.GetRequiredService<ContextService>();
            var dbContext = scope.ServiceProvider.GetRequiredService<FocusMemoryContext>();

            var currentProject = new CodeGraphProject
            {
                Name = "Focus L-AIci",
                RootPath = repoRoot,
                Description = "Current Focus repo.",
                Summary = "Current project summary.",
                UpdatedUtc = DateTime.UtcNow
            };
            var unrelatedProject = new CodeGraphProject
            {
                Name = "Simple SMB Tester",
                RootPath = @"C:\Copilot\Simple SMB Tester",
                Description = "Unrelated repo with broad dashboard wording.",
                Summary = "Dashboard utilities and other code.",
                UpdatedUtc = DateTime.UtcNow
            };

            dbContext.CodeGraphProjects.AddRange(currentProject, unrelatedProject);
            await dbContext.SaveChangesAsync(CancellationToken.None);

            var pack = await contextService.BuildContextPackAsync("focus dashboard width", CancellationToken.None);

            Assert.NotNull(pack);
            Assert.Equal(currentProject.Name, pack!.CodeGraphProjects.First().Title);
            Assert.DoesNotContain(pack.CodeGraphProjects, x => x.Title == unrelatedProject.Name && x.Score >= pack.CodeGraphProjects.First().Score);
        }
        finally
        {
            if (Directory.Exists(repoRoot))
            {
                Directory.Delete(repoRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ContextService_SuppressesUnrelatedCodeGraphNoiseForExternalOpsQueries()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();

        var activeDirectoryWing = new Wing
        {
            Name = "Directory Ops Test",
            Slug = "directory-ops-test",
            Description = "Directory administration notes."
        };
        var greyCanaryWing = new Wing
        {
            Name = "Grey Canary Product Test",
            Slug = "grey-canary-product-test",
            Description = "Product runbooks."
        };
        dbContext.Wings.AddRange(activeDirectoryWing, greyCanaryWing);

        dbContext.Memories.Add(new MemoryEntry
        {
            Title = "AD immutable ID migration",
            Summary = "Active Directory mail and identity mapping guidance.",
            Content = "For Active Directory work, inspect user mail attributes and related identity fields before migration.",
            Kind = MemoryKind.Reference,
            SourceKind = SourceKind.ManualNote,
            Wing = activeDirectoryWing,
            UpdatedUtc = DateTime.UtcNow
        });
        dbContext.Memories.Add(new MemoryEntry
        {
            Title = "Grey Canary uninstall PowerShell script",
            Summary = "Endpoint removal uses a PowerShell script and callback flow.",
            Content = "Run the uninstall PowerShell script, then wait for the Grey Canary status callback to finish endpoint removal.",
            Kind = MemoryKind.Reference,
            SourceKind = SourceKind.ManualNote,
            Wing = greyCanaryWing,
            UpdatedUtc = DateTime.UtcNow
        });

        dbContext.CodeGraphProjects.AddRange(
            new CodeGraphProject
            {
                Name = "Focus L-AIci",
                RootPath = @"C:\Copilot\Focus L-AIci",
                Description = "Local-first development memory tool.",
                Summary = "App source code.",
                UpdatedUtc = DateTime.UtcNow
            },
            new CodeGraphProject
            {
                Name = "Simple SMB Tester",
                RootPath = @"C:\Copilot\Simple SMB Tester",
                Description = "Portable file share utility.",
                Summary = "Desktop utility source code.",
                UpdatedUtc = DateTime.UtcNow
            });
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var service = new ContextService(dbContext);
        var pack = await service.BuildContextPackAsync("powershell script active directory users emails", CancellationToken.None);

        Assert.NotNull(pack);
        Assert.NotEmpty(pack!.Memories);
        Assert.Equal("AD immutable ID migration", pack.Memories.First().Title);
        Assert.DoesNotContain(pack.Memories.Take(3), memory => memory.Title.Contains("Grey Canary", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(pack.CodeGraphProjects);
        Assert.Empty(pack.CodeGraphFiles);
        Assert.Empty(pack.CodeGraphNodes);
        Assert.Empty(pack.RecommendedSkills);
    }

    [Fact]
    public async Task ContextService_DirectoryAdminAttributeQueries_FilterBroadAdAndTicketMemories()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();

        var activeDirectoryWing = new Wing
        {
            Name = "Directory Ops Test",
            Slug = "directory-ops-attribute-test",
            Description = "Directory administration notes."
        };
        var ticketWing = new Wing
        {
            Name = "Ticketing System",
            Slug = "ticketing-system-test",
            Description = "Completed tickets and task summaries."
        };
        dbContext.Wings.AddRange(activeDirectoryWing, ticketWing);

        dbContext.Memories.AddRange(
            new MemoryEntry
            {
                Title = "Audit AD users missing email attributes",
                Summary = "Use PowerShell to report users with blank mail, proxyAddresses, or mailbox values.",
                Content = "Check Active Directory user email attributes and export missing mail values for cleanup.",
                Kind = MemoryKind.Reference,
                SourceKind = SourceKind.ManualNote,
                Wing = activeDirectoryWing,
                UpdatedUtc = DateTime.UtcNow
            },
            new MemoryEntry
            {
                Title = "AD immutable ID migration",
                Summary = "Active Directory migration guidance for identity mapping.",
                Content = "Handle immutable ID, mailNickname, and consistency GUID mapping during migration work.",
                Kind = MemoryKind.Reference,
                SourceKind = SourceKind.ManualNote,
                Wing = activeDirectoryWing,
                UpdatedUtc = DateTime.UtcNow
            },
            new MemoryEntry
            {
                Title = "ADMT conditional forwarder repair",
                Summary = "Forest and DNS fix for cross-domain migration issues.",
                Content = "Recreate the conditional forwarder and validate forest DNS lookups for ADMT.",
                Kind = MemoryKind.Incident,
                SourceKind = SourceKind.DebugSession,
                Wing = activeDirectoryWing,
                UpdatedUtc = DateTime.UtcNow
            },
            new MemoryEntry
            {
                Title = "TKT-0063 - Focus L'Aici MCP update",
                Summary = "Completed ticket for Copilot platform work.",
                Content = "Medium completed ticket with subtickets and tracked minutes.",
                Kind = MemoryKind.Task,
                SourceKind = SourceKind.ChatSession,
                Wing = ticketWing,
                UpdatedUtc = DateTime.UtcNow
            });
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var service = new ContextService(dbContext);
        var pack = await service.BuildContextPackAsync(
            "I need a powershell script to check that users have emails set in the emails section in active directory.",
            CancellationToken.None);

        Assert.NotNull(pack);
        Assert.NotEmpty(pack!.Memories);
        Assert.Equal("Audit AD users missing email attributes", pack.Memories.First().Title);
        Assert.DoesNotContain(pack.Memories, memory => memory.Title.Contains("migration", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(pack.Memories, memory => memory.Title.Contains("forwarder", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(pack.Memories, memory => memory.Title.Contains("TKT-", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(pack.RecommendedSkills);
    }

    [Fact]
    public async Task ContextService_DirectoryAdminProxyAddressPhraseQueries_FilterBroadAdMemories()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();

        var activeDirectoryWing = new Wing
        {
            Name = "Directory Ops Phrase Test",
            Slug = "directory-ops-phrase-test",
            Description = "Directory administration notes."
        };
        dbContext.Wings.Add(activeDirectoryWing);

        dbContext.Memories.AddRange(
            new MemoryEntry
            {
                Title = "Audit AD users missing email attributes",
                Summary = "Use PowerShell to report users with blank mail, proxyAddresses, or mailbox values.",
                Content = "Check Active Directory user email attributes and export missing mail values for cleanup.",
                Kind = MemoryKind.Reference,
                SourceKind = SourceKind.ManualNote,
                Wing = activeDirectoryWing,
                UpdatedUtc = DateTime.UtcNow
            },
            new MemoryEntry
            {
                Title = "AD immutable ID migration",
                Summary = "Active Directory migration guidance for identity mapping.",
                Content = "Handle immutable ID, mailNickname, and consistency GUID mapping during migration work.",
                Kind = MemoryKind.Reference,
                SourceKind = SourceKind.ManualNote,
                Wing = activeDirectoryWing,
                UpdatedUtc = DateTime.UtcNow
            },
            new MemoryEntry
            {
                Title = "ADMT conditional forwarder repair",
                Summary = "Forest and DNS fix for cross-domain migration issues.",
                Content = "Recreate the conditional forwarder and validate forest DNS lookups for ADMT.",
                Kind = MemoryKind.Incident,
                SourceKind = SourceKind.DebugSession,
                Wing = activeDirectoryWing,
                UpdatedUtc = DateTime.UtcNow
            });
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var service = new ContextService(dbContext);
        var pack = await service.BuildContextPackAsync(
            "Audit Active Directory users for blank proxy addresses with PowerShell.",
            CancellationToken.None);

        Assert.NotNull(pack);
        Assert.NotEmpty(pack!.Memories);
        Assert.Equal("Audit AD users missing email attributes", pack.Memories.First().Title);
        Assert.DoesNotContain(pack.Memories, memory => memory.Title.Contains("migration", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(pack.Memories, memory => memory.Title.Contains("forwarder", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ContextService_DirectoryAdminAttributeQueries_KeepOnlyAttributeAuditSkills()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();

        var activeDirectoryWing = new Wing
        {
            Name = "Directory Skill Test",
            Slug = "directory-skill-test",
            Description = "Directory administration skills."
        };
        dbContext.Wings.Add(activeDirectoryWing);

        dbContext.Skills.AddRange(
            new SkillEntry
            {
                Name = "Audit AD mail attributes",
                Slug = "audit-ad-mail-attributes",
                Summary = "Use PowerShell to audit mail, proxyAddresses, mailbox, userPrincipalName, and mailNickname values.",
                Category = SkillCategory.Task,
                WhenToUse = "Use this when Active Directory user email attributes need review or cleanup.",
                Flow = "Check mail and proxyAddresses.\nReview userPrincipalName and mailNickname.\nExport missing values.",
                ExamplesText = "Audit AD users missing email attributes.",
                TriggerHintsText = "active directory, email, mail, mailbox, proxyAddresses, userPrincipalName, mailNickname, audit, powershell",
                IsPinned = true,
                Wing = activeDirectoryWing,
                UpdatedUtc = DateTime.UtcNow
            },
            new SkillEntry
            {
                Name = "Repair ADMT forwarders",
                Slug = "repair-admt-forwarders",
                Summary = "Fix DNS and forest trust issues during directory migrations.",
                Category = SkillCategory.Task,
                WhenToUse = "Use this for ADMT migration and conditional forwarder troubleshooting.",
                Flow = "Review forest trust.\nRepair DNS forwarders.\nValidate domain resolution.",
                ExamplesText = "Repair ADMT migration DNS issues.",
                TriggerHintsText = "admt, dns, forest, forwarder, migration",
                IsPinned = true,
                Wing = activeDirectoryWing,
                UpdatedUtc = DateTime.UtcNow
            });
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var service = new ContextService(dbContext);
        var pack = await service.BuildContextPackAsync(
            "Audit Active Directory users for blank proxy addresses and UPN values with PowerShell.",
            CancellationToken.None);

        Assert.NotNull(pack);
        Assert.NotEmpty(pack!.RecommendedSkills);
        Assert.Equal("Audit AD mail attributes", pack.RecommendedSkills.First().Name);
        Assert.DoesNotContain(pack.RecommendedSkills, skill => skill.Name.Contains("ADMT", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ContextService_LocalSupportQueries_SuppressCodeGraphAndIrrelevantSkills()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();

        dbContext.Skills.Add(new SkillEntry
        {
            Name = "Review web design quality",
            Slug = "review-web-design-quality-test",
            Summary = "Review website UI and layout quality.",
            Category = SkillCategory.Task,
            WhenToUse = "Use this for web UI review.",
            Flow = "Inspect layout and CSS.",
            TriggerHintsText = "website, web, ui, layout, css"
        });
        dbContext.CodeGraphProjects.Add(new CodeGraphProject
        {
            Name = "Focus L-AIci",
            RootPath = @"C:\Copilot\Focus L-AIci",
            Description = "Focus source.",
            Summary = "Application code.",
            UpdatedUtc = DateTime.UtcNow
        });
        dbContext.Memories.AddRange(
            new MemoryEntry
            {
                Title = "Compare folder hashes with PowerShell",
                Summary = "Use Get-FileHash and relative paths to compare two directory trees.",
                Content = "Build a PowerShell diff that shows left-only, right-only, and changed files.",
                Kind = MemoryKind.Reference,
                SourceKind = SourceKind.ManualNote,
                UpdatedUtc = DateTime.UtcNow
            },
            new MemoryEntry
            {
                Title = "Local executable and application locations",
                Summary = "Verified workstation paths for PowerShell, git, and dotnet.",
                Content = "Reference locations for local task runners on this machine.",
                Kind = MemoryKind.Reference,
                SourceKind = SourceKind.ManualNote,
                UpdatedUtc = DateTime.UtcNow
            },
            new MemoryEntry
            {
                Title = "ADMT cross-forest migration conditional forwarder fix",
                Summary = "Forest and DNS repair guidance for a directory migration issue.",
                Content = "Recreate the conditional forwarder and validate domain resolution for ADMT.",
                Kind = MemoryKind.Incident,
                SourceKind = SourceKind.DebugSession,
                UpdatedUtc = DateTime.UtcNow
            });
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var service = new ContextService(dbContext);
        var pack = await service.BuildContextPackAsync(
            "How do I troubleshoot a local Windows PC that is running slow and having network issues?",
            CancellationToken.None);

        Assert.NotNull(pack);
        Assert.Empty(pack!.RecommendedSkills);
        Assert.Empty(pack.CodeGraphProjects);
        Assert.Empty(pack.CodeGraphFiles);
        Assert.Empty(pack.CodeGraphNodes);
    }

    [Fact]
    public async Task ContextService_FolderComparisonAutomationQueries_SuppressCodeGraphAndPreferFolderCompareSkill()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();

        await MemorySeeder.EnsureDatabaseAsync(dbContext, CancellationToken.None);

        dbContext.CodeGraphProjects.Add(new CodeGraphProject
        {
            Name = "Focus L-AIci",
            RootPath = @"C:\Copilot\Focus L-AIci",
            Description = "Focus source.",
            Summary = "Application code.",
            UpdatedUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var service = new ContextService(dbContext);
        var pack = await service.BuildContextPackAsync(
            "need a powershell script that will compare two different folders files and show the differences",
            CancellationToken.None);

        Assert.NotNull(pack);
        Assert.NotEmpty(pack!.RecommendedSkills);
        Assert.Equal("compare-folder-contents-with-powershell", pack.RecommendedSkills.First().Slug);
        Assert.DoesNotContain(pack.RecommendedSkills, skill => skill.Slug == "get-exchange-online-mailbox-inventory");
        Assert.DoesNotContain(pack.RecommendedSkills, skill => skill.Slug == "audit-on-prem-active-directory-user-attributes");
        Assert.True(pack.Memories.Count == 0 || pack.Memories.First().Title == "Compare folder hashes with PowerShell");
        Assert.DoesNotContain(pack.Memories, memory => memory.Title.Contains("Local executable", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(pack.Memories, memory => memory.Title.Contains("ADMT", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(pack.CodeGraphProjects);
        Assert.Empty(pack.CodeGraphFiles);
        Assert.Empty(pack.CodeGraphNodes);
    }

    [Fact]
    public async Task ContextService_ProjectHistoryQueries_PreferMatchedMemoryAndProjectAndSuppressGenericSkills()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();

        dbContext.Memories.AddRange(
            new MemoryEntry
            {
                Title = "Built the Sophos XGS desktop monitor in C:\\Copilot\\Sophos-XGS with encrypted credential storage, XML API polling, KPI dashboard, raw-tag explorer, desktop alerts, and dark mode support. Published output lives in the publish folder.",
                Summary = "Recent shipped state for the Sophos XGS desktop monitor project.",
                Content = "Sophos XGS build summary and current shipped state.",
                Kind = MemoryKind.Insight,
                SourceKind = SourceKind.ManualNote,
                UpdatedUtc = DateTime.UtcNow
            },
            new MemoryEntry
            {
                Title = "Dashboard should separate intent, active work, and durable knowledge",
                Summary = "Focus dashboard layout decision.",
                Content = "Homepage layout guidance for Focus.",
                Kind = MemoryKind.Decision,
                SourceKind = SourceKind.ManualNote,
                UpdatedUtc = DateTime.UtcNow.AddDays(-2)
            });
        dbContext.CodeGraphProjects.AddRange(
            new CodeGraphProject
            {
                Name = "Sophos XGS Monitor",
                RootPath = @"C:\Copilot\Sophos-XGS",
                Description = "Desktop monitoring app.",
                Summary = "Sophos XGS project.",
                UpdatedUtc = DateTime.UtcNow
            },
            new CodeGraphProject
            {
                Name = "Focus L-AIci",
                RootPath = @"C:\Copilot\Focus L-AIci",
                Description = "Local-first memory system.",
                Summary = "Focus project.",
                UpdatedUtc = DateTime.UtcNow
            });
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var service = new ContextService(dbContext);
        var pack = await service.BuildContextPackAsync(
            "What changed recently around \"Built the Sophos XGS desktop monitor in C:\\Copilot\\Sophos-XGS with encrypted credential storage, XML API polling, KPI dashboard, raw-tag explorer, desktop alerts, and dark mode support. Published outp\"?",
            CancellationToken.None);

        Assert.NotNull(pack);
        Assert.Empty(pack!.RecommendedSkills);
        Assert.NotEmpty(pack.Memories);
        Assert.Equal("Built the Sophos XGS desktop monitor in C:\\Copilot\\Sophos-XGS with encrypted credential storage, XML API polling, KPI dashboard, raw-tag explorer, desktop alerts, and dark mode support. Published output lives in the publish folder.", pack.Memories.First().Title);
        Assert.Single(pack.CodeGraphProjects);
        Assert.Equal("Sophos XGS Monitor", pack.CodeGraphProjects.First().Title);
        Assert.Empty(pack.CodeGraphFiles);
        Assert.Empty(pack.CodeGraphNodes);
    }

    [Fact]
    public async Task ContextService_WmiDiagnosticQueries_PreferLocalSupportSkillAndSuppressDirectorySkills()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();

        await MemorySeeder.EnsureDatabaseAsync(dbContext, CancellationToken.None);

        var service = new ContextService(dbContext);
        var pack = await service.BuildContextPackAsync(
            "create a powershell script that can check and see if wmi is working on a pc",
            CancellationToken.None);

        Assert.NotNull(pack);
        Assert.NotEmpty(pack!.RecommendedSkills);
        Assert.Equal("check-wmi-health-on-a-windows-pc", pack.RecommendedSkills.First().Slug);
        Assert.DoesNotContain(pack.RecommendedSkills, skill => skill.Slug == "get-exchange-online-mailbox-inventory");
        Assert.DoesNotContain(pack.RecommendedSkills, skill => skill.Slug == "audit-on-prem-active-directory-user-attributes");
        Assert.Empty(pack.Memories);
        Assert.Empty(pack.CodeGraphProjects);
        Assert.Empty(pack.CodeGraphFiles);
        Assert.Empty(pack.CodeGraphNodes);
    }

    [Fact]
    public async Task ContextService_WebUiQueries_PreferWebSkillsAndSuppressCodeGraphNoise()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();

        dbContext.Skills.AddRange(
            new SkillEntry
            {
                Name = "Review web design quality",
                Slug = "review-web-design-quality-test",
                Summary = "Review website UI layout and spacing quality.",
                Category = SkillCategory.Task,
                WhenToUse = "Use this for website layout, homepage, spacing, and CSS issues.",
                Flow = "Inspect layout.\nReview spacing.\nAdjust CSS.",
                TriggerHintsText = "website, web, ui, layout, homepage, spacing, css",
                IsPinned = true
            },
            new SkillEntry
            {
                Name = "Review endpoint uninstall and recovery flow",
                Slug = "endpoint-uninstall-flow-test",
                Summary = "Review endpoint uninstall behavior.",
                Category = SkillCategory.Product,
                WhenToUse = "Use this for endpoint uninstall work.",
                Flow = "Inspect uninstall flow.",
                TriggerHintsText = "endpoint, uninstall, recovery"
            });
        dbContext.CodeGraphProjects.Add(new CodeGraphProject
        {
            Name = "Grey Canary",
            RootPath = @"C:\Copilot\Grey Canary",
            Description = "Unrelated repo.",
            Summary = "Endpoint code.",
            UpdatedUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var service = new ContextService(dbContext);
        var pack = await service.BuildContextPackAsync(
            "Improve the website UI layout and fix spacing issues on the homepage.",
            CancellationToken.None);

        Assert.NotNull(pack);
        Assert.NotEmpty(pack!.RecommendedSkills);
        Assert.Equal("Review web design quality", pack.RecommendedSkills.First().Name);
        Assert.DoesNotContain(pack.RecommendedSkills, skill => skill.Name.Contains("endpoint", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(pack.CodeGraphProjects);
        Assert.Empty(pack.CodeGraphFiles);
        Assert.Empty(pack.CodeGraphNodes);
    }

    [Fact]
    public async Task ContextService_CloudQueries_PreferMicrosoftCloudSkills()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();

        dbContext.Skills.AddRange(
            new SkillEntry
            {
                Name = "Instrument App Insights telemetry",
                Slug = "instrument-app-insights-telemetry-test",
                Summary = "Add Azure Application Insights telemetry and deployment visibility.",
                Category = SkillCategory.System,
                WhenToUse = "Use this for Azure deployment, telemetry, identity, and cloud observability work.",
                Flow = "Review Azure deployment.\nAdd telemetry.\nVerify identity and monitoring.",
                TriggerHintsText = "azure, cloud, deployment, identity, app insights, telemetry",
                IsPinned = true
            },
            new SkillEntry
            {
                Name = "Review web design quality",
                Slug = "review-web-design-quality-cloud-test",
                Summary = "Review website UI layout and spacing quality.",
                Category = SkillCategory.Task,
                WhenToUse = "Use this for website layout.",
                Flow = "Inspect UI.",
                TriggerHintsText = "website, web, ui, layout"
            });
        dbContext.CodeGraphProjects.Add(new CodeGraphProject
        {
            Name = "Simple SMB Tester",
            RootPath = @"C:\Copilot\Simple SMB Tester",
            Description = "Unrelated repo.",
            Summary = "Desktop utility source code.",
            UpdatedUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var service = new ContextService(dbContext);
        var pack = await service.BuildContextPackAsync(
            "Help me work on Azure deployment and identity integration.",
            CancellationToken.None);

        Assert.NotNull(pack);
        Assert.NotEmpty(pack!.RecommendedSkills);
        Assert.Equal("Instrument App Insights telemetry", pack.RecommendedSkills.First().Name);
        Assert.Empty(pack.CodeGraphProjects);
        Assert.Empty(pack.CodeGraphFiles);
        Assert.Empty(pack.CodeGraphNodes);
    }

    [Fact]
    public async Task ContextService_CloudServiceQueries_DoNotPromoteGenericServiceWordToCodeIntent()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();

        dbContext.Skills.Add(new SkillEntry
        {
            Name = "Instrument App Insights telemetry",
            Slug = "instrument-app-insights-telemetry-service-test",
            Summary = "Add Azure Application Insights telemetry and deployment visibility.",
            Category = SkillCategory.System,
            WhenToUse = "Use this for Azure deployment, telemetry, identity, and cloud observability work.",
            Flow = "Review Azure deployment.\nAdd telemetry.\nVerify identity and monitoring.",
            TriggerHintsText = "azure, cloud, deployment, identity, app insights, telemetry",
            IsPinned = true
        });
        dbContext.CodeGraphProjects.Add(new CodeGraphProject
        {
            Name = "Focus L-AIci",
            RootPath = @"C:\Copilot\Focus L-AIci",
            Description = "Application code.",
            Summary = "Repo source code.",
            UpdatedUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var service = new ContextService(dbContext);
        var pack = await service.BuildContextPackAsync(
            "Instrument Azure App Insights telemetry for this service.",
            CancellationToken.None);

        Assert.NotNull(pack);
        Assert.NotEmpty(pack!.RecommendedSkills);
        Assert.Equal("Instrument App Insights telemetry", pack.RecommendedSkills.First().Name);
        Assert.Empty(pack.CodeGraphProjects);
        Assert.Empty(pack.CodeGraphFiles);
        Assert.Empty(pack.CodeGraphNodes);
    }

    [Fact]
    public async Task ContextService_DesktopAppQueries_PreferDotnetDesktopSkills()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();

        dbContext.Skills.AddRange(
            new SkillEntry
            {
                Name = "Apply .NET best practices",
                Slug = "apply-dotnet-best-practices-desktop-test",
                Summary = "Review .NET and C# desktop code for maintainability and correctness.",
                Category = SkillCategory.System,
                WhenToUse = "Use this for Windows Forms, desktop app, .NET, and C# work.",
                Flow = "Review .NET structure.\nCheck Windows Forms event handling.\nTighten desktop app patterns.",
                TriggerHintsText = "dotnet, csharp, windows forms, desktop app, winforms",
                IsPinned = true
            },
            new SkillEntry
            {
                Name = "Review web design quality",
                Slug = "review-web-design-quality-desktop-test",
                Summary = "Review website UI layout.",
                Category = SkillCategory.Task,
                WhenToUse = "Use this for website layout.",
                Flow = "Inspect web UI.",
                TriggerHintsText = "website, web, ui, layout"
            });
        dbContext.CodeGraphProjects.Add(new CodeGraphProject
        {
            Name = "Focus L-AIci",
            RootPath = @"C:\Copilot\Focus L-AIci",
            Description = "Unrelated repo.",
            Summary = "Web application code.",
            UpdatedUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var service = new ContextService(dbContext);
        var pack = await service.BuildContextPackAsync(
            "Help me build and improve a Windows Forms desktop app.",
            CancellationToken.None);

        Assert.NotNull(pack);
        Assert.NotEmpty(pack!.RecommendedSkills);
        Assert.Equal("Apply .NET best practices", pack.RecommendedSkills.First().Name);
        Assert.DoesNotContain(pack.RecommendedSkills, skill => skill.Name.Contains("web design", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(pack.CodeGraphProjects);
        Assert.Empty(pack.CodeGraphFiles);
        Assert.Empty(pack.CodeGraphNodes);
    }

    [Fact]
    public async Task QuickCaptureAsync_CreatesMemoryWithDerivedTags()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new PalaceService(serviceContext);

        var wingId = await service.CreateWingAsync(new WingEditorInput
        {
            Name = "Capture",
            Description = "Quick capture coverage."
        }, CancellationToken.None);

        var id = await service.QuickCaptureAsync(new QuickCaptureInput
        {
            RawText = "Installer deployment reliability follow-up\nNeed to verify token registration and retry behavior.",
            Kind = MemoryKind.Incident,
            SourceKind = SourceKind.ChatSession,
            WingId = wingId,
            IsPinned = true
        }, CancellationToken.None);

        var memory = await service.GetMemoryAsync(id, CancellationToken.None);
        Assert.NotNull(memory);
        Assert.Equal("Installer deployment reliability follow-up", memory!.Memory.Title);
        Assert.Contains("installer", memory.Memory.Tags);
        Assert.Contains("deployment", memory.Memory.Tags);
        Assert.True(memory.Memory.IsPinned);
    }

    [Fact]
    public async Task FindDuplicateSuggestionsAsync_ReturnsLikelyDuplicate()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new PalaceService(serviceContext);

        await service.SaveMemoryAsync(new MemoryEditorInput
        {
            Title = "Installer token retry behavior",
            Summary = "Capture how installer registration retries behave.",
            Content = "Installer token registration should retry cleanly after transient network failures.",
            Kind = MemoryKind.Reference,
            SourceKind = SourceKind.DebugSession,
            Importance = 4,
            TagsText = "installer, token, retry"
        }, CancellationToken.None);

        var suggestions = await service.FindDuplicateSuggestionsAsync(new MemoryEditorInput
        {
            Title = "Installer token retry behavior",
            Summary = "How registration retries behave after a transient failure.",
            Content = "Installer token registration should retry after transient network failures."
        }, CancellationToken.None);

        var suggestion = Assert.Single(suggestions);
        Assert.Equal("Likely duplicate", suggestion.ScoreLabel);
    }

    [Fact]
    public async Task DashboardDiagnostics_ReportsEmptyStateGaps()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new PalaceService(serviceContext);

        var diagnostics = await service.GetDashboardDiagnosticsAsync(null, CancellationToken.None);

        Assert.Contains(diagnostics.DetectedGaps, gap => gap.Contains("No active todos or tickets", StringComparison.Ordinal));
        Assert.Contains(diagnostics.DetectedGaps, gap => gap.Contains("No recent activity", StringComparison.Ordinal));
        Assert.Contains(diagnostics.DetectedGaps, gap => gap.Contains("No pinned memories", StringComparison.Ordinal));
        Assert.Contains(diagnostics.DetectedGaps, gap => gap.Contains("No context question", StringComparison.Ordinal));
        Assert.All(diagnostics.Sections, section => Assert.Equal(0, section.Count));
    }

    [Fact]
    public async Task DashboardDiagnostics_SurfacesStructuredSectionContent()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new PalaceService(serviceContext);

        var wingId = await service.CreateWingAsync(new WingEditorInput
        {
            Name = "Diagnostics Wing",
            Description = "Primary structured memory area."
        }, CancellationToken.None);

        var roomId = await service.CreateRoomAsync(new RoomEditorInput
        {
            WingId = wingId,
            Name = "Investigations",
            Description = "Debugging and analysis notes."
        }, CancellationToken.None);

        await service.SaveMemoryAsync(new MemoryEditorInput
        {
            Title = "Installer investigation baseline",
            Summary = "Pinned baseline memory for diagnostics.",
            Content = "Installer diagnostics should check the active database and the dashboard sections.",
            Kind = MemoryKind.Decision,
            SourceKind = SourceKind.DebugSession,
            Importance = 5,
            IsPinned = true,
            WingId = wingId,
            RoomId = roomId,
            TagsText = "installer, diagnostics"
        }, CancellationToken.None);

        await service.CreateTodoAsync(new TodoEditorInput
        {
            Title = "Inspect dashboard API output",
            Details = "Use the new diagnostics endpoint before trusting the UI.",
            Status = TodoStatus.InProgress
        }, CancellationToken.None);

        var diagnostics = await service.GetDashboardDiagnosticsAsync(new ContextBriefInput
        {
            Question = "installer diagnostics",
            ResultsPerSection = 3
        }, CancellationToken.None);

        var todoSection = Assert.Single(diagnostics.Sections, section => section.Key == "current-todos");
        var pinnedSection = Assert.Single(diagnostics.Sections, section => section.Key == "pinned-memories");
        var wingsSection = Assert.Single(diagnostics.Sections, section => section.Key == "wings");
        var contextSection = Assert.Single(diagnostics.Sections, section => section.Key == "top-context-matches");

        Assert.Equal(1, todoSection.Count);
        Assert.Equal("Inspect dashboard API output", todoSection.Items.Single().Title);
        Assert.Equal(1, pinnedSection.Count);
        Assert.Equal("Installer investigation baseline", pinnedSection.Items.Single().Title);
        Assert.Equal(1, wingsSection.Count);
        Assert.Equal("Diagnostics Wing", wingsSection.Items.Single().Title);
        Assert.True(contextSection.Count >= 1);
        Assert.DoesNotContain(diagnostics.DetectedGaps, gap => gap.Contains("No context question", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DashboardContextPack_SurfacesStructuredProvenance()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new PalaceService(serviceContext);

        var wingId = await service.CreateWingAsync(new WingEditorInput
        {
            Name = "Context",
            Description = "Context retrieval coverage."
        }, CancellationToken.None);

        await service.SaveMemoryAsync(new MemoryEditorInput
        {
            Title = "Installer diagnostics baseline",
            Summary = "Use diagnostics before trusting the UI.",
            Content = "The inspect workflow should verify installer diagnostics and workspace export before acting.",
            Kind = MemoryKind.Decision,
            SourceKind = SourceKind.DebugSession,
            Importance = 5,
            IsPinned = true,
            WingId = wingId,
            TagsText = "installer, diagnostics, inspect"
        }, CancellationToken.None);

        var dashboard = await service.GetDashboardAsync(new ContextBriefInput
        {
            Question = "installer diagnostics",
            ResultsPerSection = 3
        }, CancellationToken.None);

        Assert.NotEmpty(dashboard.ContextPack!.TopMatches);
        var firstMatch = dashboard.ContextPack.TopMatches.First();
        var provenance = Assert.IsType<ContextMatchDetailViewModel>(firstMatch.Provenance);

        Assert.Contains("installer", provenance.MatchedTokens);
        Assert.Contains(provenance.FieldHits, hit => hit.FieldKey == "title" && hit.Tokens.Contains("installer"));
        Assert.Contains(provenance.Boosts, boost => boost.Label == "Pinned memory");
        Assert.Contains(provenance.Boosts, boost => boost.Label == "Token coverage");
        Assert.True(provenance.ExactPhraseMatched);
    }

    [Fact]
    public async Task SearchMemoriesAsync_CanFilterByUpdatedSince()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();

        dbContext.Memories.AddRange(
            new MemoryEntry
            {
                Title = "Older memory",
                Summary = "Past work",
                Content = "This should be filtered out.",
                UpdatedUtc = DateTime.UtcNow.AddDays(-10),
                Wing = new Wing
                {
                    Name = "Archive",
                    Slug = "archive",
                    Description = "Older memories."
                }
            },
            new MemoryEntry
            {
                Title = "Recent memory",
                Summary = "Fresh work",
                Content = "This should remain visible.",
                UpdatedUtc = DateTime.UtcNow.AddHours(-2),
                Wing = new Wing
                {
                    Name = "Current",
                    Slug = "current",
                    Description = "Current memories."
                }
            });
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var service = new PalaceService(dbContext);
        var results = await service.SearchMemoriesAsync(null, null, null, null, null, DateTime.UtcNow.AddDays(-1), CancellationToken.None);

        var memory = Assert.Single(results);
        Assert.Equal("Recent memory", memory.Title);
    }

    [Fact]
    public async Task DashboardAndRecentChanges_SurfaceWarningsAndCrossSourceHistory()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();

        var now = DateTime.UtcNow;
        var wing = new Wing
        {
            Name = "Inspect",
            Slug = "inspect",
            Description = "Inspection wing."
        };

        dbContext.Wings.Add(wing);
        dbContext.Memories.Add(new MemoryEntry
        {
            Title = "Recent memory item",
            Summary = "Fresh memory context.",
            Content = "Stored recently for inspection.",
            UpdatedUtc = now.AddMinutes(-20),
            Wing = wing
        });
        dbContext.Todos.Add(new TodoEntry
        {
            Title = "Investigate missing pinned memory",
            Details = "Open task without a pinned memory should surface a warning.",
            Status = TodoStatus.InProgress,
            UpdatedUtc = now.AddMinutes(-10)
        });
        dbContext.Tickets.Add(new TicketEntry
        {
            TicketNumber = "TKT-0100",
            Title = "Inspect API history",
            Description = "Verify the recent changes feed.",
            Status = TicketStatus.InProgress,
            Priority = TicketPriority.Medium,
            Assignee = "Copilot",
            UpdatedUtc = now.AddMinutes(-5)
        });
        dbContext.CodeGraphProjects.Add(new CodeGraphProject
        {
            Name = "Focus repo",
            RootPath = @"C:\Copilot\Focus L-AIci",
            Summary = "Project scan",
            FileCount = 25,
            SymbolCount = 100,
            RelationshipCount = 120,
            UpdatedUtc = now.AddMinutes(-15),
            LastScannedUtc = now.AddMinutes(-3)
        });
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var service = new PalaceService(dbContext);
        var dashboard = await service.GetDashboardAsync(CancellationToken.None);
        var changes = await service.GetRecentChangesAsync(10, CancellationToken.None);

        Assert.Contains(dashboard.MissingContextWarnings, warning => warning.Contains("No pinned memories", StringComparison.Ordinal));
        Assert.Contains(dashboard.MissingContextWarningItems, warning => warning.Code == "no-pinned-memories" && warning.ActionUrl == "/Palace/NewMemory");
        Assert.Contains(changes, change => change.Kind == "Memory");
        Assert.Contains(changes, change => change.Kind == "Todo");
        Assert.Contains(changes, change => change.Kind == "Ticket");
        Assert.Contains(changes, change => change.Kind == "Code graph");
        Assert.Equal("Focus repo", changes.First().Title);
    }

    [Fact]
    public async Task Dashboard_WarnsWhenPinnedMemoryTrustHasRotated()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();

        dbContext.Wings.Add(new Wing
        {
            Name = "Trust",
            Slug = "trust",
            Description = "Trust state wing."
        });
        dbContext.Memories.Add(new MemoryEntry
        {
            Title = "Pinned but stale",
            Summary = "This pinned memory is overdue for review.",
            Content = "Stale content.",
            IsPinned = true,
            VerificationStatus = MemoryVerificationStatus.NeedsReview,
            ReviewAfterUtc = DateTime.UtcNow.AddDays(-2),
            UpdatedUtc = DateTime.UtcNow.AddDays(-100)
        });
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var service = new PalaceService(dbContext);
        var dashboard = await service.GetDashboardAsync(CancellationToken.None);

        Assert.Contains(dashboard.MissingContextWarningItems, warning => warning.Code == "stale-pinned-memories");
    }

    [Fact]
    public async Task Dashboard_DoesNotWarnForFreshUnverifiedMemories()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();

        dbContext.Wings.Add(new Wing
        {
            Name = "Fresh",
            Slug = "fresh",
            Description = "Fresh context wing."
        });
        dbContext.Memories.AddRange(
            new MemoryEntry
            {
                Title = "Pinned and fresh",
                Summary = "Fresh pinned memory should not be treated as stale immediately.",
                Content = "Fresh pinned content.",
                IsPinned = true,
                VerificationStatus = MemoryVerificationStatus.Unverified,
                UpdatedUtc = DateTime.UtcNow.AddDays(-2)
            },
            new MemoryEntry
            {
                Title = "Recent and fresh",
                Summary = "Recent memory should not trigger aging warning immediately.",
                Content = "Fresh recent content.",
                VerificationStatus = MemoryVerificationStatus.Unverified,
                UpdatedUtc = DateTime.UtcNow.AddDays(-1)
            });
        await dbContext.SaveChangesAsync(CancellationToken.None);

        var service = new PalaceService(dbContext);
        var dashboard = await service.GetDashboardAsync(CancellationToken.None);

        Assert.DoesNotContain(dashboard.MissingContextWarningItems, warning => warning.Code == "stale-pinned-memories");
        Assert.DoesNotContain(dashboard.MissingContextWarningItems, warning => warning.Code == "aging-unverified-memories");
    }

    [Fact]
    public async Task Dashboard_UsesFallbackContextWhenQuestionAndActiveWorkAreMissing()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new PalaceService(serviceContext);

        var wingId = await service.CreateWingAsync(new WingEditorInput
        {
            Name = "Fallback",
            Description = "Fallback dashboard context coverage."
        }, CancellationToken.None);

        await service.SaveMemoryAsync(new MemoryEditorInput
        {
            Title = "Installer diagnostics follow-up",
            Summary = "Recent work should become fallback dashboard context.",
            Content = "Use recent diagnostics memory when no explicit task prompt is present.",
            Kind = MemoryKind.Decision,
            SourceKind = SourceKind.DebugSession,
            Importance = 4,
            IsPinned = true,
            WingId = wingId,
            TagsText = "installer, diagnostics, fallback"
        }, CancellationToken.None);

        var dashboard = await service.GetDashboardAsync(CancellationToken.None);
        var diagnostics = await service.GetDashboardDiagnosticsAsync(null, CancellationToken.None);

        Assert.NotNull(dashboard.FallbackContext);
        Assert.True(dashboard.FallbackContext!.WasApplied);
        Assert.False(string.IsNullOrWhiteSpace(dashboard.FallbackContext.SuggestedQuestion));
        Assert.NotNull(dashboard.ContextPack);
        Assert.NotEmpty(dashboard.ContextPack!.TopMatches);
        Assert.DoesNotContain(dashboard.MissingContextWarningItems, warning => warning.Code == "no-active-work");
        Assert.DoesNotContain(dashboard.MissingContextWarningItems, warning => warning.Code == "no-context-question");
        Assert.NotNull(diagnostics.FallbackContext);
        Assert.True(diagnostics.FallbackContext!.WasApplied);
        Assert.DoesNotContain(diagnostics.DetectedGaps, gap => gap.Contains("No active todos or tickets", StringComparison.Ordinal));
        Assert.DoesNotContain(diagnostics.DetectedGaps, gap => gap.Contains("No context question", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WorkspaceExport_IncludesCurrentOperationalContext()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new PalaceService(serviceContext);

        var wingId = await service.CreateWingAsync(new WingEditorInput
        {
            Name = "Export",
            Description = "Workspace export coverage."
        }, CancellationToken.None);

        await service.SaveMemoryAsync(new MemoryEditorInput
        {
            Title = "Export baseline memory",
            Summary = "Pinned memory should show up in the workspace export.",
            Content = "Operators need a one-shot workspace export for cold-start AI sessions.",
            Kind = MemoryKind.Reference,
            SourceKind = SourceKind.Architecture,
            Importance = 4,
            IsPinned = true,
            WingId = wingId,
            TagsText = "workspace, export"
        }, CancellationToken.None);

        await service.CreateTodoAsync(new TodoEditorInput
        {
            Title = "Ship workspace export",
            Details = "Expose the current operating picture through the API and Inspect page.",
            Status = TodoStatus.InProgress
        }, CancellationToken.None);

        var ticketingService = new TicketingService(serviceContext);
        await ticketingService.CreateTicketAsync(new TicketEditorInput
        {
            Title = "Add write-back API coverage",
            Description = "Close the read/write loop for AI-assisted workflows.",
            Status = TicketStatus.InProgress,
            Priority = TicketPriority.High,
            Assignee = "Copilot",
            TagsText = "api, focus"
        }, CancellationToken.None);

        var export = await service.GetWorkspaceExportAsync(CancellationToken.None);

        Assert.Single(export.PinnedMemories);
        Assert.Single(export.ActiveTodos);
        Assert.Single(export.ActiveTickets);
        Assert.Contains("Export baseline memory", export.ExportText);
        Assert.Contains("Ship workspace export", export.ExportText);
        Assert.Contains("Add write-back API coverage", export.ExportText);
    }

    [Fact]
    public async Task WorkspaceExport_AnnotatesMemoryTrustState()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var serviceContext = harness.CreateDbContext();
        var service = new PalaceService(serviceContext);

        var wingId = await service.CreateWingAsync(new WingEditorInput
        {
            Name = "Trust export",
            Description = "Workspace export trust coverage."
        }, CancellationToken.None);

        var memoryId = await service.SaveMemoryAsync(new MemoryEditorInput
        {
            Title = "Unverified export memory",
            Summary = "Should be annotated in workspace export.",
            Content = "Old unverified memory content.",
            Kind = MemoryKind.Reference,
            SourceKind = SourceKind.Research,
            Importance = 3,
            IsPinned = true,
            WingId = wingId,
            TagsText = "trust, export"
        }, CancellationToken.None);

        await using (var updateContext = harness.CreateDbContext())
        {
            var memory = await updateContext.Memories.FirstAsync(x => x.Id == memoryId, CancellationToken.None);
            memory.UpdatedUtc = DateTime.UtcNow.AddDays(-45);
            await updateContext.SaveChangesAsync(CancellationToken.None);
        }

        var export = await service.GetWorkspaceExportAsync(CancellationToken.None);

        Assert.Contains("Unverified export memory [Unverified]", export.ExportText);
    }

    private sealed class TestHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private TestHarness(ServiceProvider services, SqliteConnection connection)
        {
            Services = services;
            _connection = connection;
        }

        public ServiceProvider Services { get; }

        public static async Task<TestHarness> CreateAsync(string? contentRootPath = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var serviceCollection = new ServiceCollection();
            var resolvedContentRootPath = contentRootPath ?? AppContext.BaseDirectory;
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:FocusPalace"] = "Data Source=:memory:"
                })
                .Build();
            serviceCollection.AddSingleton<IConfiguration>(configuration);
            serviceCollection.AddSingleton<IHostEnvironment>(new TestHostEnvironment
            {
                ContentRootPath = resolvedContentRootPath,
                ContentRootFileProvider = new PhysicalFileProvider(resolvedContentRootPath)
            });
            serviceCollection.AddSingleton<FocusDatabaseTargetService>();
            serviceCollection.AddSingleton<FocusAgentCatalogService>();
            serviceCollection.AddSingleton<RepoSkillCatalogService>();
            serviceCollection.AddDbContext<FocusMemoryContext>(options => options.UseSqlite(connection));
            serviceCollection.AddScoped<IFocusEventPublisher>(_ => NullFocusEventPublisher.Instance);
            serviceCollection.AddScoped<ContextService>();
            serviceCollection.AddScoped<PalaceService>();
            serviceCollection.AddScoped<TicketingService>();
            serviceCollection.AddScoped<SiteSettingsService>();

            var services = serviceCollection.BuildServiceProvider();

            await using var scope = services.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<FocusMemoryContext>();
            await dbContext.Database.EnsureCreatedAsync();

            return new TestHarness(services, connection);
        }

        public FocusMemoryContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<FocusMemoryContext>()
                .UseSqlite(_connection)
                .Options;

            return new FocusMemoryContext(options);
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "FocusLAIci.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(AppContext.BaseDirectory);
    }

    [Fact]
    public async Task RepoSkills_AreSurfacedAcrossCatalogDetailAndRecommendations()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"focus-repo-skills-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(contentRoot, ".agents", "skills", "repo-ui-review"));
        File.WriteAllText(
            Path.Combine(contentRoot, ".agents", "skills", "repo-ui-review", "SKILL.md"),
            """
            ---
            name: repo-ui-review
            description: Review website UI and layout regressions. Triggers on requests like "review the UI" and "fix broken layout".
            ---

            # Repo UI Review

            ## When to use

            - UI looks broken
            - Layout regressed after a change

            ## Workflow

            1. Open the affected page.
            2. Compare layout and spacing.
            3. Apply the minimal source fix.

            ## Examples

            - Review the UI for layout regressions.
            """);

        try
        {
            await using var harness = await TestHarness.CreateAsync(contentRoot);
            await using var scope = harness.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<PalaceService>();

            var catalog = await service.GetSkillCatalogAsync("layout", null, null, false, false, CancellationToken.None);
            var detail = await service.GetSkillAsync("repo-ui-review", CancellationToken.None);
            var recommendations = await service.RecommendSkillsAsync("review the UI layout", null, null, 3, CancellationToken.None);

            Assert.Contains(catalog.Skills, x => x.Slug == "repo-ui-review" && x.IsReadOnly);
            Assert.NotNull(detail);
            Assert.True(detail!.Skill.IsReadOnly);
            Assert.Equal("Project skill", detail.Skill.SourceLabel);
            Assert.Contains(".agents", detail.Skill.SourcePath, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Open the affected page.", detail.FlowSteps);
            Assert.Contains(recommendations, x => x.Slug == "repo-ui-review");
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task DashboardStats_SkillCountUsesMergedVisibleSkillTotal()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), $"focus-repo-skill-stats-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(contentRoot, ".agents", "skills", "repo-skill-one"));
        File.WriteAllText(
            Path.Combine(contentRoot, ".agents", "skills", "repo-skill-one", "SKILL.md"),
            """
            ---
            name: repo-skill-one
            description: First repo skill.
            ---

            # Repo Skill One
            """);
        Directory.CreateDirectory(Path.Combine(contentRoot, ".agents", "skills", "repo-skill-two"));
        File.WriteAllText(
            Path.Combine(contentRoot, ".agents", "skills", "repo-skill-two", "SKILL.md"),
            """
            ---
            name: repo-skill-two
            description: Second repo skill.
            ---

            # Repo Skill Two
            """);

        try
        {
            await using var harness = await TestHarness.CreateAsync(contentRoot);
            await using var scope = harness.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<PalaceService>();

            await service.SaveSkillAsync(new SkillEditorInput
            {
                Name = "Stored skill",
                Summary = "Database-backed skill.",
                Category = SkillCategory.Task,
                WhenToUse = "Use when needed.",
                Flow = "Do the thing.",
                ExamplesText = "Use the stored skill.",
                TriggerHintsText = "stored",
                IsPinned = true
            }, CancellationToken.None);

            var dashboard = await service.GetDashboardAsync(CancellationToken.None);

            Assert.Equal(3, dashboard.Stats.SkillCount);
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ExternalSkillSuggestionService_BuildAlertAsync_SuggestsMatchingCatalogSkills()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();
        dbContext.ExternalSkillSources.Add(new ExternalSkillSource
        {
            Name = "Example Skills",
            CatalogUrl = "https://example.com/catalog",
            Description = "Catalog",
            IsEnabled = true
        });
        await dbContext.SaveChangesAsync();

        using var httpClient = new HttpClient(new StubHttpMessageHandler(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://example.com/catalog"] = """
                [Azure deployment skill](https://example.com/skills/azure-deployment-skill.md)
                [Desktop support skill](https://example.com/skills/desktop-support-skill.md)
                """
        }));

        var service = new ExternalSkillSuggestionService(dbContext, httpClient);
        var alert = await service.BuildAlertAsync(new ContextPackViewModel
        {
            Question = "improve azure deployment reliability",
            SearchTokens = ["azure", "deployment", "reliability"]
        }, CancellationToken.None);

        Assert.True(alert.HasSuggestions);
        Assert.Contains(alert.Suggestions, x => x.Name.Contains("Azure", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("external skill", alert.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExternalSkillSuggestionService_BuildAlertAsync_ShowsGuidanceWhenExchangeSkillSourcesDoNotMatchYet()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();
        dbContext.ExternalSkillSources.Add(new ExternalSkillSource
        {
            Name = "Example Skills",
            CatalogUrl = "https://example.com/catalog",
            Description = "Catalog",
            IsEnabled = true
        });
        await dbContext.SaveChangesAsync();

        using var httpClient = new HttpClient(new StubHttpMessageHandler(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://example.com/catalog"] = """
                [Azure deployment skill](https://example.com/skills/azure-deployment-skill.md)
                [Desktop support skill](https://example.com/skills/desktop-support-skill.md)
                """
        }));

        var service = new ExternalSkillSuggestionService(dbContext, httpClient);
        var alert = await service.BuildAlertAsync(new ContextPackViewModel
        {
            Question = "need to create a powershell script to get all mailboxes and their types from exchange online",
            SearchTokens = ["powershell", "mailboxes", "exchange", "online"]
        }, CancellationToken.None);

        Assert.True(alert.HasAlert);
        Assert.False(alert.HasSuggestions);
        Assert.Contains("Exchange Online PowerShell", alert.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExternalSkillSuggestionService_StripsHtmlNoiseFromCatalogLabels()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();
        dbContext.ExternalSkillSources.Add(new ExternalSkillSource
        {
            Name = "skills.sh",
            CatalogUrl = "https://www.skills.sh/",
            Description = "Catalog",
            IsEnabled = true
        });
        await dbContext.SaveChangesAsync();

        using var httpClient = new HttpClient(new StubHttpMessageHandler(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://www.skills.sh/"] = """
                <a href="https://www.skills.sh/coreyhaines31/marketingskills/cold-email"><div class="lg:col-span-1 text-left"><span class="text-sm lg:text-base text-(--ds-gray-600) font-mono">232</span></div><div class="lg:col-span-13 min-w-1 flex flex-col lg:flex-row lg:items-baseline lg:gap-2"><h3 class="font-semibold text-foreground truncate whitespace-nowrap">cold-email</h3><p class="text-xs lg:text-sm text-(--ds-gray-600) font-mono mt-0.5 lg:mt-0 truncate">coreyhaines31/marketingskills</p></div><div class="lg:col-span-2 text-right flex items-center justify-end gap-2"><span class="font-mono text-sm text-foreground">48.7K</span></div></a>
                """
        }));

        var service = new ExternalSkillSuggestionService(dbContext, httpClient);
        var suggestions = await service.SuggestSkillsAsync("write a cold email campaign", ["cold", "email"], 6, CancellationToken.None);

        Assert.NotEmpty(suggestions);
        Assert.Equal("cold email", suggestions.First().Name);
        Assert.DoesNotContain("<div", suggestions.First().Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExternalSkillSuggestionService_ImportSuggestionAsync_PersistsImportedSkill()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();

        using var httpClient = new HttpClient(new StubHttpMessageHandler(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://raw.githubusercontent.com/example/repo/main/skills/azure-review.md"] = """
                ---
                name: Azure deployment review
                description: Review Azure deployment reliability and rollout safety.
                ---
                # Azure deployment review

                ## When to Use
                Use this when Azure releases or slots are failing.

                ## Workflow
                Check the deployment slot state.
                Review App Insights.

                ## Examples
                Review why the Azure release keeps failing.
                """
        }));

        var service = new ExternalSkillSuggestionService(dbContext, httpClient);
        var skill = await service.ImportSuggestionAsync("https://raw.githubusercontent.com/example/repo/main/skills/azure-review.md", "Example Skills", CancellationToken.None);

        Assert.Equal("azure-deployment-review", skill.Slug);
        Assert.Equal("Azure deployment review", skill.Name);
        Assert.Contains("Azure", skill.Summary);
        Assert.Equal(1, await dbContext.Skills.CountAsync(x => x.Slug == "azure-deployment-review"));
    }

    [Fact]
    public async Task PackBuildArchiveService_RecordAsync_PersistsBuildSummary()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();
        var archiveService = new PackBuildArchiveService(dbContext);

        var buildId = await archiveService.RecordAsync(new ContextPackViewModel
        {
            Question = "review azure deployment",
            GoalLabel = "Delivery",
            Summary = "1 memory, 1 skill",
            Input = new ContextBriefInput
            {
                Question = "review azure deployment",
                ResultsPerSection = 6
            },
            SearchTokens = ["azure", "deployment"],
            RecommendedSkills =
            [
                new SkillCardViewModel
                {
                    Id = Guid.NewGuid(),
                    Name = "Azure deployment review"
                }
            ],
            ExportText = "context pack export"
        }, CancellationToken.None);

        var record = await dbContext.PackBuildRecords.SingleAsync(x => x.Id == buildId);
        Assert.Equal("review azure deployment", record.Question);
        Assert.Equal("Delivery", record.GoalLabel);
        Assert.Equal(1, record.RecommendedSkillCount);
        Assert.Contains("azure", record.SearchTokensJson, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateCodeGraphFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), $"focus-code-graph-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        File.WriteAllText(
            Path.Combine(root, "AlphaService.cs"),
            """
            namespace Demo.Core;

            public class AlphaService
            {
                public string Render()
                {
                    return "ok";
                }

                public int Count { get; set; }
            }
            """);

        File.WriteAllText(
            Path.Combine(root, "BetaRunner.cs"),
            """
            using Demo.Core;

            namespace Demo.App;

            public class BetaRunner
            {
                private readonly AlphaService _alpha = new();

                public void Execute()
                {
                    _alpha.Render();
                }
            }
            """);

        return root;
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException)
            {
                if (attempt == 4)
                {
                    return;
                }

                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException)
            {
                if (attempt == 4)
                {
                    return;
                }

                Thread.Sleep(100);
            }
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, string> _responses;

        public StubHttpMessageHandler(IReadOnlyDictionary<string, string> responses)
        {
            _responses = responses;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!_responses.TryGetValue(request.RequestUri!.ToString(), out var content))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    RequestMessage = request
                });
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            return Task.FromResult(response);
        }
    }
}
