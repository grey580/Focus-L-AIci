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
            CREATE TABLE IF NOT EXISTS Skills (
                Id TEXT NOT NULL CONSTRAINT PK_Skills PRIMARY KEY,
                WingId TEXT NULL,
                Name TEXT NOT NULL,
                Slug TEXT NOT NULL,
                Summary TEXT NOT NULL,
                Category INTEGER NOT NULL,
                WhenToUse TEXT NOT NULL,
                Flow TEXT NOT NULL,
                ExamplesText TEXT NOT NULL,
                TriggerHintsText TEXT NOT NULL,
                IsPinned INTEGER NOT NULL DEFAULT 1,
                UseCount INTEGER NOT NULL DEFAULT 0,
                LastUsedUtc TEXT NULL,
                LastReviewedUtc TEXT NULL,
                ReviewAfterUtc TEXT NULL,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                CONSTRAINT FK_Skills_Wings_WingId FOREIGN KEY (WingId) REFERENCES Wings (Id) ON DELETE SET NULL
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
            CREATE TABLE IF NOT EXISTS PackBuildRecords (
                Id TEXT NOT NULL CONSTRAINT PK_PackBuildRecords PRIMARY KEY,
                Question TEXT NOT NULL,
                GoalLabel TEXT NOT NULL,
                Summary TEXT NOT NULL,
                ExportText TEXT NOT NULL,
                SearchTokensJson TEXT NOT NULL,
                SuggestedSkillNamesJson TEXT NOT NULL,
                ResultsPerSection INTEGER NOT NULL DEFAULT 6,
                TopMatchCount INTEGER NOT NULL DEFAULT 0,
                MemoryCount INTEGER NOT NULL DEFAULT 0,
                TodoCount INTEGER NOT NULL DEFAULT 0,
                TicketCount INTEGER NOT NULL DEFAULT 0,
                CodeGraphProjectCount INTEGER NOT NULL DEFAULT 0,
                CodeGraphFileCount INTEGER NOT NULL DEFAULT 0,
                CodeGraphNodeCount INTEGER NOT NULL DEFAULT 0,
                RecommendedSkillCount INTEGER NOT NULL DEFAULT 0,
                SuggestedExternalSkillCount INTEGER NOT NULL DEFAULT 0,
                ReviewScore INTEGER NULL,
                ReviewNotes TEXT NOT NULL DEFAULT '',
                CreatedUtc TEXT NOT NULL
            );
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS ExternalSkillSources (
                Id TEXT NOT NULL CONSTRAINT PK_ExternalSkillSources PRIMARY KEY,
                Name TEXT NOT NULL,
                CatalogUrl TEXT NOT NULL,
                Description TEXT NOT NULL,
                IsEnabled INTEGER NOT NULL DEFAULT 1,
                CreatedUtc TEXT NOT NULL,
                UpdatedUtc TEXT NOT NULL,
                LastCheckedUtc TEXT NULL,
                LastCheckStatus TEXT NOT NULL DEFAULT ''
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
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Skills_Slug
            ON Skills(Slug);
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS IX_Skills_Category
            ON Skills(Category);
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
            CREATE INDEX IF NOT EXISTS IX_PackBuildRecords_CreatedUtc
            ON PackBuildRecords(CreatedUtc DESC);
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS IX_PackBuildRecords_ReviewScore
            ON PackBuildRecords(ReviewScore);
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS IX_ExternalSkillSources_Name
            ON ExternalSkillSources(Name);
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE UNIQUE INDEX IF NOT EXISTS IX_ExternalSkillSources_CatalogUrl
            ON ExternalSkillSources(CatalogUrl);
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
        await EnsureColumnExistsAsync(dbContext, "Skills", "UseCount", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnExistsAsync(dbContext, "Skills", "LastUsedUtc", "TEXT NULL", cancellationToken);
        await EnsureColumnExistsAsync(dbContext, "Skills", "LastReviewedUtc", "TEXT NULL", cancellationToken);
        await EnsureColumnExistsAsync(dbContext, "Skills", "ReviewAfterUtc", "TEXT NULL", cancellationToken);
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
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS IX_Skills_LastUsedUtc
            ON Skills(LastUsedUtc);
            """,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS IX_Skills_ReviewAfterUtc
            ON Skills(ReviewAfterUtc);
            """,
            cancellationToken);
        await EnsureStarterSkillsAsync(dbContext, cancellationToken);
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

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task EnsureStarterSkillsAsync(FocusMemoryContext dbContext, CancellationToken cancellationToken)
    {
        var existingSlugs = await dbContext.Skills
            .AsNoTracking()
            .Select(x => x.Slug)
            .ToListAsync(cancellationToken);
        var existingSlugSet = existingSlugs.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var repoSkillSlugSet = GetRepoSkillSlugs();

        var wingLookup = await dbContext.Wings
            .AsNoTracking()
            .Select(x => new { x.Id, x.Slug })
            .ToListAsync(cancellationToken);

        foreach (var definition in CreateStarterSkills().Where(x => !existingSlugSet.Contains(x.Slug) && !repoSkillSlugSet.Contains(x.Slug)))
        {
            var now = DateTime.UtcNow;
            dbContext.Skills.Add(new SkillEntry
            {
                Name = definition.Name,
                Slug = definition.Slug,
                Summary = definition.Summary,
                Category = definition.Category,
                WhenToUse = definition.WhenToUse,
                Flow = definition.Flow,
                ExamplesText = definition.ExamplesText,
                TriggerHintsText = definition.TriggerHintsText,
                IsPinned = definition.IsPinned,
                WingId = wingLookup.FirstOrDefault(x => string.Equals(x.Slug, definition.WingSlug, StringComparison.OrdinalIgnoreCase))?.Id,
                LastReviewedUtc = now,
                ReviewAfterUtc = now.AddDays(SkillRecommendationEngine.DefaultReviewWindowDays)
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static HashSet<string> GetRepoSkillSlugs()
    {
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in ResolveRepoSkillRoots())
        {
            foreach (var skillFile in Directory.EnumerateFiles(root, "SKILL.md", SearchOption.AllDirectories))
            {
                var skillDirectory = Path.GetDirectoryName(skillFile);
                var slug = Path.GetFileName(skillDirectory);
                if (!string.IsNullOrWhiteSpace(slug))
                {
                    slugs.Add(slug);
                }
            }
        }

        return slugs;
    }

    private static IEnumerable<string> ResolveRepoSkillRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in new[]
                 {
                     Directory.GetCurrentDirectory(),
                     AppContext.BaseDirectory
                 })
        {
            var current = new DirectoryInfo(candidate);
            while (current is not null)
            {
                var skillRoot = Path.Combine(current.FullName, ".agents", "skills");
                if (Directory.Exists(skillRoot) && seen.Add(skillRoot))
                {
                    yield return skillRoot;
                }

                current = current.Parent;
            }
        }
    }

    private static IReadOnlyCollection<StarterSkillDefinition> CreateStarterSkills() =>
    [
        new(
            "Investigate static asset failures",
            "investigate-static-asset-failures",
            "Trace missing CSS and JS quickly by verifying launch root, asset paths, MIME types, and browser-visible GET responses.",
            SkillCategory.Task,
            "Use this when the site renders unstyled, scripts stop loading, or the browser reports MIME errors for static assets.",
            """
            Confirm the app is running from the intended content root or publish root.
            Check the rendered page for the exact asset URLs being referenced.
            Fetch the asset URLs with browser-like GET requests and inspect status, content length, and content type.
            If assets fail, trace static-file middleware, content root resolution, and any stale or dead asset references in layouts.
            Re-run the app the same way users launch it before concluding the fix works.
            """,
            """
            Investigate why site.css is not loading.
            Check whether the running Focus instance is serving bootstrap and site.js correctly.
            Trace a MIME type error on a static asset URL.
            """,
            "css, js, static files, mime, bootstrap, site.css, site.js",
            true,
            "reusable-patterns"),
        new(
            "Run an MCP usability pass",
            "run-mcp-usability-pass",
            "Exercise Focus the way an agent uses it: bootstrap, discovery, retrieval, governance, and duplicate-safe save flows.",
            SkillCategory.Task,
            "Use this after MCP changes, retrieval tuning, or when Focus feels awkward and needs a practical end-to-end usability check.",
            """
            Initialize an MCP session and verify the manifest loads.
            Start with workspace bootstrap and wing or room discovery for the area you care about.
            Run a realistic context question and inspect whether durable memories lead the results.
            Test governance and duplicate-safe save flows with dry-run mode.
            Record any friction as a concrete follow-up item instead of a vague usability complaint.
            """,
            """
            Do a live MCP usability pass for Microsoft-related memories.
            Check whether duplicate-safe save catches an Entra mailbox draft.
            Verify context.inspect leads with durable memory instead of code graph noise.
            """,
            "mcp, usability, context.inspect, bootstrap, governance, duplicates",
            true,
            null),
        new(
            "Understand Focus local runtime quirks",
            "understand-focus-local-runtime-quirks",
            "Remember the local rules that usually matter first: content root, database target override, locked DLLs, and restart/test sequencing.",
            SkillCategory.System,
            "Use this before debugging confusing local behavior where Focus seems to use the wrong data, wrong assets, or refuses to rebuild cleanly.",
            """
            Check which content root the app is using and whether wwwroot resolves from that location.
            Verify the effective database target and whether focus-palace.database-target.json is overriding it.
            Stop the exact running dotnet host before rebuilding if DLLs are locked.
            Retest using the same launch path and command that reproduces the issue.
            """,
            """
            Why is Focus using the wrong database?
            Why do tests fail because FocusLAIci.Web.dll is locked?
            Why does the page HTML load but static files 404?
            """,
            "content root, database target, locked dll, launch path, static files",
            true,
            null),
        new(
            "Review Microsoft integration work",
            "review-microsoft-integration-work",
            "Use Microsoft wing knowledge, Entra memories, and governance signals to review identity and mailbox integration work consistently.",
            SkillCategory.Product,
            "Use this when working on Microsoft Entra, Graph, Office 365, mailbox handling, or broader Microsoft-specific product behavior.",
            """
            Start in the Microsoft wing and inspect the relevant rooms before broad search.
            Search for Entra, Graph, OAuth, mailbox, and tenant-specific terms with includeRetired enabled when history matters.
            Resolve canonical memories if duplicate-looking search results appear.
            Build a context pack for the exact architecture or troubleshooting question.
            Save any new Microsoft findings only after checking duplicate suggestions and canonical history.
            """,
            """
            Review how Grey Canary uses Microsoft Entra for SOC mailbox handling.
            Find the canonical Microsoft Graph delegated-scope memory.
            Check whether a new mailbox note should merge into an existing Entra memory.
            """,
            "microsoft, entra, graph, office365, oauth, mailbox",
            true,
            "microsoft"),
        new(
            "Review endpoint uninstall and recovery flow",
            "review-endpoint-uninstall-and-recovery-flow",
            "Follow the Grey Canary endpoint removal workflow carefully so uninstall, recovery, and final cleanup stay aligned with prior decisions.",
            SkillCategory.Product,
            "Use this when touching endpoint removal, uninstall jobs, hidden endpoints, recovery UX, or final-removal behavior.",
            """
            Search Grey Canary memories for uninstall, endpoint removal, recovery, and manual uninstall behavior.
            Inspect active tickets or recent changes before editing endpoint state transitions.
            Check whether the current flow expects a queued uninstall job, a warning modal, or a recovery path instead of direct deletion.
            Capture any changed operator guidance back into Focus when the workflow shifts.
            """,
            """
            Review the current final endpoint removal workflow.
            Check how manual uninstall differs from remote uninstall job handling.
            Verify whether endpoint recovery guidance is still current.
            """,
            "grey-canary, endpoints, uninstall, recovery, removal",
            true,
            "grey-canary"),
        new(
            "Work Focus-first",
            "work-focus-first",
            "Use Focus as the first step for coding, debugging, and operations instead of starting from guesses or a cold code search.",
            SkillCategory.Tooling,
            "Use this as the default workflow when beginning new work in a repo or resuming after interruption.",
            """
            Search Focus for the feature, subsystem, or problem statement first.
            Build a context pack for the exact task once you know the likely area.
            Review recent changes, active tickets, and governance if the work touches durable behavior.
            Use the retrieved context to drive implementation and save back important findings when done.
            """,
            """
            Start with Focus before touching auth refresh logic.
            Build a context pack before changing installer behavior.
            Use Focus to recover state after a long interruption.
            """,
            "workflow, start here, search first, context pack, retrieve before coding",
            true,
            null),
        new(
            "Govern memory safely",
            "govern-memory-safely",
            "Use duplicate detection, canonical resolution, merge, and governance queues so durable knowledge stays clean instead of drifting.",
            SkillCategory.Tooling,
            "Use this when adding or cleaning up durable memories, especially after repeated investigations in the same subsystem.",
            """
            Search first to see whether the knowledge already exists.
            If a candidate exists, inspect duplicate suggestions or resolve the canonical memory before writing.
            Use dry-run save for ambiguous updates and only persist when you know whether this should be new, merged, or superseding history.
            Review governance queues regularly so archived, superseded, and unverified memories do not pile up unnoticed.
            """,
            """
            Check whether a new Entra mailbox note should merge into an existing memory.
            Use the governance queue to find unverified or aging records.
            Resolve canonical history before citing a memory as current architecture.
            """,
            "governance, duplicates, merge, canonical, memory hygiene",
            true,
            null),
        new(
            "Acquire codebase knowledge",
            "acquire-codebase-knowledge",
            "Map an unfamiliar repository into a usable working model by documenting stack, structure, architecture, integrations, testing, and known concerns.",
            SkillCategory.Task,
            "Use this when onboarding into an existing codebase, recovering context after a long gap, or building a durable architecture map before major changes.",
            """
            Scan the repo and read the intent documents before making architecture claims.
            Capture the real stack, directory layout, entry points, integrations, and testing setup from source evidence.
            Separate verified facts from unknowns instead of guessing through ambiguous areas.
            Produce a concise codebase map that future work can reuse for implementation, review, and onboarding.
            """,
            """
            Map this repo before we start a risky refactor.
            Create a usable architecture and testing overview for a new engineer.
            Recover the real structure of a repo whose README has drifted.
            """,
            "onboarding, codebase map, architecture, structure, testing, discovery",
            true,
            null),
        new(
            "Generate architecture blueprint",
            "generate-architecture-blueprint",
            "Create a durable architecture blueprint that explains system boundaries, component responsibilities, data flow, and cross-cutting patterns from the actual code.",
            SkillCategory.Task,
            "Use this when the team needs a formal architecture reference, a system handoff, or a grounded view of how the implementation is really put together.",
            """
            Detect the real architecture from the codebase instead of restating intended patterns from old docs.
            Describe major subsystems, boundaries, dependencies, and data flow in implementation terms.
            Call out cross-cutting patterns such as auth, validation, logging, resilience, and configuration.
            Save the blueprint in a form that supports planning, review, and future extension work.
            """,
            """
            Generate a blueprint for the Focus web platform before a redesign.
            Document the actual architecture of a mixed web, service, and data system.
            Build an implementation-ready system reference from code.
            """,
            "architecture, blueprint, system design, dependencies, data flow, handoff",
            true,
            null),
        new(
            "Design agent governance",
            "design-agent-governance",
            "Apply policy controls, trust boundaries, threat detection, and audit logging so agent workflows stay safe and reviewable in production.",
            SkillCategory.System,
            "Use this when an agent can call tools, touch durable data, trigger side effects, or coordinate with other agents or external systems.",
            """
            Define the allowed tools, blocked actions, and approval boundaries for the workflow.
            Add pre-execution intent or threat checks before sensitive tool calls run.
            Make policy decisions deterministic and auditable instead of relying on the model alone.
            Record enough telemetry to explain what the agent did, why it was allowed, and how to review it later.
            """,
            """
            Add tool governance to a Focus MCP workflow.
            Design safety controls for an agent that can modify durable memory.
            Add audit trails and trust boundaries to a multi-agent automation flow.
            """,
            "governance, policy, audit, trust, tool safety, agent controls",
            true,
            null),
        new(
            "Check agent OWASP compliance",
            "check-agent-owasp-compliance",
            "Review an agent system against the OWASP Agentic Security Initiative Top 10 so prompt injection, tool abuse, escalation, and audit gaps are found early.",
            SkillCategory.System,
            "Use this before production deployment, during a security review, or after major changes to an agent workflow with tools and durable side effects.",
            """
            Evaluate the workflow against the OWASP agentic risk areas, not just general app security.
            Check prompt-injection handling, tool restrictions, agency limits, trust boundaries, and logging coverage.
            Note which controls are present, which are missing, and what evidence supports each conclusion.
            Turn the findings into a concrete hardening backlog instead of a vague compliance label.
            """,
            """
            Check whether our agent platform covers the OWASP ASI top risks.
            Audit a tool-calling workflow before rollout.
            Review an MCP automation path for escalation and logging gaps.
            """,
            "owasp, asi, compliance, agent security, prompt injection, tool abuse",
            true,
            null),
        new(
            "Orchestrate AI delivery team",
            "orchestrate-ai-delivery-team",
            "Structure planning, implementation, QA, and DevOps handoffs so larger AI-assisted projects keep context, accountability, and clear execution lanes.",
            SkillCategory.Tooling,
            "Use this when work is large enough to benefit from explicit planning, parallel streams, or repeatable handoff patterns across development and validation.",
            """
            Establish a shared project brief or operating context before splitting work.
            Separate planning, implementation, QA, and operational responsibilities so each stream has a clear purpose.
            Track sprint or milestone state in durable artifacts that survive context loss.
            Use explicit handoffs and bug tracking instead of relying on chat history alone.
            """,
            """
            Set up a repeatable AI workflow for planning, coding, QA, and release.
            Recover a project after context overflow without losing execution state.
            Organize a larger feature into planning, build, and validation lanes.
            """,
            "orchestration, handoff, sprint, qa, workflow, multi-agent, planning",
            true,
            null),
        new(
            "Instrument App Insights telemetry",
            "instrument-app-insights-telemetry",
            "Add Azure Application Insights observability so the web platform emits useful health, error, and usage telemetry with a clear deployment path.",
            SkillCategory.System,
            "Use this when the web app needs stronger production telemetry, release visibility, or incident triage support in Azure-hosted environments.",
            """
            Confirm the hosting model, runtime, and deployment path before choosing instrumentation.
            Prefer the least disruptive Azure instrumentation path that fits the hosting model.
            Add application telemetry in code or infrastructure with clear configuration boundaries.
            Verify the resulting telemetry covers health, failures, and the key operator journeys that matter during support incidents.
            """,
            """
            Add App Insights telemetry to the Focus web app.
            Plan Azure observability for a production ASP.NET Core deployment.
            Improve triage data for web incidents and failed user flows.
            """,
            "app insights, telemetry, azure, observability, monitoring, incident triage",
            true,
            "microsoft"),
        new(
            "Apply .NET best practices",
            "apply-dotnet-best-practices",
            "Review .NET and C# changes for maintainability, correctness, dependency injection hygiene, async behavior, logging, configuration, and testability.",
            SkillCategory.System,
            "Use this when changing C# or ASP.NET code and you want a focused pass on idiomatic .NET structure before a bug, warning, or style drift turns into a larger issue.",
            """
            Check whether the change follows the repo's existing C# and ASP.NET conventions before applying generic advice.
            Review dependency injection, exception handling, logging, configuration binding, and async usage for consistency and safety.
            Look for over-abstraction, leaky visibility, weak naming, or behavior-changing cleanup disguised as style work.
            Tighten the implementation only where it improves clarity, reliability, or maintainability without widening the scope.
            """,
            """
            Review this ASP.NET Core change for .NET best-practice drift.
            Check whether the new service registration and logging pattern fit the rest of the solution.
            Give a focused .NET quality pass before we merge.
            """,
            "dotnet, csharp, best practices, dependency injection, logging, configuration",
            true,
            null),
        new(
            "Review .NET design patterns",
            "review-dotnet-design-patterns",
            "Inspect a C# area for design-pattern fit, separation of concerns, testability, and over-engineering without blindly adding abstractions.",
            SkillCategory.System,
            "Use this when a subsystem feels tangled, over-layered, or inconsistent and you need a design review grounded in how the code actually works.",
            """
            Map the concrete responsibilities and dependency flow before recommending patterns.
            Check for unnecessary wrappers, misplaced abstractions, or missing seams around external dependencies.
            Review how the code handles commands, factories, repositories, providers, and other recurring patterns only where they fit the current design.
            Prefer specific recommendations that reduce complexity or improve testability over pattern-by-pattern scoring.
            """,
            """
            Review this C# feature area for pattern misuse or needless abstraction.
            Check whether the service and repository split is helping or hurting maintainability.
            Evaluate a .NET subsystem for design-pattern drift before refactoring.
            """,
            "dotnet, design patterns, architecture review, abstractions, solid, maintainability",
            true,
            null),
        new(
            "Plan .NET upgrade",
            "plan-dotnet-upgrade",
            "Plan a .NET framework or package upgrade in a staged way so target frameworks, package drift, build changes, and validation order stay manageable.",
            SkillCategory.Task,
            "Use this when a solution needs a .NET upgrade, dependency modernization, or project sequencing review before editing frameworks and packages.",
            """
            Inventory the projects, target frameworks, SDK constraints, and package drift first.
            Order the work from least-coupled libraries toward app hosts, tests, and pipelines.
            Identify likely breaking changes, legacy package risks, and configuration updates before making edits.
            Turn the upgrade into explicit checkpoints covering restore, build, tests, runtime validation, and rollback points.
            """,
            """
            Plan the upgrade path for this solution to a newer .NET release.
            Check package compatibility before changing TargetFramework values.
            Create a safe sequencing plan for a multi-project .NET upgrade.
            """,
            "dotnet upgrade, target framework, packages, migration, build pipeline, compatibility",
            true,
            null),
        new(
            "Review C# async workflows",
            "review-csharp-async-workflows",
            "Check async C# code for deadlock risks, blocking calls, weak cancellation flow, unnecessary allocations, and exception-handling mistakes.",
            SkillCategory.System,
            "Use this when code adds async behavior, background work, I/O, or concurrency and you want a targeted pass on correctness and runtime behavior.",
            """
            Look for blocking calls, fire-and-forget work, and Task usage that breaks async end-to-end behavior.
            Check naming, return types, cancellation propagation, and exception handling for consistency with the surrounding code.
            Prefer simple async fixes that preserve behavior before reaching for advanced task patterns.
            Call out hot-path allocation or parallelization opportunities only when they are likely to matter in practice.
            """,
            """
            Review this C# async path for blocking or deadlock risks.
            Check whether cancellation tokens are flowing through the new service calls.
            Evaluate a background workflow for Task misuse and exception handling gaps.
            """,
            "csharp, async, await, cancellation, tasks, deadlock, concurrency",
            true,
            null),
        new(
            "Review EF Core data access",
            "review-ef-core-data-access",
            "Review Entity Framework Core usage for query shape, tracking behavior, relationship mapping, migration safety, and common performance traps.",
            SkillCategory.System,
            "Use this when changing EF Core entities, queries, or migrations and you want to avoid N+1 behavior, weak modeling, or fragile persistence changes.",
            """
            Check whether the DbContext, entity configuration, and navigation model match the actual usage pattern.
            Review queries for tracking mode, projection, pagination, Include usage, and N+1 risk.
            Inspect SaveChanges boundaries, concurrency assumptions, and transaction handling where writes span multiple operations.
            Treat migrations as deployable artifacts that need clear intent, safe naming, and runtime awareness.
            """,
            """
            Review this EF Core query for tracking and N+1 problems.
            Check whether the new migration is shaped safely for deployment.
            Evaluate an entity change for model configuration and relationship drift.
            """,
            "ef core, dbcontext, migrations, tracking, includes, queries, entity framework",
            true,
            null),
        new(
            "Work as web coder",
            "work-as-web-coder",
            "Approach web changes with explicit attention to standards, accessibility, HTTP behavior, security, performance, and browser-visible outcomes.",
            SkillCategory.Task,
            "Use this when working on HTML, CSS, JavaScript, web APIs, static assets, rendering behavior, or browser-visible problems across the web stack.",
            """
            Start from the user-visible web behavior before assuming the problem is only in markup or only on the server.
            Check semantics, accessibility, network behavior, asset delivery, and browser constraints together when a change crosses layers.
            Prefer standards-compliant fixes that improve clarity, resilience, and cross-browser behavior instead of brittle one-off patches.
            Translate vague web terminology into concrete implementation concerns before editing code.
            """,
            """
            Triage this web issue from HTTP response to browser rendering.
            Implement a standards-friendly fix for a CSS, JS, or accessibility regression.
            Treat this feature as a full web-stack change, not just a markup tweak.
            """,
            "web, html, css, javascript, http, browser, accessibility, performance",
            true,
            null),
        new(
            "Review web design quality",
            "review-web-design-quality",
            "Inspect a live page for layout, spacing, responsive, and accessibility issues, then trace the fixes back to the real source files.",
            SkillCategory.Task,
            "Use this when a page looks broken, inconsistent, or awkward and you need a practical visual review instead of guessing from code alone.",
            """
            Review the page visually at meaningful desktop and mobile widths before editing.
            Prioritize layout breakage, overlap, clipping, focus visibility, contrast, and inconsistent spacing ahead of cosmetic nitpicks.
            Identify the real component or stylesheet source for each issue instead of patching symptoms from the wrong file.
            Recheck the affected flow after changes so fixes do not create regressions at another viewport.
            """,
            """
            Review this page for layout and responsive issues.
            Check the UI for visual consistency and accessibility gaps before more feature work.
            Trace the source of a broken layout and fix it at the right component or stylesheet.
            """,
            "web design, ui review, responsive, layout, accessibility, css, screenshots",
            true,
            null),
        new(
            "Test web application flows",
            "test-web-application-flows",
            "Exercise a running web app like a user would so navigation, forms, console errors, screenshots, and regressions are checked in one pass.",
            SkillCategory.Task,
            "Use this after web changes, before shipping a UI fix, or whenever a user flow needs evidence instead of a code-only confidence check.",
            """
            Confirm the target app is running and accessible before testing interactions.
            Walk the real user flow with browser automation or browser-like requests, not just unit assumptions.
            Capture screenshots, console output, and broken steps when a flow fails so the fix has usable evidence.
            Re-run the changed flow after fixes and note any remaining friction clearly.
            """,
            """
            Test the login or settings flow end to end after a UI change.
            Reproduce a reported browser issue and capture the evidence.
            Verify a web feature with real interactions before closing the work.
            """,
            "webapp testing, playwright, ui flow, screenshots, console logs, regression",
            true,
            null),
        new(
            "Review SQL code safety",
            "review-sql-code-safety",
            "Review SQL for injection risk, weak permissions, brittle schema decisions, unreadable query shape, and maintainability problems before performance tuning.",
            SkillCategory.System,
            "Use this when changing SQL queries, procedures, schema scripts, or database access patterns and you want a focused safety and quality pass.",
            """
            Check parameterization, access boundaries, and sensitive-data exposure before discussing optimization.
            Review query readability, naming, joins, and schema constraints for maintainability and correctness.
            Identify anti-patterns such as SELECT *, string-built SQL, or DISTINCT hiding a join problem.
            Turn findings into concrete fixes or review notes tied to the actual query and database intent.
            """,
            """
            Review this SQL for injection risk and code-quality problems.
            Check whether a migration script is safe and maintainable before performance tuning.
            Evaluate database queries for least-privilege and schema hygiene concerns.
            """,
            "sql, query review, injection, schema, permissions, code quality, database safety",
            true,
            null),
        new(
            "Optimize SQL performance",
            "optimize-sql-performance",
            "Tune SQL queries and indexing strategy by focusing on query shape, predicate selectivity, join behavior, pagination, and real execution costs.",
            SkillCategory.System,
            "Use this when database work is slow, query plans look suspicious, or a change needs a practical performance review across common SQL engines.",
            """
            Start with the slow query shape and likely access path before proposing indexes.
            Look for non-sargable predicates, over-broad selects, poor pagination, and avoidable subquery or join costs.
            Recommend indexes that fit the actual filter and sort patterns instead of generic indexing advice.
            Keep the optimization tied to measurable bottlenecks, not theoretical micro-tuning.
            """,
            """
            Optimize this slow SQL query and explain the likely indexing strategy.
            Review pagination and filtering patterns for better database performance.
            Check whether a join or subquery rewrite would materially improve execution cost.
            """,
            "sql optimization, indexes, query plan, pagination, joins, performance, database",
            true,
            null),
        new(
            "Run security review",
            "run-security-review",
            "Review code and configuration like a security researcher by tracing dangerous inputs, auth boundaries, dependency risk, secret exposure, and exploitable sinks.",
            SkillCategory.System,
            "Use this when a change touches authentication, authorization, input handling, external process execution, secrets, or any path that could expose sensitive data or side effects.",
            """
            Define the scan scope and identify the runtime, frameworks, and trust boundaries involved.
            Check dependencies and committed configuration for known security drift, leaked credentials, or weak defaults before reading business logic.
            Trace user-controlled inputs toward sensitive sinks such as database queries, file writes, rendered output, or command execution.
            Keep findings specific, severity-ranked, and tied to real exploitability instead of generic best-practice warnings.
            """,
            """
            Run a targeted security review on this auth and admin workflow.
            Check whether this API change introduces injection or access-control risk.
            Review this feature for secrets exposure, weak validation, or unsafe side effects.
            """,
            "security review, auth, authorization, injection, xss, secrets, vulnerabilities",
            true,
            null),
        new(
            "Plan threat model analysis",
            "plan-threat-model-analysis",
            "Build a practical threat model by mapping architecture, trust boundaries, attack surfaces, and prioritized risks before security work fragments into isolated findings.",
            SkillCategory.System,
            "Use this when a subsystem is security-sensitive, a release needs architectural risk review, or the team needs STRIDE-style analysis rather than only code scanning.",
            """
            Start from the real architecture and trust boundaries, not an idealized diagram.
            Identify assets, entry points, data flows, privileged operations, and abuse paths that matter for the current system shape.
            Group risks by boundary and attacker opportunity so the model explains where controls should live.
            Turn the threat model into concrete mitigations, verification points, and follow-up security work instead of a static document.
            """,
            """
            Threat-model this new service boundary before rollout.
            Map the attack surface for a workflow that handles secrets or admin operations.
            Build a focused threat model for a new endpoint or background agent path.
            """,
            "threat model, stride, trust boundary, attack surface, abuse case, architecture risk",
            true,
            null),
        new(
            "Manage secret scanning",
            "manage-secret-scanning",
            "Set up or review secret scanning and push protection so exposed credentials are caught early and remediation stays operationally clear.",
            SkillCategory.System,
            "Use this when a repo needs stronger secret hygiene, a blocked push must be understood, or credential-leak handling needs a repeatable workflow.",
            """
            Decide whether the task is repository setup, push-protection triage, custom pattern definition, or alert remediation.
            Prefer preventing secrets from landing in history over documenting cleanup after the fact.
            Review exclusions, bypasses, and custom patterns carefully so they narrow false positives without creating blind spots.
            Tie secret scanning guidance back to real remediation steps such as rotation, removal, and alert follow-up.
            """,
            """
            Configure secret scanning and push protection for this repository.
            Review a blocked push and decide whether the right fix is removal, rotation, or a justified bypass.
            Tighten secret-scanning exclusions without creating blind spots.
            """,
            "secret scanning, push protection, leaked credentials, github security, secrets, remediation",
            true,
            null),
        new(
            "Configure CodeQL scanning",
            "configure-codeql-scanning",
            "Plan or review CodeQL scanning so security analysis, workflow triggers, language coverage, and SARIF reporting fit the real repository structure.",
            SkillCategory.System,
            "Use this when a repo needs GitHub code scanning setup, better CodeQL workflow coverage, or troubleshooting around CodeQL analysis and alerts.",
            """
            Decide whether the repo needs default setup, advanced workflow control, or local CLI analysis.
            Check language coverage, build mode, triggers, and monorepo layout before editing workflow files.
            Keep permissions minimal while preserving SARIF upload and actionable analysis results.
            Treat CodeQL as part of the broader security workflow by pairing setup choices with triage and validation expectations.
            """,
            """
            Configure CodeQL scanning for this .NET repository.
            Review whether the current codeql.yml has the right languages, triggers, and permissions.
            Plan a safer CodeQL setup for a monorepo or mixed-language project.
            """,
            "codeql, code scanning, github actions, sarif, security analysis, workflow",
            true,
            null)
    ];

    private sealed record StarterSkillDefinition(
        string Name,
        string Slug,
        string Summary,
        SkillCategory Category,
        string WhenToUse,
        string Flow,
        string ExamplesText,
        string TriggerHintsText,
        bool IsPinned,
        string? WingSlug);

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
