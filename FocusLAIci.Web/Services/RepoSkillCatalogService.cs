using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FocusLAIci.Web.Models;
using Microsoft.Extensions.Hosting;

namespace FocusLAIci.Web.Services;

public sealed class RepoSkillCatalogService
{
    private static readonly Regex HeadingRegex = new(@"^#{1,3}\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex NumberedListRegex = new(@"^\d+\.\s+", RegexOptions.Compiled);
    private static readonly Regex TriggerPhraseRegex = new("\"([^\"]{3,120})\"", RegexOptions.Compiled);
    private readonly IHostEnvironment _hostEnvironment;

    public RepoSkillCatalogService(IHostEnvironment hostEnvironment)
    {
        _hostEnvironment = hostEnvironment;
    }

    public IReadOnlyCollection<RepoSkillDocument> GetAllSkills()
    {
        var bySlug = new Dictionary<string, RepoSkillDocument>(StringComparer.OrdinalIgnoreCase);
        foreach (var skillFile in EnumerateSkillFiles())
        {
            var document = ParseSkill(skillFile);
            if (document is null)
            {
                continue;
            }

            bySlug.TryAdd(document.Skill.Slug, document);
        }

        return bySlug.Values
            .OrderBy(x => x.Skill.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public RepoSkillDocument? GetSkill(string slug)
        => GetAllSkills().FirstOrDefault(x => string.Equals(x.Skill.Slug, slug, StringComparison.OrdinalIgnoreCase));

    private IEnumerable<string> EnumerateSkillFiles()
    {
        foreach (var skillRoot in ResolveSkillRoots())
        {
            foreach (var skillFile in Directory.EnumerateFiles(skillRoot, "SKILL.md", SearchOption.AllDirectories))
            {
                yield return skillFile;
            }
        }
    }

    private IEnumerable<string> ResolveSkillRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = new DirectoryInfo(_hostEnvironment.ContentRootPath);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, ".agents", "skills");
            if (Directory.Exists(candidate) && seen.Add(candidate))
            {
                yield return candidate;
            }

            current = current.Parent;
        }
    }

    private static RepoSkillDocument? ParseSkill(string skillFilePath)
    {
        string content;
        try
        {
            content = File.ReadAllText(skillFilePath);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        var (frontMatter, body) = SplitFrontMatter(content);
        var heading = ExtractHeading(body);
        var machineName = GetFrontMatterValue(frontMatter, "name");
        var rawName = !string.IsNullOrWhiteSpace(heading)
            ? heading
            : !string.IsNullOrWhiteSpace(machineName)
                ? HumanizeName(machineName)
                : HumanizeName(Path.GetFileName(Path.GetDirectoryName(skillFilePath) ?? string.Empty));
        var summary = NormalizeText(GetFrontMatterValue(frontMatter, "description"));
        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = ExtractFirstParagraph(body);
        }

        if (string.IsNullOrWhiteSpace(rawName) || string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        var slug = SlugUtility.CreateSlug(machineName ?? rawName);
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var whenToUse = ExtractSection(body, "when to use", "use this skill when", "scope of application", "prerequisites");
        if (string.IsNullOrWhiteSpace(whenToUse))
        {
            whenToUse = summary;
        }

        var flow = ExtractSection(body, "workflow overview", "workflow", "quick start", "commands", "process");
        if (string.IsNullOrWhiteSpace(flow))
        {
            flow = whenToUse;
        }

        var examples = ExtractSection(body, "examples", "example prompts", "quick start", "commands");
        if (string.IsNullOrWhiteSpace(examples))
        {
            examples = string.Join(Environment.NewLine, ExtractQuotedPhrases(summary).Take(4));
        }

        var explicitTriggerHints = NormalizeText(GetFrontMatterValue(frontMatter, "trigger_hints"));
        var triggerHintsText = !string.IsNullOrWhiteSpace(explicitTriggerHints)
            ? Truncate(explicitTriggerHints, 500)
            : string.Join(", ", BuildTriggerHints(rawName, summary, whenToUse));
        var lastUpdatedUtc = File.GetLastWriteTimeUtc(skillFilePath);
        var category = ParseCategory(GetFrontMatterValue(frontMatter, "category"));
        var isPinned = ParseBoolean(GetFrontMatterValue(frontMatter, "pinned"), defaultValue: false);
        var wingSlug = NormalizeText(GetFrontMatterValue(frontMatter, "wing"));
        var skill = new SkillEntry
        {
            Id = CreateDeterministicGuid($"repo-skill:{slug}"),
            Name = rawName,
            Slug = slug,
            Summary = Truncate(summary, 500),
            Category = category ?? InferCategory(rawName, summary, whenToUse, flow, skillFilePath),
            WhenToUse = whenToUse,
            Flow = flow,
            ExamplesText = examples,
            TriggerHintsText = Truncate(triggerHintsText, 500),
            IsPinned = isPinned,
            UseCount = 0,
            LastReviewedUtc = lastUpdatedUtc,
            ReviewAfterUtc = lastUpdatedUtc.AddDays(SkillRecommendationEngine.DefaultReviewWindowDays),
            CreatedUtc = lastUpdatedUtc,
            UpdatedUtc = lastUpdatedUtc
        };

        return new RepoSkillDocument(skill, skillFilePath, "Project skill", wingSlug);
    }

    private static (Dictionary<string, string> FrontMatter, string Body) SplitFrontMatter(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!normalized.StartsWith("---\n", StringComparison.Ordinal))
        {
            return (new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), normalized);
        }

        var endIndex = normalized.IndexOf("\n---\n", 4, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            return (new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), normalized);
        }

        var frontMatterText = normalized[4..endIndex];
        var body = normalized[(endIndex + 5)..];
        var frontMatter = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in frontMatterText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim().Trim('\'', '"');
            if (!string.IsNullOrWhiteSpace(key))
            {
                frontMatter[key] = value;
            }
        }

        return (frontMatter, body);
    }

    private static string GetFrontMatterValue(IReadOnlyDictionary<string, string> frontMatter, string key)
        => frontMatter.TryGetValue(key, out var value) ? value.Trim() : string.Empty;

    private static SkillCategory? ParseCategory(string value)
        => Enum.TryParse<SkillCategory>(value, ignoreCase: true, out var category) ? category : null;

    private static bool ParseBoolean(string value, bool defaultValue)
        => bool.TryParse(value, out var parsed) ? parsed : defaultValue;

    private static string ExtractHeading(string body)
    {
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.Trim();
            var match = HeadingRegex.Match(line);
            if (match.Success)
            {
                return NormalizeText(match.Groups[1].Value);
            }
        }

        return string.Empty;
    }

    private static string ExtractFirstParagraph(string body)
    {
        var builder = new StringBuilder();
        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                if (builder.Length > 0)
                {
                    break;
                }

                continue;
            }

            if (line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(line);
        }

        return Truncate(NormalizeText(builder.ToString()), 500);
    }

    private static string ExtractSection(string body, params string[] sectionNames)
    {
        if (sectionNames.Length == 0)
        {
            return string.Empty;
        }

        var lines = body.Split('\n');
        var builder = new List<string>();
        var capture = false;
        var inCodeBlock = false;

        foreach (var rawLine in lines)
        {
            var trimmed = rawLine.TrimEnd();
            var heading = HeadingRegex.Match(trimmed.Trim());
            if (heading.Success)
            {
                var headingText = NormalizeText(heading.Groups[1].Value).ToLowerInvariant();
                if (capture)
                {
                    break;
                }

                capture = sectionNames.Any(sectionName => headingText.Contains(sectionName, StringComparison.OrdinalIgnoreCase));
                continue;
            }

            if (!capture)
            {
                continue;
            }

            if (trimmed.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inCodeBlock = !inCodeBlock;
                continue;
            }

            var normalized = NormalizeSectionLine(trimmed, inCodeBlock);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                builder.Add(normalized);
            }
        }

        if (builder.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, builder.Take(12));
    }

    private static string NormalizeSectionLine(string line, bool inCodeBlock)
    {
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        if (trimmed.StartsWith("|", StringComparison.Ordinal) || trimmed.StartsWith("![", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (inCodeBlock)
        {
            return trimmed;
        }

        trimmed = trimmed.TrimStart('-', '*', '>', ' ');
        trimmed = NumberedListRegex.Replace(trimmed, string.Empty);
        return NormalizeText(trimmed);
    }

    private static IReadOnlyCollection<string> BuildTriggerHints(string name, string summary, string whenToUse)
    {
        var hints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var phrase in ExtractQuotedPhrases(summary))
        {
            hints.Add(phrase);
        }

        foreach (var token in TokenizeHints(name).Concat(TokenizeHints(summary)).Concat(TokenizeHints(whenToUse)))
        {
            hints.Add(token);
        }

        return hints
            .Where(x => x.Length >= 3)
            .Take(8)
            .ToArray();
    }

    private static IEnumerable<string> ExtractQuotedPhrases(string text)
        => TriggerPhraseRegex.Matches(text).Select(x => NormalizeText(x.Groups[1].Value));

    private static IEnumerable<string> TokenizeHints(string text)
    {
        return text
            .Split([' ', '\r', '\n', '\t', ',', '.', ':', ';', '/', '\\', '(', ')', '[', ']', '{', '}', '-', '_'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 3)
            .Select(NormalizeText);
    }

    private static SkillCategory InferCategory(string name, string summary, string whenToUse, string flow, string skillFilePath)
    {
        var corpus = $"{name} {summary} {whenToUse} {flow} {skillFilePath}".ToLowerInvariant();
        if (corpus.Contains("security", StringComparison.Ordinal) ||
            corpus.Contains("owasp", StringComparison.Ordinal) ||
            corpus.Contains("secure", StringComparison.Ordinal))
        {
            return SkillCategory.System;
        }

        if (corpus.Contains("design", StringComparison.Ordinal) ||
            corpus.Contains("ux", StringComparison.Ordinal) ||
            corpus.Contains("ui", StringComparison.Ordinal) ||
            corpus.Contains("website", StringComparison.Ordinal) ||
            corpus.Contains("webapp", StringComparison.Ordinal))
        {
            return SkillCategory.Product;
        }

        if (corpus.Contains("cli", StringComparison.Ordinal) ||
            corpus.Contains("playwright", StringComparison.Ordinal) ||
            corpus.Contains("sql", StringComparison.Ordinal) ||
            corpus.Contains("github", StringComparison.Ordinal) ||
            corpus.Contains("git", StringComparison.Ordinal) ||
            corpus.Contains("devtools", StringComparison.Ordinal) ||
            corpus.Contains("test", StringComparison.Ordinal) ||
            corpus.Contains("refactor", StringComparison.Ordinal) ||
            corpus.Contains("documentation", StringComparison.Ordinal))
        {
            return SkillCategory.Tooling;
        }

        return SkillCategory.Task;
    }

    private static Guid CreateDeterministicGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, guidBytes.Length);
        return new Guid(guidBytes);
    }

    private static string HumanizeName(string value)
    {
        var normalized = NormalizeText(value.Replace('-', ' ').Replace('_', ' '));
        return string.IsNullOrWhiteSpace(normalized)
            ? string.Empty
            : string.Join(' ', normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
    }

    private static string NormalizeText(string value)
        => string.Join(' ', value.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Trim();

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength].TrimEnd();
}

public sealed record RepoSkillDocument(SkillEntry Skill, string SourcePath, string SourceLabel, string WingSlug);
