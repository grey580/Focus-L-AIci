using FocusLAIci.Web.Data;
using FocusLAIci.Web.Models;
using FocusLAIci.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        var settingsService = new SiteSettingsService(settingsContext);
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
            serviceCollection.AddDbContext<FocusMemoryContext>(options => options.UseSqlite(connection));
            serviceCollection.AddScoped<PalaceService>();
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
}
