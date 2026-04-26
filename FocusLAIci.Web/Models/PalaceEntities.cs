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

public sealed class SiteSettings
{
    public int Id { get; set; } = 1;

    [MaxLength(80)]
    public string DisplayName { get; set; } = "Focus L-AIci";

    [MaxLength(400)]
    public string HomeHeroCopy { get; set; } = "A local-first C# memory system for app development: wings, rooms, verbatim notes, searchable context, and an explorer UI for finding past reasoning fast.";

    [MaxLength(120)]
    public string TimeZoneId { get; set; } = "UTC";

    public bool ShowUtcTimestamps { get; set; }

    [Range(1, 5)]
    public int DefaultMemoryImportance { get; set; } = 3;
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

public sealed class TodoEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(180)]
    public string Title { get; set; } = string.Empty;

    public string Details { get; set; } = string.Empty;

    public TodoStatus Status { get; set; } = TodoStatus.Pending;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedUtc { get; set; }
}

public sealed class TicketEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ParentTicketId { get; set; }
    public Guid? SummaryMemoryId { get; set; }

    [MaxLength(24)]
    public string TicketNumber { get; set; } = string.Empty;

    [MaxLength(180)]
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
    public TicketStatus Status { get; set; } = TicketStatus.New;
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;

    [MaxLength(120)]
    public string Assignee { get; set; } = string.Empty;

    [MaxLength(400)]
    public string TagsText { get; set; } = string.Empty;

    [MaxLength(120)]
    public string GitBranch { get; set; } = string.Empty;

    [MaxLength(80)]
    public string GitCommit { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedUtc { get; set; }
    public TicketEntry? ParentTicket { get; set; }
    public MemoryEntry? SummaryMemory { get; set; }
    public ICollection<TicketEntry> SubTickets { get; set; } = new List<TicketEntry>();
    public ICollection<TicketNoteEntry> Notes { get; set; } = new List<TicketNoteEntry>();
    public ICollection<TicketActivityEntry> Activities { get; set; } = new List<TicketActivityEntry>();
    public ICollection<TicketTimeLogEntry> TimeLogs { get; set; } = new List<TicketTimeLogEntry>();
}

public sealed class TicketNoteEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TicketId { get; set; }

    [MaxLength(120)]
    public string Author { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public TicketEntry? Ticket { get; set; }
}

public sealed class TicketActivityEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TicketId { get; set; }

    [MaxLength(60)]
    public string ActivityType { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Message { get; set; } = string.Empty;

    public string Metadata { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public TicketEntry? Ticket { get; set; }
}

public sealed class TicketTimeLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TicketId { get; set; }

    [MaxLength(120)]
    public string ModelName { get; set; } = string.Empty;

    [MaxLength(260)]
    public string Summary { get; set; } = string.Empty;

    public int MinutesSpent { get; set; }
    public DateTime LoggedUtc { get; set; } = DateTime.UtcNow;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public TicketEntry? Ticket { get; set; }
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

public sealed class ContextLinkEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ContextRecordKind SourceKind { get; set; }
    public Guid SourceId { get; set; }
    public ContextRecordKind TargetKind { get; set; }
    public Guid TargetId { get; set; }

    [MaxLength(120)]
    public string Label { get; set; } = "Related";

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
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

public enum TodoStatus
{
    [Display(Name = "Pending")]
    Pending = 1,

    [Display(Name = "In progress")]
    InProgress = 2,

    [Display(Name = "Blocked")]
    Blocked = 3,

    [Display(Name = "Done")]
    Done = 4
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

public enum TicketStatus
{
    [Display(Name = "New")]
    New = 1,

    [Display(Name = "In progress")]
    InProgress = 2,

    [Display(Name = "Blocked")]
    Blocked = 3,

    [Display(Name = "Completed")]
    Completed = 4
}

public enum TicketPriority
{
    [Display(Name = "Low")]
    Low = 1,

    [Display(Name = "Medium")]
    Medium = 2,

    [Display(Name = "High")]
    High = 3,

    [Display(Name = "Critical")]
    Critical = 4
}

public enum ContextRecordKind
{
    Memory = 1,
    Todo = 2,
    Ticket = 3,
    CodeGraphProject = 4,
    CodeGraphNode = 5,
    CodeGraphFile = 6
}
