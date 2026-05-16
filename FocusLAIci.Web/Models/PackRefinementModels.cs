using System.ComponentModel.DataAnnotations;

namespace FocusLAIci.Web.Models;

public sealed class PackBuildRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(400)]
    public string Question { get; set; } = string.Empty;

    [MaxLength(40)]
    public string GoalLabel { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Summary { get; set; } = string.Empty;

    public string ExportText { get; set; } = string.Empty;
    public string SearchTokensJson { get; set; } = "[]";
    public string SuggestedSkillNamesJson { get; set; } = "[]";
    public int ResultsPerSection { get; set; } = 6;
    public int TopMatchCount { get; set; }
    public int MemoryCount { get; set; }
    public int TodoCount { get; set; }
    public int TicketCount { get; set; }
    public int CodeGraphProjectCount { get; set; }
    public int CodeGraphFileCount { get; set; }
    public int CodeGraphNodeCount { get; set; }
    public int RecommendedSkillCount { get; set; }
    public int SuggestedExternalSkillCount { get; set; }
    public int? ReviewScore { get; set; }

    [MaxLength(500)]
    public string ReviewNotes { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ExternalSkillSource
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string CatalogUrl { get; set; } = string.Empty;

    [MaxLength(260)]
    public string Description { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastCheckedUtc { get; set; }

    [MaxLength(260)]
    public string LastCheckStatus { get; set; } = string.Empty;
}

public sealed class ExternalSkillSuggestionViewModel
{
    public string Name { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string SourceName { get; init; } = string.Empty;
    public string SkillUrl { get; init; } = string.Empty;
    public string MatchReason { get; init; } = string.Empty;
    public decimal Score { get; init; }
}

public sealed class ExternalSkillAlertViewModel
{
    public bool HasAlert => !string.IsNullOrWhiteSpace(Message);
    public bool HasSuggestions => Suggestions.Count > 0;
    public string Message { get; init; } = string.Empty;
    public string SettingsUrl { get; init; } = "/Admin/Settings";
    public IReadOnlyCollection<ExternalSkillSuggestionViewModel> Suggestions { get; init; } = Array.Empty<ExternalSkillSuggestionViewModel>();
}

public sealed class SkillSourceCardViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string CatalogUrl { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public bool IsEnabled { get; init; }
    public DateTime? LastCheckedUtc { get; init; }
    public string LastCheckStatus { get; init; } = string.Empty;
}

public sealed class SkillSourceEditorInput
{
    [Required]
    [StringLength(160)]
    [Display(Name = "Source name")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(500)]
    [Display(Name = "Catalog URL")]
    [Url]
    public string CatalogUrl { get; set; } = string.Empty;

    [StringLength(260)]
    [Display(Name = "Description")]
    public string Description { get; set; } = string.Empty;

    [Display(Name = "Enabled")]
    public bool IsEnabled { get; set; } = true;
}

public sealed class SkillSuggestionImportInput
{
    [Required]
    public string SkillUrl { get; set; } = string.Empty;

    [Required]
    public string SourceName { get; set; } = string.Empty;

    [Required]
    public string ReturnAction { get; set; } = "Index";

    [Required]
    public ContextBriefInput ContextInput { get; set; } = new();
}
