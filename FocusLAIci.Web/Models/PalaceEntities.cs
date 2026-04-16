using System.ComponentModel.DataAnnotations;

namespace FocusLAIci.Web.Models;

public sealed class Wing
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(160)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(400)]
    public string Description { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public ICollection<Room> Rooms { get; set; } = new List<Room>();
    public ICollection<MemoryEntry> Memories { get; set; } = new List<MemoryEntry>();
}

public sealed class Room
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WingId { get; set; }

    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(160)]
    public string Slug { get; set; } = string.Empty;

    [MaxLength(400)]
    public string Description { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public Wing? Wing { get; set; }
    public ICollection<MemoryEntry> Memories { get; set; } = new List<MemoryEntry>();
}

public sealed class Tag
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(120)]
    public string Slug { get; set; } = string.Empty;

    public ICollection<MemoryEntryTag> MemoryTags { get; set; } = new List<MemoryEntryTag>();
}

public sealed class MemoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? WingId { get; set; }
    public Guid? RoomId { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Summary { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
    public MemoryKind Kind { get; set; } = MemoryKind.Decision;
    public SourceKind SourceKind { get; set; } = SourceKind.ManualNote;

    [MaxLength(260)]
    public string SourceReference { get; set; } = string.Empty;

    public int Importance { get; set; } = 3;
    public bool IsPinned { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? OccurredUtc { get; set; }
    public Wing? Wing { get; set; }
    public Room? Room { get; set; }
    public ICollection<MemoryEntryTag> MemoryTags { get; set; } = new List<MemoryEntryTag>();
    public ICollection<MemoryLink> OutgoingLinks { get; set; } = new List<MemoryLink>();
    public ICollection<MemoryLink> IncomingLinks { get; set; } = new List<MemoryLink>();
}

public sealed class MemoryEntryTag
{
    public Guid MemoryEntryId { get; set; }
    public Guid TagId { get; set; }
    public MemoryEntry? MemoryEntry { get; set; }
    public Tag? Tag { get; set; }
}

public sealed class MemoryLink
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FromMemoryEntryId { get; set; }
    public Guid ToMemoryEntryId { get; set; }

    [MaxLength(80)]
    public string Label { get; set; } = string.Empty;

    public MemoryEntry? FromMemoryEntry { get; set; }
    public MemoryEntry? ToMemoryEntry { get; set; }
}

public enum MemoryKind
{
    Decision = 1,
    Fact = 2,
    Insight = 3,
    Incident = 4,
    Reference = 5,
    Conversation = 6,
    Task = 7
}

public enum SourceKind
{
    ManualNote = 1,
    DebugSession = 2,
    Deployment = 3,
    Architecture = 4,
    Meeting = 5,
    Import = 6,
    Research = 7
}
