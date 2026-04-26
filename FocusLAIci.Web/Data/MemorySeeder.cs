using FocusLAIci.Web.Models;
using FocusLAIci.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace FocusLAIci.Web.Data;

public static class MemorySeeder
{
    public static async Task EnsureDatabaseAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FocusMemoryContext>();
        await EnsureDatabaseAsync(dbContext, cancellationToken);
    }

    public static async Task EnsureDatabaseAsync(FocusMemoryContext dbContext, CancellationToken cancellationToken = default)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS SiteSettings (
                Id INTEGER NOT NULL CONSTRAINT PK_SiteSettings PRIMARY KEY,
                DisplayName TEXT NOT NULL,
                HomeHeroCopy TEXT NOT NULL,
                TimeZoneId TEXT NOT NULL,
                ShowUtcTimestamps INTEGER NOT NULL DEFAULT 0,
                DefaultMemoryImportance INTEGER NOT NULL DEFAULT 3
            );
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS Todos (
                Id TEXT NOT NULL CONSTRAINT PK_Todos PRIMARY KEY,
                Title TEXT NOT NULL,
                Details TEXT NOT NULL,
                Status INTEGER NOT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                CompletedUtc TEXT NULL
            );
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS Tickets (
                Id TEXT NOT NULL CONSTRAINT PK_Tickets PRIMARY KEY,
                ParentTicketId TEXT NULL,
                SummaryMemoryId TEXT NULL,
                TicketNumber TEXT NOT NULL,
                Title TEXT NOT NULL,
                Description TEXT NOT NULL,
                Status INTEGER NOT NULL,
                Priority INTEGER NOT NULL,
                Assignee TEXT NOT NULL,
                TagsText TEXT NOT NULL,
                GitBranch TEXT NOT NULL,
                GitCommit TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                CompletedUtc TEXT NULL,
                CONSTRAINT FK_Tickets_Tickets_ParentTicketId FOREIGN KEY (ParentTicketId) REFERENCES Tickets (Id) ON DELETE RESTRICT,
                CONSTRAINT FK_Tickets_Memories_SummaryMemoryId FOREIGN KEY (SummaryMemoryId) REFERENCES Memories (Id) ON DELETE SET NULL
            );
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS TicketNotes (
                Id TEXT NOT NULL CONSTRAINT PK_TicketNotes PRIMARY KEY,
                TicketId TEXT NOT NULL,
                Author TEXT NOT NULL,
                Content TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                CONSTRAINT FK_TicketNotes_Tickets_TicketId FOREIGN KEY (TicketId) REFERENCES Tickets (Id) ON DELETE CASCADE
            );
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS TicketActivities (
                Id TEXT NOT NULL CONSTRAINT PK_TicketActivities PRIMARY KEY,
                TicketId TEXT NOT NULL,
                ActivityType TEXT NOT NULL,
                Message TEXT NOT NULL,
                Metadata TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                CONSTRAINT FK_TicketActivities_Tickets_TicketId FOREIGN KEY (TicketId) REFERENCES Tickets (Id) ON DELETE CASCADE
            );
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS TicketTimeLogs (
                Id TEXT NOT NULL CONSTRAINT PK_TicketTimeLogs PRIMARY KEY,
                TicketId TEXT NOT NULL,
                ModelName TEXT NOT NULL,
                Summary TEXT NOT NULL,
                MinutesSpent INTEGER NOT NULL,
                LoggedUtc TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                CONSTRAINT FK_TicketTimeLogs_Tickets_TicketId FOREIGN KEY (TicketId) REFERENCES Tickets (Id) ON DELETE CASCADE
            );
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS CodeGraphProjects (
                Id TEXT NOT NULL CONSTRAINT PK_CodeGraphProjects PRIMARY KEY,
                Name TEXT NOT NULL,
                RootPath TEXT NOT NULL,
                Description TEXT NOT NULL,
                Summary TEXT NOT NULL,
                FileCount INTEGER NOT NULL DEFAULT 0,
                SymbolCount INTEGER NOT NULL DEFAULT 0,
                RelationshipCount INTEGER NOT NULL DEFAULT 0,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                LastScannedUtc TEXT NULL
            );
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS CodeGraphFiles (
                Id TEXT NOT NULL CONSTRAINT PK_CodeGraphFiles PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                RelativePath TEXT NOT NULL,
                Language TEXT NOT NULL,
                ContentHash TEXT NOT NULL,
                LineCount INTEGER NOT NULL,
                ScannedUtc TEXT NOT NULL,
                CONSTRAINT FK_CodeGraphFiles_CodeGraphProjects_ProjectId FOREIGN KEY (ProjectId) REFERENCES CodeGraphProjects (Id) ON DELETE CASCADE
            );
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS CodeGraphNodes (
                Id TEXT NOT NULL CONSTRAINT PK_CodeGraphNodes PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                FileId TEXT NULL,
                NodeKey TEXT NOT NULL,
                Label TEXT NOT NULL,
                SecondaryLabel TEXT NOT NULL,
                NodeType INTEGER NOT NULL,
                Language TEXT NOT NULL,
                StartLine INTEGER NOT NULL,
                EndLine INTEGER NOT NULL,
                Metadata TEXT NOT NULL,
                CONSTRAINT FK_CodeGraphNodes_CodeGraphProjects_ProjectId FOREIGN KEY (ProjectId) REFERENCES CodeGraphProjects (Id) ON DELETE CASCADE,
                CONSTRAINT FK_CodeGraphNodes_CodeGraphFiles_FileId FOREIGN KEY (FileId) REFERENCES CodeGraphFiles (Id) ON DELETE SET NULL
            );
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS CodeGraphEdges (
                Id TEXT NOT NULL CONSTRAINT PK_CodeGraphEdges PRIMARY KEY,
                ProjectId TEXT NOT NULL,
                FromNodeId TEXT NOT NULL,
                ToNodeId TEXT NOT NULL,
                RelationshipType TEXT NOT NULL,
                Evidence INTEGER NOT NULL,
                ConfidenceScore TEXT NOT NULL,
                Details TEXT NOT NULL,
                CONSTRAINT FK_CodeGraphEdges_CodeGraphProjects_ProjectId FOREIGN KEY (ProjectId) REFERENCES CodeGraphProjects (Id) ON DELETE CASCADE,
                CONSTRAINT FK_CodeGraphEdges_CodeGraphNodes_FromNodeId FOREIGN KEY (FromNodeId) REFERENCES CodeGraphNodes (Id) ON DELETE CASCADE,
                CONSTRAINT FK_CodeGraphEdges_CodeGraphNodes_ToNodeId FOREIGN KEY (ToNodeId) REFERENCES CodeGraphNodes (Id) ON DELETE RESTRICT
            );
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS ContextLinks (
                Id TEXT NOT NULL CONSTRAINT PK_ContextLinks PRIMARY KEY,
                SourceKind INTEGER NOT NULL,
                SourceId TEXT NOT NULL,
                TargetKind INTEGER NOT NULL,
                TargetId TEXT NOT NULL,
                Label TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL
            );
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            INSERT OR IGNORE INTO SiteSettings (Id, DisplayName, HomeHeroCopy, TimeZoneId, ShowUtcTimestamps, DefaultMemoryImportance)
            VALUES (1, 'Focus L-AIci', 'A local-first C# memory system for app development: wings, rooms, verbatim notes, searchable context, and an explorer UI for finding past reasoning fast.', 'UTC', 0, 3);
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM Wings
            WHERE Name = 'Concurrent Wing'
              AND Description = 'race'
              AND NOT EXISTS (SELECT 1 FROM Rooms WHERE Rooms.WingId = Wings.Id)
              AND NOT EXISTS (SELECT 1 FROM Memories WHERE Memories.WingId = Wings.Id);
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Wings_Name_NoCase
            ON Wings(Name COLLATE NOCASE);
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS IX_Todos_Status_UpdatedUtc
            ON Todos(Status, UpdatedUtc DESC);
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Tickets_TicketNumber
            ON Tickets(TicketNumber);
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS IX_Tickets_Status_UpdatedUtc
            ON Tickets(Status, UpdatedUtc DESC);
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS IX_Tickets_ParentTicketId
            ON Tickets(ParentTicketId);
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS IX_TicketNotes_TicketId_CreatedUtc
            ON TicketNotes(TicketId, CreatedUtc DESC);
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS IX_TicketActivities_TicketId_CreatedUtc
            ON TicketActivities(TicketId, CreatedUtc DESC);
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS IX_TicketTimeLogs_TicketId_LoggedUtc
            ON TicketTimeLogs(TicketId, LoggedUtc DESC);
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS IX_CodeGraphFiles_ProjectId_RelativePath
            ON CodeGraphFiles(ProjectId, RelativePath);
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS IX_CodeGraphNodes_ProjectId_NodeKey
            ON CodeGraphNodes(ProjectId, NodeKey);
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS IX_CodeGraphNodes_ProjectId_NodeType_Label
            ON CodeGraphNodes(ProjectId, NodeType, Label);
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS IX_CodeGraphEdges_ProjectId_FromNodeId_ToNodeId_RelationshipType
            ON CodeGraphEdges(ProjectId, FromNodeId, ToNodeId, RelationshipType);
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS IX_ContextLinks_SourceKind_SourceId
            ON ContextLinks(SourceKind, SourceId);
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS IX_ContextLinks_TargetKind_TargetId
            ON ContextLinks(TargetKind, TargetId);
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS IX_ContextLinks_SourceKind_SourceId_TargetKind_TargetId
            ON ContextLinks(SourceKind, SourceId, TargetKind, TargetId);
            """,
            cancellationToken);
        await EnsureColumnExistsAsync(dbContext, "Memories", "VerificationStatus", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnExistsAsync(dbContext, "Memories", "LastVerifiedUtc", "TEXT NULL", cancellationToken);
        await EnsureColumnExistsAsync(dbContext, "Memories", "ReviewAfterUtc", "TEXT NULL", cancellationToken);
        await EnsureColumnExistsAsync(dbContext, "Memories", "LifecycleState", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
        await EnsureColumnExistsAsync(dbContext, "Memories", "SupersededByMemoryId", "TEXT NULL", cancellationToken);
        await EnsureColumnExistsAsync(dbContext, "Memories", "LifecycleReason", "TEXT NOT NULL DEFAULT ''", cancellationToken);
        await EnsureColumnExistsAsync(dbContext, "Memories", "LifecycleChangedUtc", "TEXT NULL", cancellationToken);
        await EnsureColumnExistsAsync(dbContext, "Memories", "ArchivedUtc", "TEXT NULL", cancellationToken);
        await EnsureColumnExistsAsync(dbContext, "Memories", "LastReferencedUtc", "TEXT NULL", cancellationToken);
        await EnsureColumnExistsAsync(dbContext, "Memories", "ReferenceCount", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS IX_Memories_VerificationStatus
            ON Memories(VerificationStatus);
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS IX_Memories_ReviewAfterUtc
            ON Memories(ReviewAfterUtc);
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS IX_Memories_LifecycleState
            ON Memories(LifecycleState);
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS IX_Memories_SupersededByMemoryId
            ON Memories(SupersededByMemoryId);
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS IX_Memories_LastReferencedUtc
            ON Memories(LastReferencedUtc);
            """,
            cancellationToken);
    }

    public static async Task SeedSampleDataAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FocusMemoryContext>();
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);

        if (await dbContext.Wings.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = DateTime.UtcNow;
        var productWing = new Wing
        {
            Name = "Product Strategy",
            Slug = "product-strategy",
            Description = "North-star goals, roadmap decisions, and user-facing product knowledge."
        };
        var engineeringWing = new Wing
        {
            Name = "Engineering Operations",
            Slug = "engineering-operations",
            Description = "Deployment practices, incident response notes, and debugging history."
        };
        var knowledgeWing = new Wing
        {
            Name = "Reusable Patterns",
            Slug = "reusable-patterns",
            Description = "Architecture templates, naming conventions, and implementation playbooks."
        };

        var roadmapRoom = new Room
        {
            Wing = productWing,
            Name = "Roadmap",
            Slug = "roadmap",
            Description = "Current themes and priorities."
        };
        var installerRoom = new Room
        {
            Wing = engineeringWing,
            Name = "Installer Reliability",
            Slug = "installer-reliability",
            Description = "Lessons and fixes around endpoint installs."
        };
        var uiRoom = new Room
        {
            Wing = knowledgeWing,
            Name = "Admin UX",
            Slug = "admin-ux",
            Description = "Patterns for practical, high-signal admin interfaces."
        };

        var tags = new Dictionary<string, Tag>(StringComparer.OrdinalIgnoreCase)
        {
            ["memory"] = new() { Name = "memory", Slug = "memory" },
            ["architecture"] = new() { Name = "architecture", Slug = "architecture" },
            ["installer"] = new() { Name = "installer", Slug = "installer" },
            ["frontend"] = new() { Name = "frontend", Slug = "frontend" },
            ["search"] = new() { Name = "search", Slug = "search" }
        };

        var vision = CreateMemory(
            productWing,
            roadmapRoom,
            "Keep all important reasoning, not just summaries",
            "This palace stores verbatim notes plus strong metadata so future sessions can recover decisions quickly.",
            """
            The core design principle for Focus L-AIci is local-first, structured memory.
            Instead of trying to decide what an AI should forget, preserve the original note
            and organize it into wings and rooms so retrieval stays understandable to a human.
            """,
            MemoryKind.Decision,
            SourceKind.Architecture,
            5,
            true,
            now.AddDays(-5),
            tags["memory"],
            tags["architecture"]);

        var installerNote = CreateMemory(
            engineeringWing,
            installerRoom,
            "Always surface the real installer failure",
            "Bootstrap wrappers should never replace the true underlying process error with a generic blank exit-code message.",
            """
            Capture stdout and stderr from the native installer process, but use a reliable exit-code
            mechanism. Avoid false failures from wrapper-specific process handling. Best-effort host
            preparation steps must not block registration if the actual product install can proceed.
            """,
            MemoryKind.Incident,
            SourceKind.DebugSession,
            5,
            true,
            now.AddDays(-2),
            tags["installer"]);

        var explorerNote = CreateMemory(
            knowledgeWing,
            uiRoom,
            "Admin tools should prioritize browseability",
            "A good memory frontend needs stats, filters, recent items, and obvious drill-down paths.",
            """
            When building operational knowledge tools, the interface should let operators answer
            three questions quickly: what exists, what changed recently, and where do I drill deeper.
            Dashboard cards, faceted search, room/wing browse pages, and linked memories give that shape.
            """,
            MemoryKind.Insight,
            SourceKind.Research,
            4,
            false,
            now.AddDays(-1),
            tags["frontend"],
            tags["search"]);

        dbContext.AddRange(productWing, engineeringWing, knowledgeWing, roadmapRoom, installerRoom, uiRoom);
        dbContext.Tags.AddRange(tags.Values);
        dbContext.Memories.AddRange(vision, installerNote, explorerNote);
        dbContext.MemoryLinks.AddRange(
            new MemoryLink
            {
                FromMemoryEntry = vision,
                ToMemoryEntry = explorerNote,
                Label = "influences"
            },
            new MemoryLink
            {
                FromMemoryEntry = installerNote,
                ToMemoryEntry = vision,
                Label = "validates"
            });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static MemoryEntry CreateMemory(
        Wing wing,
        Room room,
        string title,
        string summary,
        string content,
        MemoryKind kind,
        SourceKind sourceKind,
        int importance,
        bool pinned,
        DateTime occurredUtc,
        params Tag[] tags)
    {
        var memory = new MemoryEntry
        {
            Wing = wing,
            Room = room,
            Title = title,
            Summary = summary,
            Content = content.Trim(),
            Kind = kind,
            SourceKind = sourceKind,
            Importance = importance,
            IsPinned = pinned,
            VerificationStatus = MemoryVerificationStatus.Verified,
            LastVerifiedUtc = occurredUtc,
            ReviewAfterUtc = occurredUtc.AddDays(MemoryTrustHelper.DefaultReviewWindowDays),
            OccurredUtc = occurredUtc,
            CreatedUtc = occurredUtc,
            UpdatedUtc = occurredUtc
        };

        foreach (var tag in tags)
        {
            memory.MemoryTags.Add(new MemoryEntryTag
            {
                MemoryEntry = memory,
                Tag = tag
            });
        }

        return memory;
    }

    private static async Task EnsureColumnExistsAsync(FocusMemoryContext dbContext, string tableName, string columnName, string columnDefinition, CancellationToken cancellationToken)
    {
        var safeTableName = ValidateSqlIdentifier(tableName);
        var safeColumnName = ValidateSqlIdentifier(columnName);
        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info({tableName});";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }

        var sql = "ALTER TABLE " + safeTableName + " ADD COLUMN " + safeColumnName + " " + columnDefinition + ";";
        await dbContext.Database.ExecuteSqlRawAsync(
            sql,
            cancellationToken);
    }

    private static string ValidateSqlIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '_')))
        {
            throw new InvalidOperationException("Unsafe SQL identifier.");
        }

        return value;
    }
}
