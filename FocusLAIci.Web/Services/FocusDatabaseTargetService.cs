using System.Text.Json;
using FocusLAIci.Web.Data;
using FocusLAIci.Web.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FocusLAIci.Web.Services;

public sealed class FocusDatabaseTargetService
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly string _overrideFilePath;

    public FocusDatabaseTargetService(IConfiguration configuration, IHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
        _overrideFilePath = Path.Combine(_environment.ContentRootPath, "focus-palace.database-target.json");
    }

    public FocusDatabaseTargetSnapshot GetCurrentTarget()
    {
        var defaultConnectionString = _configuration.GetConnectionString("FocusPalace") ?? "Data Source=focus-palace.db";
        var defaultPath = ResolveAbsoluteDatabasePath(defaultConnectionString);
        var overrideTarget = LoadOverride();

        if (overrideTarget is null)
        {
            return new FocusDatabaseTargetSnapshot
            {
                ConnectionString = defaultConnectionString,
                DatabasePath = defaultPath,
                DefaultDatabasePath = defaultPath,
                UsesDefaultDatabase = true,
                OverrideFilePath = _overrideFilePath
            };
        }

        var effectiveConnectionString = BuildConnectionString(overrideTarget.DatabasePath);
        return new FocusDatabaseTargetSnapshot
        {
            ConnectionString = effectiveConnectionString,
            DatabasePath = ResolveAbsoluteDatabasePath(effectiveConnectionString),
            DefaultDatabasePath = defaultPath,
            UsesDefaultDatabase = false,
            OverrideFilePath = _overrideFilePath
        };
    }

    public async Task<FocusDatabaseTargetSnapshot> UpdateTargetAsync(DatabaseTargetInput input, CancellationToken cancellationToken)
    {
        if (input.UseDefaultDatabase)
        {
            if (File.Exists(_overrideFilePath))
            {
                File.Delete(_overrideFilePath);
            }

            var snapshot = GetCurrentTarget();
            await EnsureDatabaseReadyAsync(snapshot.ConnectionString, cancellationToken);
            return snapshot;
        }

        if (string.IsNullOrWhiteSpace(input.DatabasePath))
        {
            throw new InvalidOperationException("Provide a database file path or choose the default database target.");
        }

        var absolutePath = Path.GetFullPath(input.DatabasePath.Trim());
        var directory = Path.GetDirectoryName(absolutePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Provide a valid database file path.");
        }

        Directory.CreateDirectory(directory);

        var payload = JsonSerializer.Serialize(
            new DatabaseTargetOverride { DatabasePath = absolutePath },
            new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_overrideFilePath, payload, cancellationToken);

        var snapshotAfterSave = GetCurrentTarget();
        await EnsureDatabaseReadyAsync(snapshotAfterSave.ConnectionString, cancellationToken);
        return snapshotAfterSave;
    }

    public Task EnsureCurrentDatabaseReadyAsync(CancellationToken cancellationToken = default)
        => EnsureDatabaseReadyAsync(GetCurrentTarget().ConnectionString, cancellationToken);

    private async Task EnsureDatabaseReadyAsync(string connectionString, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<FocusMemoryContext>()
            .UseSqlite(connectionString)
            .Options;

        await using var dbContext = new FocusMemoryContext(options);
        await MemorySeeder.EnsureDatabaseAsync(dbContext, cancellationToken);
    }

    private DatabaseTargetOverride? LoadOverride()
    {
        if (!File.Exists(_overrideFilePath))
        {
            return null;
        }

        var content = File.ReadAllText(_overrideFilePath);
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        return JsonSerializer.Deserialize<DatabaseTargetOverride>(content);
    }

    private string ResolveAbsoluteDatabasePath(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;
        if (string.Equals(dataSource, ":memory:", StringComparison.Ordinal))
        {
            return ":memory:";
        }

        return Path.GetFullPath(
            Path.IsPathRooted(dataSource)
                ? dataSource
                : Path.Combine(_environment.ContentRootPath, dataSource));
    }

    private static string BuildConnectionString(string databasePath)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath
        };

        return builder.ToString();
    }

    private sealed class DatabaseTargetOverride
    {
        public string DatabasePath { get; set; } = string.Empty;
    }
}
