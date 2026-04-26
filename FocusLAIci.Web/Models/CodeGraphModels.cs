using System.ComponentModel.DataAnnotations;

namespace FocusLAIci.Web.Models;

public sealed class CodeGraphProject
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(400)]
    public string RootPath { get; set; } = string.Empty;

    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public int FileCount { get; set; }
    public int SymbolCount { get; set; }
    public int RelationshipCount { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastScannedUtc { get; set; }
    public ICollection<CodeGraphFile> Files { get; set; } = new List<CodeGraphFile>();
    public ICollection<CodeGraphNode> Nodes { get; set; } = new List<CodeGraphNode>();
    public ICollection<CodeGraphEdge> Edges { get; set; } = new List<CodeGraphEdge>();
}

public sealed class CodeGraphFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }

    [MaxLength(320)]
    public string RelativePath { get; set; } = string.Empty;

    [MaxLength(40)]
    public string Language { get; set; } = string.Empty;

    [MaxLength(128)]
    public string ContentHash { get; set; } = string.Empty;

    public int LineCount { get; set; }
    public DateTime ScannedUtc { get; set; } = DateTime.UtcNow;
    public CodeGraphProject? Project { get; set; }
    public ICollection<CodeGraphNode> Nodes { get; set; } = new List<CodeGraphNode>();
}

public sealed class CodeGraphNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid? FileId { get; set; }

    [MaxLength(400)]
    public string NodeKey { get; set; } = string.Empty;

    [MaxLength(180)]
    public string Label { get; set; } = string.Empty;

    [MaxLength(240)]
    public string SecondaryLabel { get; set; } = string.Empty;

    public CodeGraphNodeType NodeType { get; set; }

    [MaxLength(40)]
    public string Language { get; set; } = string.Empty;

    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public string Metadata { get; set; } = string.Empty;
    public CodeGraphProject? Project { get; set; }
    public CodeGraphFile? File { get; set; }
    public ICollection<CodeGraphEdge> OutgoingEdges { get; set; } = new List<CodeGraphEdge>();
    public ICollection<CodeGraphEdge> IncomingEdges { get; set; } = new List<CodeGraphEdge>();
}

public sealed class CodeGraphEdge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Guid FromNodeId { get; set; }
    public Guid ToNodeId { get; set; }

    [MaxLength(40)]
    public string RelationshipType { get; set; } = string.Empty;

    public CodeGraphEdgeEvidence Evidence { get; set; } = CodeGraphEdgeEvidence.Extracted;
    public decimal ConfidenceScore { get; set; } = 1m;

    [MaxLength(400)]
    public string Details { get; set; } = string.Empty;

    public CodeGraphProject? Project { get; set; }
    public CodeGraphNode? FromNode { get; set; }
    public CodeGraphNode? ToNode { get; set; }
}

public enum CodeGraphNodeType
{
    File = 1,
    Namespace = 2,
    Type = 3,
    Method = 4,
    Property = 5,
    ExternalReference = 6
}

public enum CodeGraphEdgeEvidence
{
    Extracted = 1,
    Inferred = 2
}

public sealed class CodeGraphBoardViewModel
{
    public CodeGraphProjectInput Input { get; init; } = new();
    public IReadOnlyCollection<CodeGraphProjectCardViewModel> Projects { get; init; } = Array.Empty<CodeGraphProjectCardViewModel>();
}

public sealed class CodeGraphProjectCardViewModel
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string RootPath { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public int FileCount { get; init; }
    public int SymbolCount { get; init; }
    public int RelationshipCount { get; init; }
    public DateTime CreatedUtc { get; init; }
    public DateTime UpdatedUtc { get; init; }
    public DateTime? LastScannedUtc { get; init; }
}

public sealed class CodeGraphProjectDetailViewModel
{
    public CodeGraphProjectCardViewModel Project { get; init; } = new();
    public string Query { get; init; } = string.Empty;
    public Guid? SelectedNodeId { get; init; }
    public Guid? SelectedFileId { get; init; }
    public IReadOnlyCollection<CodeGraphStatCardViewModel> Stats { get; init; } = Array.Empty<CodeGraphStatCardViewModel>();
    public IReadOnlyCollection<CodeGraphNodeListItemViewModel> Hotspots { get; init; } = Array.Empty<CodeGraphNodeListItemViewModel>();
    public IReadOnlyCollection<CodeGraphFileListItemViewModel> Files { get; init; } = Array.Empty<CodeGraphFileListItemViewModel>();
    public IReadOnlyCollection<CodeGraphNodeListItemViewModel> Nodes { get; init; } = Array.Empty<CodeGraphNodeListItemViewModel>();
    public IReadOnlyCollection<CodeGraphEdgeListItemViewModel> Relationships { get; init; } = Array.Empty<CodeGraphEdgeListItemViewModel>();
    public CodeGraphThreeDimensionalSceneViewModel Scene { get; init; } = new();
    public CodeGraphSvgViewModel Graph { get; init; } = new();
    public ContextLinksPanelViewModel ContextLinks { get; init; } = new();
}

public sealed class CodeGraphProjectInput
{
    [Required(ErrorMessage = "Project name is required.")]
    [StringLength(160)]
    [Display(Name = "Project name")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Repository root path is required.")]
    [StringLength(400)]
    [Display(Name = "Repository root path")]
    public string RootPath { get; set; } = string.Empty;

    [StringLength(500)]
    [Display(Name = "Description")]
    public string Description { get; set; } = string.Empty;
}

public sealed class CodeGraphStatCardViewModel
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string HelpText { get; init; } = string.Empty;
}

public sealed class CodeGraphFileListItemViewModel
{
    public Guid Id { get; init; }
    public string RelativePath { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public int LineCount { get; init; }
    public int NodeCount { get; init; }
    public bool IsSelected { get; init; }
}

public sealed class CodeGraphNodeListItemViewModel
{
    public Guid Id { get; init; }
    public Guid? FileId { get; init; }
    public string Label { get; init; } = string.Empty;
    public string SecondaryLabel { get; init; } = string.Empty;
    public string NodeTypeLabel { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public int StartLine { get; init; }
    public int Degree { get; init; }
}

public sealed class CodeGraphEdgeListItemViewModel
{
    public string FromLabel { get; init; } = string.Empty;
    public string ToLabel { get; init; } = string.Empty;
    public string RelationshipType { get; init; } = string.Empty;
    public string EvidenceLabel { get; init; } = string.Empty;
    public decimal ConfidenceScore { get; init; }
    public string Details { get; init; } = string.Empty;
}

public sealed class CodeGraphSvgViewModel
{
    public string SelectedNodeLabel { get; init; } = string.Empty;
    public string SelectedNodeTypeLabel { get; init; } = string.Empty;
    public string SelectedNodeDetails { get; init; } = string.Empty;
    public string EmptyMessage { get; init; } = string.Empty;
    public IReadOnlyCollection<CodeGraphSvgNodeViewModel> Nodes { get; init; } = Array.Empty<CodeGraphSvgNodeViewModel>();
    public IReadOnlyCollection<CodeGraphSvgEdgeViewModel> Edges { get; init; } = Array.Empty<CodeGraphSvgEdgeViewModel>();
}

public sealed class CodeGraphSvgNodeViewModel
{
    public Guid Id { get; init; }
    public string Label { get; init; } = string.Empty;
    public string NodeTypeLabel { get; init; } = string.Empty;
    public double X { get; init; }
    public double Y { get; init; }
    public double Radius { get; init; }
    public string CssClass { get; init; } = string.Empty;
}

public sealed class CodeGraphSvgEdgeViewModel
{
    public Guid FromNodeId { get; init; }
    public Guid ToNodeId { get; init; }
    public string RelationshipType { get; init; } = string.Empty;
    public string CssClass { get; init; } = string.Empty;
}

public sealed class CodeGraphThreeDimensionalSceneViewModel
{
    public string Heading { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyCollection<CodeGraphSceneLegendItemViewModel> Legend { get; init; } = Array.Empty<CodeGraphSceneLegendItemViewModel>();
    public IReadOnlyCollection<CodeGraphSceneNodeViewModel> Nodes { get; init; } = Array.Empty<CodeGraphSceneNodeViewModel>();
    public IReadOnlyCollection<CodeGraphSceneEdgeViewModel> Edges { get; init; } = Array.Empty<CodeGraphSceneEdgeViewModel>();
}

public sealed class CodeGraphSceneLegendItemViewModel
{
    public string Label { get; init; } = string.Empty;
    public string ColorHex { get; init; } = string.Empty;
}

public sealed class CodeGraphSceneNodeViewModel
{
    public Guid Id { get; init; }
    public string Label { get; init; } = string.Empty;
    public string NodeTypeLabel { get; init; } = string.Empty;
    public string SecondaryLabel { get; init; } = string.Empty;
    public string ColorHex { get; init; } = string.Empty;
    public double X { get; init; }
    public double Y { get; init; }
    public double Z { get; init; }
    public double Radius { get; init; }
    public bool IsEmphasized { get; init; }
}

public sealed class CodeGraphSceneEdgeViewModel
{
    public Guid FromNodeId { get; init; }
    public Guid ToNodeId { get; init; }
    public string ColorHex { get; init; } = string.Empty;
}
