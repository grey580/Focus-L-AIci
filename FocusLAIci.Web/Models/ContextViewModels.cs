using System.ComponentModel.DataAnnotations;

namespace FocusLAIci.Web.Models;

public sealed class ContextBriefInput
{
    [Required]
    [StringLength(800)]
    [Display(Name = "What are you trying to do?")]
    public string Question { get; set; } = string.Empty;

    [Display(Name = "Include completed todos and tickets")]
    public bool IncludeCompletedWork { get; set; } = true;

    [Display(Name = "Wing")]
    public Guid? WingId { get; set; }

    [Display(Name = "Room")]
    public Guid? RoomId { get; set; }

    [Display(Name = "Memory kind")]
    public MemoryKind? Kind { get; set; }

    [StringLength(120)]
    [Display(Name = "Tag")]
    public string Tag { get; set; } = string.Empty;

    [Display(Name = "Include retired memories")]
    public bool IncludeRetired { get; set; }

    [Display(Name = "Include ticket notes, activity, and time logs")]
    public bool ExpandHistory { get; set; } = true;

    [Range(3, 10)]
    [Display(Name = "Items per source")]
    public int ResultsPerSection { get; set; } = 6;

    [Display(Name = "Pack goal")]
    public ContextPackGoal PackGoal { get; set; } = ContextPackGoal.General;

    [Display(Name = "Prefer recent changes")]
    public bool PreferRecentChanges { get; set; } = true;
}

public sealed class ContextPackViewModel
{
    public Guid? ArchivedBuildId { get; init; }
    public string Question { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string GoalLabel { get; init; } = string.Empty;
    public bool NeedsMoreContext { get; init; }
    public ContextPackDecisionViewModel Decision { get; init; } = new();
    public ContextBriefInput Input { get; init; } = new();
    public IReadOnlyCollection<string> SearchTokens { get; init; } = Array.Empty<string>();
    public IReadOnlyCollection<DashboardWarningViewModel> DetectedGapItems { get; init; } = Array.Empty<DashboardWarningViewModel>();
    public IReadOnlyCollection<string> ClarifyingQuestions { get; init; } = Array.Empty<string>();
    public IReadOnlyCollection<ContextRecordViewModel> TopMatches { get; init; } = Array.Empty<ContextRecordViewModel>();
    public IReadOnlyCollection<ContextRecordViewModel> Memories { get; init; } = Array.Empty<ContextRecordViewModel>();
    public IReadOnlyCollection<ContextRecordViewModel> Todos { get; init; } = Array.Empty<ContextRecordViewModel>();
    public IReadOnlyCollection<ContextRecordViewModel> Tickets { get; init; } = Array.Empty<ContextRecordViewModel>();
    public IReadOnlyCollection<ContextRecordViewModel> CodeGraphProjects { get; init; } = Array.Empty<ContextRecordViewModel>();
    public IReadOnlyCollection<ContextRecordViewModel> CodeGraphFiles { get; init; } = Array.Empty<ContextRecordViewModel>();
    public IReadOnlyCollection<ContextRecordViewModel> CodeGraphNodes { get; init; } = Array.Empty<ContextRecordViewModel>();
    public IReadOnlyCollection<SkillCardViewModel> RecommendedSkills { get; init; } = Array.Empty<SkillCardViewModel>();
    public ExternalSkillAlertViewModel ExternalSkillAlert { get; init; } = new();
    public string ExportText { get; init; } = string.Empty;
}

public sealed class ContextPackDecisionViewModel
{
    public string Kind { get; init; } = string.Empty;
    public string PrimaryCause { get; init; } = string.Empty;
    public IReadOnlyCollection<string> Causes { get; init; } = Array.Empty<string>();
    public IReadOnlyCollection<string> Reasons { get; init; } = Array.Empty<string>();
    public IReadOnlyCollection<string> Evidence { get; init; } = Array.Empty<string>();
}

public enum ContextPackGoal
{
    General = 1,
    Debugging = 2,
    Delivery = 3,
    Research = 4,
    Architecture = 5
}

public sealed class ContextRecordViewModel
{
    public ContextRecordKind Kind { get; init; }
    public Guid Id { get; init; }
    public string KindLabel { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string Preview { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string ScoreLabel { get; init; } = string.Empty;
    public string MatchReason { get; init; } = string.Empty;
    public decimal Score { get; init; }
    public decimal SemanticScore { get; init; }
    public bool IsLinked { get; init; }
    public string FreshnessWarning { get; init; } = string.Empty;
    public string DuplicateWarning { get; init; } = string.Empty;
    public Guid? DuplicateCandidateId { get; init; }
    public ContextMatchDetailViewModel? Provenance { get; init; }
}

public sealed class ContextMatchDetailViewModel
{
    public IReadOnlyCollection<string> MatchedTokens { get; init; } = Array.Empty<string>();
    public IReadOnlyCollection<ContextMatchFieldHitViewModel> FieldHits { get; init; } = Array.Empty<ContextMatchFieldHitViewModel>();
    public IReadOnlyCollection<ContextMatchBoostViewModel> Boosts { get; init; } = Array.Empty<ContextMatchBoostViewModel>();
    public bool ExactPhraseMatched { get; init; }
}

public sealed class ContextMatchFieldHitViewModel
{
    public string FieldKey { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public IReadOnlyCollection<string> Tokens { get; init; } = Array.Empty<string>();
}

public sealed class ContextMatchBoostViewModel
{
    public string Label { get; init; } = string.Empty;
    public decimal Value { get; init; }
}

public sealed class ContextLinksPanelViewModel
{
    public ContextRecordKind SourceKind { get; init; }
    public Guid SourceId { get; init; }
    public string SourceTitle { get; init; } = string.Empty;
    public string SourceText { get; init; } = string.Empty;
    public string ReturnUrl { get; init; } = string.Empty;
    public IReadOnlyCollection<ContextLinkedItemViewModel> LinkedItems { get; init; } = Array.Empty<ContextLinkedItemViewModel>();
    public IReadOnlyCollection<ContextRecordViewModel> SuggestedItems { get; init; } = Array.Empty<ContextRecordViewModel>();
}

public sealed class ContextLinkedItemViewModel
{
    public Guid LinkId { get; init; }
    public ContextRecordKind Kind { get; init; }
    public Guid TargetId { get; init; }
    public string KindLabel { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}

public sealed class ContextLinkCreateInput
{
    public ContextRecordKind SourceKind { get; set; }
    public Guid SourceId { get; set; }
    public ContextRecordKind TargetKind { get; set; }
    public Guid TargetId { get; set; }

    [StringLength(120)]
    public string Label { get; set; } = "Related";

    public string ReturnUrl { get; set; } = "/";
}

public sealed class ContextLinkDeleteInput
{
    public Guid LinkId { get; set; }
    public string ReturnUrl { get; set; } = "/";
}

public sealed class ContextSuggestedLinksInput
{
    public ContextRecordKind SourceKind { get; set; }
    public Guid SourceId { get; set; }

    [Required]
    public string SourceText { get; set; } = string.Empty;

    [Range(1, 10)]
    public int Limit { get; set; } = 3;

    [StringLength(120)]
    public string Label { get; set; } = "Related context";

    public string ReturnUrl { get; set; } = "/";
}
