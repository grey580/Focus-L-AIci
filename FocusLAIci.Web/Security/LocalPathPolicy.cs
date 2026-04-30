using Microsoft.Extensions.DependencyInjection;

namespace FocusLAIci.Web.Security;

public sealed class LocalPathPolicy
{
    private static readonly HashSet<string> AllowedDatabaseExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".db",
        ".db3",
        ".sqlite",
        ".sqlite3"
    };

    private readonly string[] _approvedProjectRoots;
    private readonly string[] _approvedDatabaseRoots;

    [ActivatorUtilitiesConstructor]
    public LocalPathPolicy(IHostEnvironment environment)
        : this(
            BuildApprovedProjectRoots(
                environment.ContentRootPath,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
            BuildApprovedDatabaseRoots(
                environment.ContentRootPath,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)))
    {
    }

    public LocalPathPolicy(IEnumerable<string> approvedProjectRoots, IEnumerable<string> approvedDatabaseRoots)
    {
        _approvedProjectRoots = NormalizeRoots(approvedProjectRoots);
        _approvedDatabaseRoots = NormalizeRoots(approvedDatabaseRoots);
    }

    public IReadOnlyCollection<string> ApprovedProjectRoots => _approvedProjectRoots;

    public IReadOnlyCollection<string> ApprovedDatabaseRoots => _approvedDatabaseRoots;

    public string EnsureApprovedProjectRoot(string candidatePath)
    {
        var normalized = NormalizeCandidatePath(candidatePath);
        if (!IsWithinApprovedRoots(normalized, _approvedProjectRoots))
        {
            throw new InvalidOperationException(
                $"Repository root paths must stay within approved local work roots: {FormatRoots(_approvedProjectRoots)}.");
        }

        return normalized;
    }

    public string EnsureApprovedDatabasePath(string candidatePath)
    {
        var normalized = NormalizeCandidatePath(candidatePath);
        var extension = Path.GetExtension(normalized);
        if (!AllowedDatabaseExtensions.Contains(extension))
        {
            throw new InvalidOperationException("Database targets must use a SQLite file extension such as .db, .db3, .sqlite, or .sqlite3.");
        }

        if (!IsWithinApprovedRoots(normalized, _approvedDatabaseRoots))
        {
            throw new InvalidOperationException(
                $"Database targets must stay within approved local data roots: {FormatRoots(_approvedDatabaseRoots)}.");
        }

        return normalized;
    }

    private static string[] BuildApprovedProjectRoots(string contentRootPath, string? userProfilePath)
    {
        var roots = new List<string> { contentRootPath };
        AddMeaningfulAncestorRoots(roots, contentRootPath, maxLevels: 2);

        if (!string.IsNullOrWhiteSpace(userProfilePath))
        {
            roots.Add(Path.Combine(userProfilePath, "source"));
        }

        return NormalizeRoots(roots);
    }

    private static string[] BuildApprovedDatabaseRoots(string contentRootPath, string? userProfilePath, string? localApplicationDataPath)
    {
        var roots = new List<string> { contentRootPath };
        AddMeaningfulAncestorRoots(roots, contentRootPath, maxLevels: 2);

        if (!string.IsNullOrWhiteSpace(localApplicationDataPath))
        {
            roots.Add(Path.Combine(localApplicationDataPath, "FocusLAIci"));
        }

        if (!string.IsNullOrWhiteSpace(userProfilePath))
        {
            roots.Add(Path.Combine(userProfilePath, "source"));
        }

        return NormalizeRoots(roots);
    }

    private static void AddMeaningfulAncestorRoots(ICollection<string> roots, string path, int maxLevels)
    {
        var current = NormalizeCandidatePath(path);
        for (var level = 0; level < maxLevels; level++)
        {
            var parent = Directory.GetParent(current);
            if (parent is null)
            {
                break;
            }

            var normalizedParent = NormalizeCandidatePath(parent.FullName);
            if (string.Equals(normalizedParent, Path.GetPathRoot(normalizedParent), StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            roots.Add(normalizedParent);
            current = normalizedParent;
        }
    }

    private static string[] NormalizeRoots(IEnumerable<string> roots)
    {
        return roots
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(NormalizeResolvedPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeCandidatePath(string path)
    {
        var fullPath = Path.GetFullPath(path.Trim());
        var pathRoot = Path.GetPathRoot(fullPath);
        if (!string.IsNullOrEmpty(pathRoot) &&
            string.Equals(fullPath, pathRoot, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string NormalizeResolvedPath(string path)
    {
        return NormalizeCandidatePath(ResolveReparsePoints(path));
    }

    private static string ResolveReparsePoints(string path)
    {
        var normalized = NormalizeCandidatePath(path);
        var pathRoot = Path.GetPathRoot(normalized);
        if (string.IsNullOrEmpty(pathRoot) ||
            string.Equals(normalized, pathRoot, StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        var current = NormalizeCandidatePath(pathRoot);
        var relativeSegments = normalized[pathRoot.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in relativeSegments)
        {
            var next = NormalizeCandidatePath(Path.Combine(current, segment));
            current = TryResolveReparsePoint(next, out var resolvedPath)
                ? resolvedPath
                : next;
        }

        return current;
    }

    private static bool TryResolveReparsePoint(string path, out string resolvedPath)
    {
        resolvedPath = string.Empty;

        FileSystemInfo? fileSystemInfo = null;
        if (Directory.Exists(path))
        {
            fileSystemInfo = new DirectoryInfo(path);
        }
        else if (File.Exists(path))
        {
            fileSystemInfo = new FileInfo(path);
        }

        if (fileSystemInfo is null)
        {
            return false;
        }

        try
        {
            if ((fileSystemInfo.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                return false;
            }

            var target = fileSystemInfo.ResolveLinkTarget(returnFinalTarget: true);
            if (target is null)
            {
                return false;
            }

            resolvedPath = NormalizeCandidatePath(target.FullName);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool IsWithinApprovedRoots(string candidatePath, IEnumerable<string> approvedRoots)
    {
        var normalizedCandidatePath = NormalizeResolvedPath(candidatePath);
        foreach (var root in approvedRoots)
        {
            if (string.Equals(normalizedCandidatePath, root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var rootWithSeparator = root + Path.DirectorySeparatorChar;
            if (normalizedCandidatePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string FormatRoots(IEnumerable<string> roots)
    {
        return string.Join(", ", roots);
    }
}
