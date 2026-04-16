using FocusLAIci.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace FocusLAIci.Web.Data;

public static class MemorySeeder
{
    public static async Task EnsureDatabaseAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FocusMemoryContext>();
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
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
}
