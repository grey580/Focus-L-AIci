using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FocusLAIci.Web.Models;

public sealed class DashboardViewModel
{
    public PalaceStatsViewModel Stats { get; init; } = new();
    public IReadOnlyCollection<WingSummaryViewModel> Wings { get; init; } = Array.Empty<WingSummaryViewModel>();
    public IReadOnlyCollection<MemoryCardViewModel> RecentMemories { get; init; } = Array.Empty<MemoryCardViewModel>();
    public IReadOnlyCollection<MemoryCardViewModel> PinnedMemories { get; init; } = Array.Empty<MemoryCardViewModel>();
    public IReadOnlyCollection<TodoItemViewModel> CurrentTodos { get; init; } = Array.Empty<TodoItemViewModel>();
    public IReadOnlyCollection<string> SearchExamples { get; init; } = Array.Empty<string>();
}

public sealed class SiteSettingsSnapshot
{
    public string DisplayName { get; init; } = "Focus L-AIci";
    public string HomeHeroCopy { get; init; } = "A local-first C# memory system for app development: wings, rooms, verbatim notes, searchable context, and an explorer UI for finding past reasoning fast.";
    public string TimeZoneId { get; init; } = "UTC";
    public bool ShowUtcTimestamps { get; init; }
    public int DefaultMemoryImportance { get; init; } = 3;
}

public sealed class AdminSettingsViewModel
{
    public AdminSettingsInput Input { get; init; } = new();
    public IReadOnlyCollection<SelectListItem> TimeZoneOptions { get; init; } = Array.Empty<SelectListItem>();
    public string ActiveTimeZoneLabel { get; init; } = "UTC";
}

public sealed class AdminSettingsInput
{
    [Required]
    [StringLength(80)]
    [Display(Name = "Site display name")]
    public string DisplayName { get; set; } = "Focus L-AIci";

    [Required]
    [StringLength(400)]
    [Display(Name = "Dashboard hero copy")]
    public string HomeHeroCopy { get; set; } = "A local-first C# memory system for app development: wings, rooms, verbatim notes, searchable context, and an explorer UI for finding past reasoning fast.";

    [Required]
    [StringLength(120)]
    [Display(Name = "Preferred time zone")]
    public string TimeZoneId { get; set; } = "UTC";

    [Display(Name = "Also show UTC beside localized timestamps")]
    public bool ShowUtcTimestamps { get; set; }

    [Range(1, 5)]
    [Display(Name = "Default memory importance")]
    public int DefaultMemoryImportance { get; set; } = 3;
}

public sealed class PalaceStatsViewModel
{
    public int WingCount { get; init; }
    public int RoomCount { get; init; }
    public int MemoryCount { get; init; }
    public int PinnedCount { get; init; }
    public int TagCount { get; init; }
    public int OpenTodoCount { get; init; }
    public int CompletedTodoCount { get; init; }
}

public sealed class TodoBoardViewModel
{
    public PalaceStatsViewModel Stats { get; init; } = new();
    public TodoEditorInput Input { get; init; } = new();
    public IReadOnlyCollection<TodoItemViewModel> InProgressTodos { get; init; } = Array.Empty<TodoItemViewModel>();
    public IReadOnlyCollection<TodoItemViewModel> PendingTodos { get; init; } = Array.Empty<TodoItemViewModel>();
    public IReadOnlyCollection<TodoItemViewModel> BlockedTodos { get; init; } = Array.Empty<TodoItemViewModel>();
    public IReadOnlyCollection<TodoItemViewModel> DoneTodos { get; init; } = Array.Empty<TodoItemViewModel>();
}

public sealed class TodoItemViewModel
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public TodoStatus Status { get; init; }
    public string StatusLabel { get; init; } = string.Empty;
    public DateTime CreatedUtc { get; init; }
    public DateTime UpdatedUtc { get; init; }
    public DateTime? CompletedUtc { get; init; }
}

public sealed class TodoEditorInput
{
    [Required]
    [StringLength(180)]
    [Display(Name = "Todo title")]
    public string Title { get; set; } = string.Empty;

    [DataType(DataType.MultilineText)]
    [Display(Name = "Details")]
    public string Details { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Starting status")]
    public TodoStatus Status { get; set; } = TodoStatus.Pending;
}

public sealed class WingSummaryViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int RoomCount { get; init; }
    public int MemoryCount { get; init; }
    public DateTime? LatestActivityUtc { get; init; }
}

public sealed class WingBrowseViewModel
{
    public IReadOnlyCollection<WingSummaryViewModel> Wings { get; init; } = Array.Empty<WingSummaryViewModel>();
}

public sealed class RoomBrowseViewModel
{
    public IReadOnlyCollection<RoomBrowseItemViewModel> Rooms { get; init; } = Array.Empty<RoomBrowseItemViewModel>();
}

public sealed class RoomBrowseItemViewModel
{
    public Guid RoomId { get; init; }
    public Guid WingId { get; init; }
    public string WingName { get; init; } = string.Empty;
    public string WingSlug { get; init; } = string.Empty;
    public string RoomName { get; init; } = string.Empty;
    public string RoomDescription { get; init; } = string.Empty;
    public int MemoryCount { get; init; }
}

public sealed class TagBrowseViewModel
{
    public IReadOnlyCollection<TagBrowseItemViewModel> Tags { get; init; } = Array.Empty<TagBrowseItemViewModel>();
}

public sealed class PalaceVisualizerViewModel
{
    public IReadOnlyCollection<PalaceVisualizerWingViewModel> Wings { get; init; } = Array.Empty<PalaceVisualizerWingViewModel>();
    public IReadOnlyCollection<PalaceVisualizerMemoryViewModel> UnsortedMemories { get; init; } = Array.Empty<PalaceVisualizerMemoryViewModel>();
    public IReadOnlyCollection<TagCloudItemViewModel> Tags { get; init; } = Array.Empty<TagCloudItemViewModel>();
}

public sealed class PalaceVisualizerWingViewModel
{
    public Guid WingId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyCollection<PalaceVisualizerRoomViewModel> Rooms { get; init; } = Array.Empty<PalaceVisualizerRoomViewModel>();
    public IReadOnlyCollection<PalaceVisualizerMemoryViewModel> GeneralMemories { get; init; } = Array.Empty<PalaceVisualizerMemoryViewModel>();
}

public sealed class PalaceVisualizerRoomViewModel
{
    public Guid RoomId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyCollection<PalaceVisualizerMemoryViewModel> Memories { get; init; } = Array.Empty<PalaceVisualizerMemoryViewModel>();
}

public sealed class PalaceVisualizerMemoryViewModel
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public int Importance { get; init; }
    public bool IsPinned { get; init; }
    public IReadOnlyCollection<string> TagNames { get; init; } = Array.Empty<string>();
    public IReadOnlyCollection<string> TagSlugs { get; init; } = Array.Empty<string>();
}

public sealed class TagBrowseItemViewModel
{
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public int MemoryCount { get; init; }
}

public sealed class TagCloudItemViewModel
{
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public int MemoryCount { get; init; }
    public int Weight { get; init; }
}

public sealed class MemoryCardViewModel
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string WingSlug { get; init; } = string.Empty;
    public string WingName { get; init; } = "Unsorted";
    public string RoomName { get; init; } = "General";
    public MemoryKind Kind { get; init; }
    public SourceKind SourceKind { get; init; }
    public int Importance { get; init; }
    public bool IsPinned { get; init; }
    public DateTime UpdatedUtc { get; init; }
    public IReadOnlyCollection<string> Tags { get; init; } = Array.Empty<string>();
}

public sealed class MemoryDetailViewModel
{
    public MemoryCardViewModel Memory { get; init; } = new();
    public string Content { get; init; } = string.Empty;
    public string SourceReference { get; init; } = string.Empty;
    public DateTime CreatedUtc { get; init; }
    public DateTime? OccurredUtc { get; init; }
    public IReadOnlyCollection<MemoryRelationshipViewModel> OutgoingLinks { get; init; } = Array.Empty<MemoryRelationshipViewModel>();
    public IReadOnlyCollection<MemoryRelationshipViewModel> IncomingLinks { get; init; } = Array.Empty<MemoryRelationshipViewModel>();
}

public sealed class MemoryRelationshipViewModel
{
    public Guid MemoryId { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

public sealed class ExploreViewModel
{
    public string Query { get; init; } = string.Empty;
    public Guid? WingId { get; init; }
    public Guid? RoomId { get; init; }
    public MemoryKind? Kind { get; init; }
    public string Tag { get; init; } = string.Empty;
    public IReadOnlyCollection<MemoryCardViewModel> Memories { get; init; } = Array.Empty<MemoryCardViewModel>();
    public IReadOnlyCollection<SelectListItem> WingOptions { get; init; } = Array.Empty<SelectListItem>();
    public IReadOnlyCollection<SelectListItem> RoomOptions { get; init; } = Array.Empty<SelectListItem>();
}

public sealed class WingDetailViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyCollection<RoomSummaryViewModel> Rooms { get; init; } = Array.Empty<RoomSummaryViewModel>();
    public IReadOnlyCollection<MemoryCardViewModel> Memories { get; init; } = Array.Empty<MemoryCardViewModel>();
}

public sealed class RoomSummaryViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int MemoryCount { get; init; }
}

public sealed class MemoryEditorViewModel
{
    public string Heading { get; init; } = string.Empty;
    public string SubmitLabel { get; init; } = string.Empty;
    public MemoryEditorInput Input { get; init; } = new();
    public IReadOnlyCollection<SelectListItem> WingOptions { get; init; } = Array.Empty<SelectListItem>();
    public IReadOnlyCollection<SelectListItem> RoomOptions { get; init; } = Array.Empty<SelectListItem>();
}

public sealed class MemoryEditorInput
{
    public Guid? Id { get; set; }

    [Required]
    [StringLength(200)]
    [Display(Name = "Memory title")]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(500)]
    [Display(Name = "Summary")]
    public string Summary { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Verbatim content")]
    public string Content { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Memory kind")]
    public MemoryKind Kind { get; set; } = MemoryKind.Decision;

    [Required]
    [Display(Name = "Source type")]
    public SourceKind SourceKind { get; set; } = SourceKind.ManualNote;

    [StringLength(260)]
    [Display(Name = "Source reference")]
    public string SourceReference { get; set; } = string.Empty;

    [Range(1, 5)]
    [Display(Name = "Importance")]
    public int Importance { get; set; } = 3;

    [Display(Name = "Pin this memory")]
    public bool IsPinned { get; set; }

    [Display(Name = "Occurred at")]
    [DisplayFormat(DataFormatString = "{0:yyyy-MM-ddTHH:mm}", ApplyFormatInEditMode = true)]
    public DateTime? OccurredUtc { get; set; }

    [Display(Name = "Wing")]
    public Guid? WingId { get; set; }

    [Display(Name = "Room")]
    public Guid? RoomId { get; set; }

    [Display(Name = "Tags")]
    public string TagsText { get; set; } = string.Empty;
}

public sealed class WingEditorInput
{
    [Required]
    [StringLength(120)]
    [Display(Name = "Wing name")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(400)]
    [Display(Name = "Wing description")]
    public string Description { get; set; } = string.Empty;
}

public sealed class RoomEditorViewModel
{
    public RoomEditorInput Input { get; init; } = new();
    public IReadOnlyCollection<SelectListItem> WingOptions { get; init; } = Array.Empty<SelectListItem>();
}

public sealed class RoomEditorInput
{
    [Required]
    [Display(Name = "Wing")]
    public Guid WingId { get; set; }

    [Required]
    [StringLength(120)]
    [Display(Name = "Room name")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(400)]
    [Display(Name = "Room description")]
    public string Description { get; set; } = string.Empty;
}

public sealed class PalaceApiSummaryViewModel
{
    public PalaceStatsViewModel Stats { get; init; } = new();
    public IReadOnlyCollection<WingSummaryViewModel> Wings { get; init; } = Array.Empty<WingSummaryViewModel>();
}
