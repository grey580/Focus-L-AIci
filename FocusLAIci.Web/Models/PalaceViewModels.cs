using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace FocusLAIci.Web.Models;

public sealed class DashboardViewModel
{
    public PalaceStatsViewModel Stats { get; init; } = new();
    public ContextBriefInput ContextInput { get; init; } = new();
    public ContextPackViewModel? ContextPack { get; init; }
    public IReadOnlyCollection<TicketSummaryViewModel> ActiveTickets { get; init; } = Array.Empty<TicketSummaryViewModel>();
    public IReadOnlyCollection<DashboardActivityViewModel> RecentActivity { get; init; } = Array.Empty<DashboardActivityViewModel>();
    public IReadOnlyCollection<WingSummaryViewModel> Wings { get; init; } = Array.Empty<WingSummaryViewModel>();
    public IReadOnlyCollection<MemoryCardViewModel> RecentMemories { get; init; } = Array.Empty<MemoryCardViewModel>();
    public IReadOnlyCollection<MemoryCardViewModel> PinnedMemories { get; init; } = Array.Empty<MemoryCardViewModel>();
    public IReadOnlyCollection<TodoItemViewModel> CurrentTodos { get; init; } = Array.Empty<TodoItemViewModel>();
    public IReadOnlyCollection<string> MissingContextWarnings { get; init; } = Array.Empty<string>();
    public IReadOnlyCollection<DashboardWarningViewModel> MissingContextWarningItems { get; init; } = Array.Empty<DashboardWarningViewModel>();
    public IReadOnlyCollection<string> SearchExamples { get; init; } = Array.Empty<string>();
}

public sealed class DashboardDiagnosticsViewModel
{
    public DateTime GeneratedUtc { get; init; }
    public FocusDatabaseTargetSnapshot DatabaseTarget { get; init; } = new();
    public PalaceStatsViewModel Stats { get; init; } = new();
    public ContextBriefInput ContextInput { get; init; } = new();
    public string ContextSummary { get; init; } = string.Empty;
    public int TopMatchCount { get; init; }
    public IReadOnlyCollection<string> DetectedGaps { get; init; } = Array.Empty<string>();
    public IReadOnlyCollection<DashboardWarningViewModel> DetectedGapItems { get; init; } = Array.Empty<DashboardWarningViewModel>();
    public IReadOnlyCollection<RecentChangeItemViewModel> RecentChanges { get; init; } = Array.Empty<RecentChangeItemViewModel>();
    public IReadOnlyCollection<DashboardSectionSnapshotViewModel> Sections { get; init; } = Array.Empty<DashboardSectionSnapshotViewModel>();
}

public sealed class DashboardWarningViewModel
{
    public string Code { get; init; } = string.Empty;
    public string Severity { get; init; } = "warning";
    public string Message { get; init; } = string.Empty;
    public string ActionLabel { get; init; } = string.Empty;
    public string ActionUrl { get; init; } = string.Empty;
}

public sealed class DashboardSectionSnapshotViewModel
{
    public string Key { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public int Count { get; init; }
    public bool IsEmpty { get; init; }
    public IReadOnlyCollection<DashboardDiagnosticRecordViewModel> Items { get; init; } = Array.Empty<DashboardDiagnosticRecordViewModel>();
}

public sealed class DashboardDiagnosticRecordViewModel
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
}

public sealed class InspectorViewModel
{
    public DashboardDiagnosticsViewModel Diagnostics { get; init; } = new();
    public IReadOnlyCollection<RecentChangeItemViewModel> RecentChanges { get; init; } = Array.Empty<RecentChangeItemViewModel>();
    public string DiagnosticsApiUrl { get; init; } = string.Empty;
    public string RecentChangesApiUrl { get; init; } = string.Empty;
    public string WorkspaceApiUrl { get; init; } = string.Empty;
    public WorkspaceExportViewModel WorkspaceExport { get; init; } = new();
}

public sealed class RecentChangeItemViewModel
{
    public string Kind { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public DateTime ChangedUtc { get; init; }
}

public sealed class WorkspaceExportViewModel
{
    public DateTime GeneratedUtc { get; init; }
    public FocusDatabaseTargetSnapshot DatabaseTarget { get; init; } = new();
    public PalaceStatsViewModel Stats { get; init; } = new();
    public string ExportText { get; init; } = string.Empty;
    public IReadOnlyCollection<MemoryCardViewModel> PinnedMemories { get; init; } = Array.Empty<MemoryCardViewModel>();
    public IReadOnlyCollection<TodoItemViewModel> ActiveTodos { get; init; } = Array.Empty<TodoItemViewModel>();
    public IReadOnlyCollection<TicketSummaryViewModel> ActiveTickets { get; init; } = Array.Empty<TicketSummaryViewModel>();
    public IReadOnlyCollection<CodeGraphProjectCardViewModel> CodeGraphProjects { get; init; } = Array.Empty<CodeGraphProjectCardViewModel>();
    public IReadOnlyCollection<RecentChangeItemViewModel> RecentChanges { get; init; } = Array.Empty<RecentChangeItemViewModel>();
}

public sealed class TodoStatusUpdateInput
{
    [Required]
    public TodoStatus Status { get; set; }
}

public sealed class TicketStatusUpdateInput
{
    [Required]
    public TicketStatus Status { get; set; }
}

public sealed class DashboardActivityViewModel
{
    public string Label { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public DateTime OccurredUtc { get; init; }
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
    public DatabaseTargetInput DatabaseInput { get; init; } = new();
    public IReadOnlyCollection<SelectListItem> TimeZoneOptions { get; init; } = Array.Empty<SelectListItem>();
    public string ActiveTimeZoneLabel { get; init; } = "UTC";
    public FocusDatabaseTargetSnapshot DatabaseTarget { get; init; } = new();
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

public sealed class DatabaseTargetInput
{
    [Display(Name = "Use the default app database target")]
    public bool UseDefaultDatabase { get; set; }

    [Display(Name = "Database file path")]
    public string DatabasePath { get; set; } = string.Empty;
}

public sealed class FocusDatabaseTargetSnapshot
{
    public string ConnectionString { get; init; } = "Data Source=focus-palace.db";
    public string DatabasePath { get; init; } = string.Empty;
    public string DefaultDatabasePath { get; init; } = string.Empty;
    public bool UsesDefaultDatabase { get; init; }
    public string OverrideFilePath { get; init; } = string.Empty;
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
    public int OpenTicketCount { get; init; }
    public int CompletedTicketCount { get; init; }
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

public sealed class TodoDetailsViewModel
{
    public TodoItemViewModel Todo { get; init; } = new();
    public TodoEditorInput Input { get; init; } = new();
    public ContextLinksPanelViewModel ContextLinks { get; init; } = new();
}

public sealed class TodoItemViewModel
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
    public string PreviewDetails { get; init; } = string.Empty;
    public bool HasMoreDetails { get; init; }
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

public sealed class TicketBoardViewModel
{
    public const int DefaultCompletedPageSize = 5;

    public PalaceStatsViewModel Stats { get; init; } = new();
    public TicketEditorInput CreateInput { get; init; } = new();
    public int OpenTopLevelTicketCount { get; init; }
    public int CompletedTopLevelTicketCount { get; init; }
    public int OpenSubTicketCount { get; init; }
    public IReadOnlyCollection<TicketSummaryViewModel> NewTickets { get; init; } = Array.Empty<TicketSummaryViewModel>();
    public IReadOnlyCollection<TicketSummaryViewModel> InProgressTickets { get; init; } = Array.Empty<TicketSummaryViewModel>();
    public IReadOnlyCollection<TicketSummaryViewModel> BlockedTickets { get; init; } = Array.Empty<TicketSummaryViewModel>();
    public IReadOnlyCollection<TicketSummaryViewModel> CompletedTickets { get; init; } = Array.Empty<TicketSummaryViewModel>();
    public string CompletedSearch { get; init; } = string.Empty;
    public int CompletedPage { get; init; } = 1;
    public int CompletedPageSize { get; init; } = DefaultCompletedPageSize;
    public int CompletedFilteredCount { get; init; }
    public int CompletedTotalPages { get; init; }
}

public sealed class TicketDetailsViewModel
{
    public TicketDetailViewModel Ticket { get; init; } = new();
    public TicketEditorInput EditInput { get; init; } = new();
    public TicketSubTicketInput SubTicketInput { get; init; } = new();
    public TicketNoteInput NoteInput { get; init; } = new();
    public TicketTimeLogInput TimeLogInput { get; init; } = new();
    public IReadOnlyCollection<TicketSummaryViewModel> SubTickets { get; init; } = Array.Empty<TicketSummaryViewModel>();
    public IReadOnlyCollection<TicketNoteViewModel> Notes { get; init; } = Array.Empty<TicketNoteViewModel>();
    public IReadOnlyCollection<TicketTimeLogViewModel> TimeLogs { get; init; } = Array.Empty<TicketTimeLogViewModel>();
    public IReadOnlyCollection<TicketActivityViewModel> Activities { get; init; } = Array.Empty<TicketActivityViewModel>();
    public ContextLinksPanelViewModel ContextLinks { get; init; } = new();
}

public class TicketSummaryViewModel
{
    public Guid Id { get; init; }
    public Guid? ParentTicketId { get; init; }
    public string TicketNumber { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string PreviewDescription { get; init; } = string.Empty;
    public bool HasMoreDescription { get; init; }
    public TicketStatus Status { get; init; }
    public string StatusLabel { get; init; } = string.Empty;
    public TicketPriority Priority { get; init; }
    public string PriorityLabel { get; init; } = string.Empty;
    public string Assignee { get; init; } = string.Empty;
    public IReadOnlyCollection<string> Tags { get; init; } = Array.Empty<string>();
    public string GitBranch { get; init; } = string.Empty;
    public bool HasGitCommit { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime UpdatedUtc { get; init; }
    public DateTime? CompletedUtc { get; init; }
    public int SubTicketCount { get; init; }
    public int CompletedSubTicketCount { get; init; }
    public int TotalMinutesSpent { get; init; }
}

public sealed class TicketDetailViewModel : TicketSummaryViewModel
{
    public string ParentTicketNumber { get; init; } = string.Empty;
    public string ParentTicketTitle { get; init; } = string.Empty;
    public Guid? SummaryMemoryId { get; init; }
}

public sealed class TicketNoteViewModel
{
    public Guid Id { get; init; }
    public string Author { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public DateTime CreatedUtc { get; init; }
    public DateTime UpdatedUtc { get; init; }
}

public sealed class TicketTimeLogViewModel
{
    public Guid Id { get; init; }
    public string ModelName { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public int MinutesSpent { get; init; }
    public DateTime LoggedUtc { get; init; }
    public DateTime CreatedUtc { get; init; }
}

public sealed class TicketActivityViewModel
{
    public Guid Id { get; init; }
    public string ActivityType { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Metadata { get; init; } = string.Empty;
    public DateTime CreatedUtc { get; init; }
}

public sealed class TicketEditorInput
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "Ticket title is required.")]
    [StringLength(180)]
    [Display(Name = "Ticket title")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ticket description is required.")]
    [DataType(DataType.MultilineText)]
    [Display(Name = "Description")]
    public string Description { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Status")]
    public TicketStatus Status { get; set; } = TicketStatus.New;

    [Required]
    [Display(Name = "Priority")]
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;

    [Required]
    [StringLength(120)]
    [Display(Name = "Assignee")]
    public string Assignee { get; set; } = "Copilot";

    [StringLength(400)]
    [Display(Name = "Tags")]
    public string? TagsText { get; set; } = string.Empty;

    [StringLength(120)]
    [Display(Name = "Git branch")]
    public string? GitBranch { get; set; } = string.Empty;

    [Display(Name = "Git commit")]
    public bool HasGitCommit { get; set; }
}

public sealed class TicketSubTicketInput
{
    [Required]
    [StringLength(180)]
    [Display(Name = "Sub-ticket title")]
    public string Title { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.MultilineText)]
    [Display(Name = "Sub-ticket description")]
    public string Description { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Starting status")]
    public TicketStatus Status { get; set; } = TicketStatus.New;
}

public sealed class TicketNoteInput
{
    [Required]
    [StringLength(120)]
    [Display(Name = "Author")]
    public string Author { get; set; } = "Copilot";

    [Required]
    [DataType(DataType.MultilineText)]
    [Display(Name = "Note")]
    public string Content { get; set; } = string.Empty;
}

public sealed class TicketTimeLogInput
{
    [Required]
    [StringLength(120)]
    [Display(Name = "Model or owner")]
    public string ModelName { get; set; } = "Copilot";

    [Required]
    [StringLength(260)]
    [Display(Name = "Work summary")]
    public string Summary { get; set; } = string.Empty;

    [Range(1, 1440)]
    [Display(Name = "Minutes spent")]
    public int MinutesSpent { get; set; } = 30;

    [Display(Name = "Logged at (UTC)")]
    [DisplayFormat(DataFormatString = "{0:yyyy-MM-ddTHH:mm}", ApplyFormatInEditMode = true)]
    public DateTime LoggedUtc { get; set; } = DateTime.UtcNow;
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
    public ContextLinksPanelViewModel ContextLinks { get; init; } = new();
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
