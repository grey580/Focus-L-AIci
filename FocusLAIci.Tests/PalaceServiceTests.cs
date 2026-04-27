using FocusLAIci.Web.Data;
using FocusLAIci.Web.Models;
using FocusLAIci.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace FocusLAIci.Tests;

public sealed class PalaceServiceTests
{
    [Fact]
    public void CreateSlug_NormalizesReadableSlugs()
    {
        var slug = SlugUtility.CreateSlug("  Focus L-AIci: Installer & UI  ");

        Assert.Equal("focus-l-aici-installer-ui", slug);
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

        public static async Task<TestHarness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var serviceCollection = new ServiceCollection();
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:FocusPalace"] = "Data Source=:memory:"
                })
                .Build();
            serviceCollection.AddSingleton<IConfiguration>(configuration);
            serviceCollection.AddSingleton<IHostEnvironment>(new TestHostEnvironment
            {
                ContentRootPath = AppContext.BaseDirectory
            });
            serviceCollection.AddSingleton<FocusDatabaseTargetService>();
            serviceCollection.AddDbContext<FocusMemoryContext>(options => options.UseSqlite(connection));
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
}
