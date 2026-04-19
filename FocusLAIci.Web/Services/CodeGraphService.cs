using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FocusLAIci.Web.Data;
using FocusLAIci.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace FocusLAIci.Web.Services;

public sealed partial class CodeGraphService
{
    private static readonly Dictionary<string, string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        [".cs"] = "C#",
        [".csproj"] = "MSBuild",
        [".sln"] = "Solution",
        [".slnx"] = "Solution",
        [".ts"] = "TypeScript",
        [".tsx"] = "TypeScript",
        [".js"] = "JavaScript",
        [".jsx"] = "JavaScript",
        [".py"] = "Python",
        [".go"] = "Go",
        [".ps1"] = "PowerShell",
        [".java"] = "Java"
    };

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        ".idea",
        ".vscode",
        "bin",
        "obj",
        "node_modules",
        "dist",
        "publish",
        "coverage"
    };

    private static readonly HashSet<string> ReferenceKeywordExclusions = new(StringComparer.OrdinalIgnoreCase)
    {
        "string",
        "object",
        "task",
        "tasks",
        "void",
        "bool",
        "true",
        "false",
        "null",
        "await",
        "return",
        "class",
        "public",
        "private",
        "protected",
        "internal"
    };

    private readonly FocusMemoryContext _dbContext;

    public CodeGraphService(FocusMemoryContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CodeGraphBoardViewModel> GetBoardAsync(CancellationToken cancellationToken)
    {
        var projects = await _dbContext.CodeGraphProjects
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedUtc)
            .ToListAsync(cancellationToken);

        return new CodeGraphBoardViewModel
        {
            Projects = projects.Select(MapProjectCard).ToArray()
        };
    }

    public async Task<Guid> CreateProjectAsync(CodeGraphProjectInput input, CancellationToken cancellationToken)
    {
        var rootPath = NormalizeRootPath(input.RootPath);
        if (!Directory.Exists(rootPath))
        {
            throw new InvalidOperationException("That repository root path does not exist on disk.");
        }

        var duplicateExists = await _dbContext.CodeGraphProjects
            .AnyAsync(x => x.RootPath.ToUpper() == rootPath.ToUpper(), cancellationToken);
        if (duplicateExists)
        {
            throw new InvalidOperationException("A code graph for that repository root already exists.");
        }

        var project = new CodeGraphProject
        {
            Name = input.Name.Trim(),
            RootPath = rootPath,
            Description = input.Description.Trim(),
            UpdatedUtc = DateTime.UtcNow
        };

        _dbContext.CodeGraphProjects.Add(project);
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await ScanProjectAsync(project.Id, cancellationToken);
        }
        catch
        {
            _dbContext.CodeGraphProjects.Remove(project);
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }

        return project.Id;
    }

    public async Task RescanProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        await ScanProjectAsync(projectId, cancellationToken);
    }

    public async Task<CodeGraphProjectDetailViewModel?> GetProjectAsync(Guid projectId, string? query, Guid? selectedNodeId, CancellationToken cancellationToken)
    {
        var project = await _dbContext.CodeGraphProjects
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == projectId, cancellationToken);
        if (project is null)
        {
            return null;
        }

        var files = await _dbContext.CodeGraphFiles
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderBy(x => x.RelativePath)
            .ToListAsync(cancellationToken);

        var nodes = await _dbContext.CodeGraphNodes
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .OrderBy(x => x.NodeType)
            .ThenBy(x => x.Label)
            .ToListAsync(cancellationToken);

        var edges = await _dbContext.CodeGraphEdges
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId)
            .ToListAsync(cancellationToken);

        var fileLookup = files.ToDictionary(x => x.Id);
        var nodeLookup = nodes.ToDictionary(x => x.Id);
        var nodeDegree = edges
            .SelectMany(x => new[] { x.FromNodeId, x.ToNodeId })
            .GroupBy(x => x)
            .ToDictionary(x => x.Key, x => x.Count());

        var normalizedQuery = query?.Trim() ?? string.Empty;
        var filteredNodes = nodes
            .Where(x =>
                string.IsNullOrWhiteSpace(normalizedQuery)
                || x.Label.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || x.SecondaryLabel.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)
                || (x.FileId.HasValue
                    && fileLookup.TryGetValue(x.FileId.Value, out var file)
                    && file.RelativePath.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        var effectiveSelectedNode = selectedNodeId.HasValue && nodeLookup.ContainsKey(selectedNodeId.Value)
            ? nodeLookup[selectedNodeId.Value]
            : filteredNodes
                .OrderByDescending(x => nodeDegree.GetValueOrDefault(x.Id))
                .ThenBy(x => x.Label)
                .FirstOrDefault();

        IEnumerable<CodeGraphEdge> projectRelationships = effectiveSelectedNode is null
            ? edges
            : edges.Where(x => x.FromNodeId == effectiveSelectedNode.Id || x.ToNodeId == effectiveSelectedNode.Id).ToArray();

        return new CodeGraphProjectDetailViewModel
        {
            Project = MapProjectCard(project),
            Query = normalizedQuery,
            SelectedNodeId = effectiveSelectedNode?.Id,
            Stats =
            [
                new CodeGraphStatCardViewModel { Label = "Files", Value = project.FileCount.ToString("N0"), HelpText = "Tracked source files in this graph." },
                new CodeGraphStatCardViewModel { Label = "Symbols", Value = project.SymbolCount.ToString("N0"), HelpText = "Types, methods, properties, and namespaces." },
                new CodeGraphStatCardViewModel { Label = "Edges", Value = project.RelationshipCount.ToString("N0"), HelpText = "Declares, imports, contains, and inferred references." },
                new CodeGraphStatCardViewModel { Label = "Languages", Value = files.Select(x => x.Language).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString("N0"), HelpText = "Detected source-language families." }
            ],
            Hotspots = nodes
                .OrderByDescending(x => nodeDegree.GetValueOrDefault(x.Id))
                .ThenBy(x => x.Label)
                .Take(8)
                .Select(x => MapNode(x, nodeDegree.GetValueOrDefault(x.Id)))
                .ToArray(),
            Files = files
                .Select(file => new CodeGraphFileListItemViewModel
                {
                    Id = file.Id,
                    RelativePath = file.RelativePath,
                    Language = file.Language,
                    LineCount = file.LineCount,
                    NodeCount = nodes.Count(node => node.FileId == file.Id && node.NodeType != CodeGraphNodeType.File)
                })
                .ToArray(),
            Nodes = filteredNodes
                .OrderByDescending(x => nodeDegree.GetValueOrDefault(x.Id))
                .ThenBy(x => x.Label)
                .Take(40)
                .Select(x => MapNode(x, nodeDegree.GetValueOrDefault(x.Id)))
                .ToArray(),
            Relationships = projectRelationships
                .OrderByDescending(x => x.ConfidenceScore)
                .ThenBy(x => x.RelationshipType)
                .Take(30)
                .Select(edge => MapEdge(edge, nodeLookup))
                .ToArray(),
            Scene = BuildThreeDimensionalScene(project.Name, filteredNodes, edges, nodeDegree, effectiveSelectedNode),
            Graph = BuildGraphSvg(effectiveSelectedNode, edges, nodeLookup)
        };
    }

    private async Task ScanProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await _dbContext.CodeGraphProjects
            .FirstOrDefaultAsync(x => x.Id == projectId, cancellationToken);
        if (project is null)
        {
            throw new InvalidOperationException("That code graph project no longer exists.");
        }

        if (!Directory.Exists(project.RootPath))
        {
            throw new InvalidOperationException("The repository root path for this code graph no longer exists.");
        }

        await _dbContext.CodeGraphEdges.Where(x => x.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.CodeGraphNodes.Where(x => x.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);
        await _dbContext.CodeGraphFiles.Where(x => x.ProjectId == projectId).ExecuteDeleteAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var files = new List<CodeGraphFile>();
        var nodes = new List<CodeGraphNode>();
        var edges = new List<CodeGraphEdge>();
        var nodeByKey = new Dictionary<string, CodeGraphNode>(StringComparer.OrdinalIgnoreCase);
        var fileScans = new List<FileScanState>();

        foreach (var filePath in EnumerateProjectFiles(project.RootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var extension = Path.GetExtension(filePath);
            if (!SupportedExtensions.TryGetValue(extension, out var language))
            {
                continue;
            }

            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            var relativePath = Path.GetRelativePath(project.RootPath, filePath).Replace('\\', '/');
            var lineCount = string.IsNullOrEmpty(content) ? 0 : content.Count(ch => ch == '\n') + 1;
            var file = new CodeGraphFile
            {
                ProjectId = projectId,
                RelativePath = relativePath,
                Language = language,
                LineCount = lineCount,
                ScannedUtc = now,
                ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))
            };

            files.Add(file);

            var fileNode = AddNode(nodes, nodeByKey, new CodeGraphNode
            {
                ProjectId = projectId,
                File = file,
                FileId = file.Id,
                NodeKey = $"file:{relativePath}",
                Label = Path.GetFileName(relativePath),
                SecondaryLabel = relativePath,
                NodeType = CodeGraphNodeType.File,
                Language = language
            });

            var extraction = ExtractFile(language, relativePath, content);
            var declaredNodeIds = new HashSet<Guid>();

            foreach (var namespaceName in extraction.Namespaces)
            {
                var namespaceNode = AddNode(nodes, nodeByKey, new CodeGraphNode
                {
                    ProjectId = projectId,
                    File = file,
                    FileId = file.Id,
                    NodeKey = $"namespace:{relativePath}:{namespaceName}",
                    Label = namespaceName,
                    SecondaryLabel = relativePath,
                    NodeType = CodeGraphNodeType.Namespace,
                    Language = language
                });
                declaredNodeIds.Add(namespaceNode.Id);
                AddEdge(edges, projectId, fileNode.Id, namespaceNode.Id, "declares", CodeGraphEdgeEvidence.Extracted, 1m, "Namespace declaration.");
            }

            var typeNodesByName = new List<(int Line, CodeGraphNode Node)>();
            foreach (var symbol in extraction.Symbols.OrderBy(x => x.StartLine))
            {
                var node = AddNode(nodes, nodeByKey, new CodeGraphNode
                {
                    ProjectId = projectId,
                    File = file,
                    FileId = file.Id,
                    NodeKey = $"{symbol.NodeType}:{relativePath}:{symbol.Label}:{symbol.StartLine}",
                    Label = symbol.Label,
                    SecondaryLabel = symbol.SecondaryLabel,
                    NodeType = symbol.NodeType,
                    Language = language,
                    StartLine = symbol.StartLine,
                    EndLine = symbol.EndLine,
                    Metadata = symbol.Metadata
                });

                declaredNodeIds.Add(node.Id);
                AddEdge(edges, projectId, fileNode.Id, node.Id, "declares", CodeGraphEdgeEvidence.Extracted, 1m, "Declared in file.");

                if (node.NodeType == CodeGraphNodeType.Type)
                {
                    typeNodesByName.Add((node.StartLine, node));
                }
            }

            foreach (var methodOrProperty in nodes
                         .Where(x => x.FileId == file.Id && (x.NodeType == CodeGraphNodeType.Method || x.NodeType == CodeGraphNodeType.Property))
                         .OrderBy(x => x.StartLine)
                         .ToArray())
            {
                var parentType = typeNodesByName
                    .Where(x => x.Line < methodOrProperty.StartLine)
                    .OrderByDescending(x => x.Line)
                    .Select(x => x.Node)
                    .FirstOrDefault();

                if (parentType is not null)
                {
                    AddEdge(edges, projectId, parentType.Id, methodOrProperty.Id, "contains", CodeGraphEdgeEvidence.Extracted, 1m, "Contained within type.");
                }
            }

            foreach (var reference in extraction.ExternalReferences)
            {
                var externalNode = AddNode(nodes, nodeByKey, new CodeGraphNode
                {
                    ProjectId = projectId,
                    NodeKey = $"external:{language}:{reference}",
                    Label = reference,
                    SecondaryLabel = language,
                    NodeType = CodeGraphNodeType.ExternalReference,
                    Language = language
                });

                AddEdge(edges, projectId, fileNode.Id, externalNode.Id, "imports", CodeGraphEdgeEvidence.Extracted, 1m, "Explicit import or using statement.");
            }

            fileScans.Add(new FileScanState(file, fileNode, content, declaredNodeIds));
        }

        var symbolLookup = nodes
            .Where(x => x.NodeType is CodeGraphNodeType.Namespace or CodeGraphNodeType.Type or CodeGraphNodeType.Method or CodeGraphNodeType.Property)
            .GroupBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1 && group.Key.Length >= 4 && !ReferenceKeywordExclusions.Contains(group.Key))
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.OrdinalIgnoreCase);

        foreach (var fileScan in fileScans)
        {
            var fileTokens = WordTokenRegex().Matches(fileScan.Content)
                .Select(x => x.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var symbol in symbolLookup.Values
                         .Where(symbol => !fileScan.DeclaredNodeIds.Contains(symbol.Id)
                                          && fileTokens.Contains(symbol.Label)
                                          && symbol.FileId != fileScan.File.Id)
                         .OrderBy(symbol => symbol.Label)
                         .Take(24))
            {
                AddEdge(edges, projectId, fileScan.FileNode.Id, symbol.Id, "references", CodeGraphEdgeEvidence.Inferred, 0.55m, "Matched a unique symbol token in the file.");
            }
        }

        _dbContext.CodeGraphFiles.AddRange(files);
        _dbContext.CodeGraphNodes.AddRange(nodes);
        _dbContext.CodeGraphEdges.AddRange(edges);

        project.FileCount = files.Count;
        project.SymbolCount = nodes.Count(x => x.NodeType != CodeGraphNodeType.File && x.NodeType != CodeGraphNodeType.ExternalReference);
        project.RelationshipCount = edges.Count;
        project.LastScannedUtc = now;
        project.UpdatedUtc = now;
        project.Summary = BuildSummary(project.Name, files, nodes, edges);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string BuildSummary(IReadOnlyCollection<CodeGraphFile> files, IReadOnlyCollection<CodeGraphNode> nodes, IReadOnlyCollection<CodeGraphEdge> edges)
    {
        var languageSummary = files
            .GroupBy(x => x.Language)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => x.Key)
            .Take(3)
            .Select(x => $"{x.Key} ({x.Count()})");

        var hotspots = edges
            .SelectMany(x => new[] { x.FromNodeId, x.ToNodeId })
            .GroupBy(x => x)
            .OrderByDescending(x => x.Count())
            .Take(3)
            .Join(nodes, degree => degree.Key, node => node.Id, (degree, node) => $"{node.Label} ({degree.Count()} links)");

        return $"Scanned {files.Count} files across {files.Select(x => x.Language).Distinct(StringComparer.OrdinalIgnoreCase).Count()} languages. Top languages: {string.Join(", ", languageSummary)}. Highest-connectivity nodes: {string.Join(", ", hotspots)}.";
    }

    private static string BuildSummary(string projectName, IReadOnlyCollection<CodeGraphFile> files, IReadOnlyCollection<CodeGraphNode> nodes, IReadOnlyCollection<CodeGraphEdge> edges)
    {
        var baseSummary = BuildSummary(files, nodes, edges);
        return $"{projectName} code graph. {baseSummary}";
    }

    private static CodeGraphNode AddNode(ICollection<CodeGraphNode> nodes, IDictionary<string, CodeGraphNode> nodeByKey, CodeGraphNode node)
    {
        if (nodeByKey.TryGetValue(node.NodeKey, out var existing))
        {
            return existing;
        }

        nodes.Add(node);
        nodeByKey[node.NodeKey] = node;
        return node;
    }

    private static void AddEdge(ICollection<CodeGraphEdge> edges, Guid projectId, Guid fromNodeId, Guid toNodeId, string relationshipType, CodeGraphEdgeEvidence evidence, decimal confidenceScore, string details)
    {
        if (fromNodeId == toNodeId)
        {
            return;
        }

        if (edges.Any(edge => edge.FromNodeId == fromNodeId
                              && edge.ToNodeId == toNodeId
                              && edge.RelationshipType == relationshipType))
        {
            return;
        }

        edges.Add(new CodeGraphEdge
        {
            ProjectId = projectId,
            FromNodeId = fromNodeId,
            ToNodeId = toNodeId,
            RelationshipType = relationshipType,
            Evidence = evidence,
            ConfidenceScore = confidenceScore,
            Details = details
        });
    }

    private static string NormalizeRootPath(string rootPath)
        => Path.GetFullPath(rootPath.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static IEnumerable<string> EnumerateProjectFiles(string rootPath)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            var current = pending.Pop();

            foreach (var directory in Directory.EnumerateDirectories(current))
            {
                if (SkippedDirectories.Contains(Path.GetFileName(directory)))
                {
                    continue;
                }

                pending.Push(directory);
            }

            foreach (var file in Directory.EnumerateFiles(current))
            {
                yield return file;
            }
        }
    }

    private static FileExtraction ExtractFile(string language, string relativePath, string content)
        => language switch
        {
            "C#" => ExtractCSharp(relativePath, content),
            "JavaScript" or "TypeScript" => ExtractJavaScript(relativePath, content),
            "Python" => ExtractPython(relativePath, content),
            "Go" => ExtractGo(relativePath, content),
            "PowerShell" => ExtractPowerShell(relativePath, content),
            _ => new FileExtraction()
        };

    private static FileExtraction ExtractCSharp(string relativePath, string content)
    {
        var extraction = new FileExtraction();
        extraction.Namespaces.UnionWith(NamespaceRegex().Matches(content).Select(x => x.Groups["name"].Value));
        extraction.ExternalReferences.UnionWith(UsingRegex().Matches(content).Select(x => x.Groups["name"].Value));

        foreach (Match match in CSharpTypeRegex().Matches(content))
        {
            extraction.Symbols.Add(BuildSymbol(relativePath, content, match, CodeGraphNodeType.Type, $"{match.Groups["kind"].Value} in {relativePath}"));
        }

        foreach (Match match in CSharpMethodRegex().Matches(content))
        {
            var methodName = match.Groups["name"].Value;
            if (ReferenceKeywordExclusions.Contains(methodName))
            {
                continue;
            }

            extraction.Symbols.Add(BuildSymbol(relativePath, content, match, CodeGraphNodeType.Method, $"{match.Groups["returnType"].Value.Trim()} in {relativePath}"));
        }

        foreach (Match match in CSharpPropertyRegex().Matches(content))
        {
            extraction.Symbols.Add(BuildSymbol(relativePath, content, match, CodeGraphNodeType.Property, $"{match.Groups["type"].Value.Trim()} in {relativePath}"));
        }

        return extraction;
    }

    private static FileExtraction ExtractJavaScript(string relativePath, string content)
    {
        var extraction = new FileExtraction();
        extraction.ExternalReferences.UnionWith(JavaScriptImportRegex().Matches(content).Select(x => x.Groups["name"].Value));

        foreach (Match match in JavaScriptTypeRegex().Matches(content))
        {
            extraction.Symbols.Add(BuildSymbol(relativePath, content, match, CodeGraphNodeType.Type, $"{match.Groups["kind"].Value} in {relativePath}"));
        }

        foreach (Match match in JavaScriptFunctionRegex().Matches(content))
        {
            extraction.Symbols.Add(BuildSymbol(relativePath, content, match, CodeGraphNodeType.Method, $"function in {relativePath}"));
        }

        return extraction;
    }

    private static FileExtraction ExtractPython(string relativePath, string content)
    {
        var extraction = new FileExtraction();
        extraction.ExternalReferences.UnionWith(PythonImportRegex().Matches(content).Select(x => x.Groups["name"].Value));

        foreach (Match match in PythonClassRegex().Matches(content))
        {
            extraction.Symbols.Add(BuildSymbol(relativePath, content, match, CodeGraphNodeType.Type, $"class in {relativePath}"));
        }

        foreach (Match match in PythonFunctionRegex().Matches(content))
        {
            extraction.Symbols.Add(BuildSymbol(relativePath, content, match, CodeGraphNodeType.Method, $"function in {relativePath}"));
        }

        return extraction;
    }

    private static FileExtraction ExtractGo(string relativePath, string content)
    {
        var extraction = new FileExtraction();
        extraction.Namespaces.UnionWith(GoPackageRegex().Matches(content).Select(x => x.Groups["name"].Value));
        extraction.ExternalReferences.UnionWith(GoImportRegex().Matches(content).Select(x => x.Groups["name"].Value));

        foreach (Match match in GoTypeRegex().Matches(content))
        {
            extraction.Symbols.Add(BuildSymbol(relativePath, content, match, CodeGraphNodeType.Type, $"type in {relativePath}"));
        }

        foreach (Match match in GoFunctionRegex().Matches(content))
        {
            extraction.Symbols.Add(BuildSymbol(relativePath, content, match, CodeGraphNodeType.Method, $"function in {relativePath}"));
        }

        return extraction;
    }

    private static FileExtraction ExtractPowerShell(string relativePath, string content)
    {
        var extraction = new FileExtraction();
        extraction.ExternalReferences.UnionWith(PowerShellImportRegex().Matches(content).Select(x => x.Groups["name"].Value));

        foreach (Match match in PowerShellFunctionRegex().Matches(content))
        {
            extraction.Symbols.Add(BuildSymbol(relativePath, content, match, CodeGraphNodeType.Method, $"function in {relativePath}"));
        }

        return extraction;
    }

    private static ExtractedSymbol BuildSymbol(string relativePath, string content, Match match, CodeGraphNodeType nodeType, string secondaryLabel)
    {
        var startLine = GetLineNumber(content, match.Index);
        return new ExtractedSymbol(
            nodeType,
            match.Groups["name"].Value,
            secondaryLabel,
            startLine,
            startLine,
            relativePath);
    }

    private static int GetLineNumber(string content, int index)
    {
        var count = 1;
        for (var i = 0; i < index && i < content.Length; i++)
        {
            if (content[i] == '\n')
            {
                count++;
            }
        }

        return count;
    }

    private static CodeGraphProjectCardViewModel MapProjectCard(CodeGraphProject project)
        => new()
        {
            Id = project.Id,
            Name = project.Name,
            RootPath = project.RootPath,
            Description = project.Description,
            Summary = project.Summary,
            FileCount = project.FileCount,
            SymbolCount = project.SymbolCount,
            RelationshipCount = project.RelationshipCount,
            CreatedUtc = project.CreatedUtc,
            UpdatedUtc = project.UpdatedUtc,
            LastScannedUtc = project.LastScannedUtc
        };

    private static CodeGraphNodeListItemViewModel MapNode(CodeGraphNode node, int degree)
        => new()
        {
            Id = node.Id,
            FileId = node.FileId,
            Label = node.Label,
            SecondaryLabel = node.SecondaryLabel,
            NodeTypeLabel = node.NodeType.ToString(),
            Language = node.Language,
            StartLine = node.StartLine,
            Degree = degree
        };

    private static CodeGraphEdgeListItemViewModel MapEdge(CodeGraphEdge edge, IReadOnlyDictionary<Guid, CodeGraphNode> nodeLookup)
        => new()
        {
            FromLabel = nodeLookup.TryGetValue(edge.FromNodeId, out var fromNode) ? fromNode.Label : "Unknown",
            ToLabel = nodeLookup.TryGetValue(edge.ToNodeId, out var toNode) ? toNode.Label : "Unknown",
            RelationshipType = edge.RelationshipType,
            EvidenceLabel = edge.Evidence.ToString(),
            ConfidenceScore = edge.ConfidenceScore,
            Details = edge.Details
        };

    private static CodeGraphThreeDimensionalSceneViewModel BuildThreeDimensionalScene(
        string projectName,
        IReadOnlyCollection<CodeGraphNode> filteredNodes,
        IReadOnlyCollection<CodeGraphEdge> edges,
        IReadOnlyDictionary<Guid, int> nodeDegree,
        CodeGraphNode? selectedNode)
    {
        var primaryNodes = filteredNodes
            .OrderByDescending(node => nodeDegree.GetValueOrDefault(node.Id))
            .ThenBy(node => node.Label)
            .Take(48)
            .ToList();

        if (selectedNode is not null && primaryNodes.All(node => node.Id != selectedNode.Id))
        {
            primaryNodes.Insert(0, selectedNode);
        }

        var selectedIds = primaryNodes
            .Select(node => node.Id)
            .Distinct()
            .ToHashSet();

        var sceneEdges = edges
            .Where(edge => selectedIds.Contains(edge.FromNodeId) && selectedIds.Contains(edge.ToNodeId))
            .OrderByDescending(edge => nodeDegree.GetValueOrDefault(edge.FromNodeId) + nodeDegree.GetValueOrDefault(edge.ToNodeId))
            .ThenBy(edge => edge.RelationshipType)
            .Take(180)
            .ToArray();

        var nodesByType = primaryNodes
            .GroupBy(node => node.NodeType)
            .OrderBy(group => group.Key)
            .ToArray();

        var groupAnchors = new Dictionary<CodeGraphNodeType, (double X, double Y, double Z)>
        {
            [CodeGraphNodeType.File] = (0d, -34d, 68d),
            [CodeGraphNodeType.Namespace] = (-196d, -134d, -92d),
            [CodeGraphNodeType.Type] = (184d, 44d, -24d),
            [CodeGraphNodeType.Method] = (-92d, 174d, 38d),
            [CodeGraphNodeType.Property] = (206d, -166d, 78d),
            [CodeGraphNodeType.ExternalReference] = (-244d, 34d, 126d)
        };

        var sceneNodes = new List<CodeGraphSceneNodeViewModel>();

        foreach (var group in nodesByType)
        {
            var anchor = groupAnchors[group.Key];
            var orderedGroup = group
                .OrderByDescending(node => nodeDegree.GetValueOrDefault(node.Id))
                .ThenBy(node => node.Label)
                .ToArray();

            for (var index = 0; index < orderedGroup.Length; index++)
            {
                var node = orderedGroup[index];
                var angle = ((index * 137.5d) + StableUnitValue(node.Id, 11) * 180d) * Math.PI / 180d;
                var radius = 72d + (index * 18d) + (StableUnitValue(node.Id, 7) * 34d);
                var zOffset = (StableUnitValue(node.Id, 5) - 0.5d) * 236d + anchor.Z;
                var x = anchor.X + Math.Cos(angle) * radius;
                var y = anchor.Y + Math.Sin(angle) * radius * 0.86d;
                var degree = nodeDegree.GetValueOrDefault(node.Id);
                var isEmphasized = selectedNode?.Id == node.Id || degree >= 18 || index < 2;

                sceneNodes.Add(new CodeGraphSceneNodeViewModel
                {
                    Id = node.Id,
                    Label = node.Label,
                    NodeTypeLabel = node.NodeType.ToString(),
                    SecondaryLabel = node.SecondaryLabel,
                    ColorHex = GetNodeTypeColor(group.Key),
                    X = x,
                    Y = y,
                    Z = zOffset,
                    Radius = Math.Min(10d + Math.Sqrt(Math.Max(1d, degree)) * 0.92d, 18d),
                    IsEmphasized = isEmphasized
                });
            }
        }

        var presentTypes = sceneNodes
            .Select(node => node.NodeTypeLabel)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(label => label)
            .ToArray();

        return new CodeGraphThreeDimensionalSceneViewModel
        {
            Heading = $"{projectName} 3D code graph",
            Summary = "A rotating structural map of the persisted code graph, colored by node type and sized by connectivity.",
            Legend = presentTypes
                .Select(label =>
                {
                    var nodeType = Enum.Parse<CodeGraphNodeType>(label, ignoreCase: false);
                    return new CodeGraphSceneLegendItemViewModel
                    {
                        Label = label,
                        ColorHex = GetNodeTypeColor(nodeType)
                    };
                })
                .ToArray(),
            Nodes = sceneNodes,
            Edges = sceneEdges
                .Select(edge => new CodeGraphSceneEdgeViewModel
                {
                    FromNodeId = edge.FromNodeId,
                    ToNodeId = edge.ToNodeId,
                    ColorHex = edge.RelationshipType == "references" ? "#FFB76A" : "#8AB4F8"
                })
                .ToArray()
        };
    }

    private static CodeGraphSvgViewModel BuildGraphSvg(CodeGraphNode? selectedNode, IReadOnlyCollection<CodeGraphEdge> edges, IReadOnlyDictionary<Guid, CodeGraphNode> nodeLookup)
    {
        if (selectedNode is null)
        {
            return new CodeGraphSvgViewModel
            {
                EmptyMessage = "No nodes matched this graph yet. Scan a repository or widen the current query."
            };
        }

        var neighborEdges = edges
            .Where(x => x.FromNodeId == selectedNode.Id || x.ToNodeId == selectedNode.Id)
            .Take(10)
            .ToArray();

        var neighbors = neighborEdges
            .Select(edge => edge.FromNodeId == selectedNode.Id ? edge.ToNodeId : edge.FromNodeId)
            .Distinct()
            .Where(nodeLookup.ContainsKey)
            .Select(id => nodeLookup[id])
            .ToArray();

        var svgNodes = new List<CodeGraphSvgNodeViewModel>
        {
            new()
            {
                Id = selectedNode.Id,
                Label = selectedNode.Label,
                NodeTypeLabel = selectedNode.NodeType.ToString(),
                X = 320,
                Y = 190,
                Radius = 34,
                CssClass = "code-graph-node-center"
            }
        };

        var svgEdges = new List<CodeGraphSvgEdgeViewModel>();
        var angleStep = neighbors.Length == 0 ? 360d : 360d / neighbors.Length;

        for (var index = 0; index < neighbors.Length; index++)
        {
            var angleRadians = ((angleStep * index) - 90d) * Math.PI / 180d;
            var x = 320 + Math.Cos(angleRadians) * 175d;
            var y = 190 + Math.Sin(angleRadians) * 135d;
            var neighbor = neighbors[index];

            svgNodes.Add(new CodeGraphSvgNodeViewModel
            {
                Id = neighbor.Id,
                Label = neighbor.Label,
                NodeTypeLabel = neighbor.NodeType.ToString(),
                X = x,
                Y = y,
                Radius = 24,
                CssClass = "code-graph-node-neighbor"
            });

            var relationshipType = neighborEdges
                .First(edge => (edge.FromNodeId == selectedNode.Id && edge.ToNodeId == neighbor.Id)
                               || (edge.ToNodeId == selectedNode.Id && edge.FromNodeId == neighbor.Id))
                .RelationshipType;

            svgEdges.Add(new CodeGraphSvgEdgeViewModel
            {
                FromNodeId = selectedNode.Id,
                ToNodeId = neighbor.Id,
                RelationshipType = relationshipType,
                CssClass = relationshipType == "references" ? "code-graph-edge-inferred" : "code-graph-edge-extracted"
            });
        }

        return new CodeGraphSvgViewModel
        {
            SelectedNodeLabel = selectedNode.Label,
            SelectedNodeTypeLabel = selectedNode.NodeType.ToString(),
            SelectedNodeDetails = string.IsNullOrWhiteSpace(selectedNode.SecondaryLabel)
                ? selectedNode.Language
                : $"{selectedNode.SecondaryLabel} • {selectedNode.Language}",
            Nodes = svgNodes,
            Edges = svgEdges
        };
    }

    private static string GetNodeTypeColor(CodeGraphNodeType nodeType)
        => nodeType switch
        {
            CodeGraphNodeType.File => "#6FA8FF",
            CodeGraphNodeType.Namespace => "#F49BC2",
            CodeGraphNodeType.Type => "#FFB76A",
            CodeGraphNodeType.Method => "#68D668",
            CodeGraphNodeType.Property => "#B8D0FF",
            CodeGraphNodeType.ExternalReference => "#E7DF74",
            _ => "#D4D8E1"
        };

    private static double StableUnitValue(Guid id, int salt)
    {
        var bytes = id.ToByteArray();
        var offset = Math.Abs(salt % bytes.Length);
        var chunk = BitConverter.ToUInt32(bytes, offset <= bytes.Length - sizeof(uint) ? offset : bytes.Length - sizeof(uint));
        return chunk / (double)uint.MaxValue;
    }

    [GeneratedRegex(@"\b[A-Za-z_][A-Za-z0-9_]{2,}\b", RegexOptions.Compiled)]
    private static partial Regex WordTokenRegex();

    [GeneratedRegex(@"^\s*namespace\s+(?<name>[A-Za-z_][A-Za-z0-9_\.]+)", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex NamespaceRegex();

    [GeneratedRegex(@"^\s*using\s+(?:static\s+)?(?:(?:[A-Za-z_][A-Za-z0-9_]*)\s*=\s*)?(?<name>[A-Za-z_][A-Za-z0-9_\.]+)\s*;", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex UsingRegex();

    [GeneratedRegex(@"^\s*(?:public|private|protected|internal|static|sealed|abstract|partial|\s)+(?:record|class|interface|struct|enum)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex CSharpTypeRegex();

    [GeneratedRegex(@"^\s*(?:public|private|protected|internal|static|virtual|override|sealed|abstract|async|partial|new|extern|\s)+(?<returnType>[A-Za-z0-9_<>\[\]\.,\?]+)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex CSharpMethodRegex();

    [GeneratedRegex(@"^\s*(?:public|private|protected|internal|static|virtual|override|sealed|abstract|required|init|\s)+(?<type>[A-Za-z0-9_<>\[\]\.,\?]+)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\{\s*(?:get|set|init)\s*;", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex CSharpPropertyRegex();

    [GeneratedRegex(@"(?:import\s+.*?\s+from\s+['""](?<name>[^'""]+)['""]|require\(\s*['""](?<name>[^'""]+)['""]\s*\)|export\s+.*?\s+from\s+['""](?<name>[^'""]+)['""])", RegexOptions.Compiled)]
    private static partial Regex JavaScriptImportRegex();

    [GeneratedRegex(@"^\s*(?:export\s+)?(?<kind>class|interface|type|enum)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex JavaScriptTypeRegex();

    [GeneratedRegex(@"^\s*(?:export\s+)?function\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(|^\s*(?:const|let|var)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:async\s*)?\(", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex JavaScriptFunctionRegex();

    [GeneratedRegex(@"^\s*(?:from\s+(?<name>[A-Za-z_][A-Za-z0-9_\.]*)\s+import|import\s+(?<name>[A-Za-z_][A-Za-z0-9_\.]*))", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex PythonImportRegex();

    [GeneratedRegex(@"^\s*class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex PythonClassRegex();

    [GeneratedRegex(@"^\s*(?:async\s+)?def\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex PythonFunctionRegex();

    [GeneratedRegex(@"^\s*package\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex GoPackageRegex();

    [GeneratedRegex(@"^\s*(?:import\s+(?:[A-Za-z_][A-Za-z0-9_]*\s+)?)?""(?<name>[^""]+)""", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex GoImportRegex();

    [GeneratedRegex(@"^\s*type\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s+", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex GoTypeRegex();

    [GeneratedRegex(@"^\s*func\s+(?:\([^)]+\)\s*)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\(", RegexOptions.Compiled | RegexOptions.Multiline)]
    private static partial Regex GoFunctionRegex();

    [GeneratedRegex(@"(?:Import-Module\s+['""]?(?<name>[A-Za-z0-9_\-\.\\/:]+)['""]?|using\s+module\s+['""]?(?<name>[A-Za-z0-9_\-\.\\/:]+)['""]?)", RegexOptions.Compiled | RegexOptions.IgnoreCase)]
    private static partial Regex PowerShellImportRegex();

    [GeneratedRegex(@"^\s*function\s+(?<name>[A-Za-z_][A-Za-z0-9_\-]*)\s*(?:\{|$)", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex PowerShellFunctionRegex();

    private sealed record ExtractedSymbol(
        CodeGraphNodeType NodeType,
        string Label,
        string SecondaryLabel,
        int StartLine,
        int EndLine,
        string Metadata);

    private sealed class FileExtraction
    {
        public HashSet<string> Namespaces { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ExternalReferences { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<ExtractedSymbol> Symbols { get; } = [];
    }

    private sealed record FileScanState(CodeGraphFile File, CodeGraphNode FileNode, string Content, HashSet<Guid> DeclaredNodeIds);
}
