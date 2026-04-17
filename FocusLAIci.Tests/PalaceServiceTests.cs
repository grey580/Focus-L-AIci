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
        var byTag = await service.SearchMemoriesAsync(null, null, null, null, "security", CancellationToken.None);
        var byQuery = await service.SearchMemoriesAsync("rotate secrets", null, null, null, null, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("Grey Canary", detail!.Memory.WingName);
        Assert.Equal("Endpoint Installer", detail.Memory.RoomName);
        Assert.Contains("security", detail.Memory.Tags);
        Assert.Contains(byTag, memory => memory.Id == memoryId);
        Assert.Contains(byQuery, memory => memory.Id == memoryId);
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

        var model = await service.GetVisualizerAsync(CancellationToken.None);

        var wing = Assert.Single(model.Wings);
        var room = Assert.Single(wing.Rooms);
        var memory = Assert.Single(room.Memories);

        Assert.Equal("Architecture", wing.Name);
        Assert.Equal("Runtime", room.Name);
        Assert.Equal(memoryId, memory.Id);
        Assert.Contains(model.Tags, tag => tag.Slug == "visualizer" && tag.MemoryCount == 1);
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
        }
        finally
        {
            TryDeleteDirectory(contentRoot);
        }
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
