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

public sealed class ContextHybridSearchTests
{
    [Fact]
    public async Task ContextService_DoesNotSurfaceSemanticOnlyMemoryWithoutLexicalGrounding()
    {
        await using var harness = await TestHarness.CreateAsync();
        await using var dbContext = harness.CreateDbContext();

        dbContext.Memories.Add(new MemoryEntry
        {
            Title = "Platform architecture baseline",
            Summary = "Core blueprint decisions.",
            Content = "This memory captures the architecture for the platform.",
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
        Assert.Empty(pack!.Memories);
        Assert.DoesNotContain(pack.TopMatches, match => match.Kind == ContextRecordKind.Memory);
    }

    [Fact]
    public async Task ContextService_KeepsSemanticBoostForLexicallyGroundedMemory()
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
            Question = "platform architecture",
            PackGoal = ContextPackGoal.Architecture,
            ResultsPerSection = 3
        }, CancellationToken.None);

        var memory = Assert.Single(pack!.Memories);
        Assert.True(memory.SemanticScore > 0m);
        Assert.Contains("architecture", memory.Provenance!.MatchedTokens, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("platform", memory.Provenance.MatchedTokens, StringComparer.OrdinalIgnoreCase);
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
            serviceCollection.AddDbContext<FocusMemoryContext>(options => options.UseSqlite(connection));
            serviceCollection.AddSingleton<IPackIntentModel, TinyLocalPackIntentModel>();
            serviceCollection.AddSingleton<IPackDecisionEngine, PackDecisionEngine>();
            serviceCollection.AddSingleton<IPackCriticEngine, PackCriticEngine>();
            serviceCollection.AddScoped<ContextService>();

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
}
