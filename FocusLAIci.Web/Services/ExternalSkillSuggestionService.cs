using System.Text.RegularExpressions;
using FocusLAIci.Web.Data;
using FocusLAIci.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace FocusLAIci.Web.Services;

public sealed partial class ExternalSkillSuggestionService
{
    private static readonly HashSet<string> ExchangeOnlineTokens =
    [
        "exchange", "online", "powershell", "exo", "mailbox", "mailboxes", "getexomailbox", "recipienttypedetails"
    ];

    private static readonly HashSet<string> DirectoryAdminTokens =
    [
        "ad", "ldap", "entra", "graph", "mail", "email", "exchange", "mailbox", "mailboxes", "office365", "o365", "m365"
    ];

    private static readonly HashSet<string> WebUiTokens =
    [
        "website", "web", "ui", "layout", "css", "frontend", "react", "tailwind"
    ];

    private static readonly HashSet<string> AzureTokens =
    [
        "azure", "cloud", "tenant", "subscription", "entra", "appinsights", "deployment"
    ];

    private static readonly HashSet<string> DesktopTokens =
    [
        "winforms", "windowsforms", "desktop", "forms"
    ];

    private readonly FocusMemoryContext _dbContext;
    private readonly HttpClient _httpClient;

    public ExternalSkillSuggestionService(FocusMemoryContext dbContext, HttpClient httpClient)
    {
        _dbContext = dbContext;
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyCollection<SkillSourceCardViewModel>> GetSourcesAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.ExternalSkillSources
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new SkillSourceCardViewModel
            {
                Id = x.Id,
                Name = x.Name,
                CatalogUrl = x.CatalogUrl,
                Description = x.Description,
                IsEnabled = x.IsEnabled,
                LastCheckedUtc = x.LastCheckedUtc,
                LastCheckStatus = x.LastCheckStatus
            })
            .ToArrayAsync(cancellationToken);
    }

    public async Task AddSourceAsync(SkillSourceEditorInput input, CancellationToken cancellationToken)
    {
        var normalizedUrl = NormalizeUrl(input.CatalogUrl);
        var existing = await _dbContext.ExternalSkillSources
            .FirstOrDefaultAsync(x => x.CatalogUrl == normalizedUrl || x.Name == input.Name.Trim(), cancellationToken);
        if (existing is not null)
        {
            throw new InvalidOperationException("A skill source with that name or catalog URL already exists.");
        }

        _dbContext.ExternalSkillSources.Add(new ExternalSkillSource
        {
            Name = input.Name.Trim(),
            CatalogUrl = normalizedUrl,
            Description = input.Description.Trim(),
            IsEnabled = input.IsEnabled,
            UpdatedUtc = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveSourceAsync(Guid id, CancellationToken cancellationToken)
    {
        var source = await _dbContext.ExternalSkillSources.FindAsync([id], cancellationToken);
        if (source is null)
        {
            return;
        }

        _dbContext.ExternalSkillSources.Remove(source);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ExternalSkillAlertViewModel> BuildAlertAsync(ContextPackViewModel pack, CancellationToken cancellationToken)
    {
        var enabledSourceCount = await _dbContext.ExternalSkillSources.CountAsync(x => x.IsEnabled, cancellationToken);
        var suggestions = await SuggestSkillsAsync(pack.Question, pack.SearchTokens, 6, cancellationToken);
        var opportunityLabel = GetOpportunityLabel(pack.Question, pack.SearchTokens);

        if (suggestions.Count > 0)
        {
            return new ExternalSkillAlertViewModel
            {
                Message = suggestions.Count == 1
                    ? "This pack may benefit from 1 external skill you can add."
                    : $"This pack may benefit from {suggestions.Count} external skills you can add.",
                Suggestions = suggestions
            };
        }

        if (string.IsNullOrWhiteSpace(opportunityLabel))
        {
            return new ExternalSkillAlertViewModel();
        }

        return new ExternalSkillAlertViewModel
        {
            Message = enabledSourceCount == 0
                ? $"This pack looks like it could benefit from an external {opportunityLabel} skill. Add one or more skill websites in Admin > Settings to enable suggestions."
                : $"This pack looks like it could benefit from an external {opportunityLabel} skill, but none matched from the configured sources yet.",
            Suggestions = Array.Empty<ExternalSkillSuggestionViewModel>()
        };
    }

    public async Task<IReadOnlyCollection<ExternalSkillSuggestionViewModel>> SuggestSkillsAsync(
        string question,
        IReadOnlyCollection<string> searchTokens,
        int limit,
        CancellationToken cancellationToken)
    {
        var sources = await _dbContext.ExternalSkillSources
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        if (sources.Count == 0)
        {
            return Array.Empty<ExternalSkillSuggestionViewModel>();
        }

        var normalizedQuestion = question.Trim();
        var normalizedTokens = Tokenize(string.Join(' ', searchTokens.Prepend(normalizedQuestion)));
        var suggestions = new List<ExternalSkillSuggestionViewModel>();

        foreach (var source in sources)
        {
            try
            {
                var catalogText = await _httpClient.GetStringAsync(source.CatalogUrl, cancellationToken);
                var candidates = ExtractCandidateLinks(source, catalogText);
                foreach (var candidate in candidates)
                {
                    var candidateTokens = Tokenize($"{candidate.Name} {candidate.Summary}");
                    var overlap = normalizedTokens.Intersect(candidateTokens, StringComparer.OrdinalIgnoreCase).ToArray();
                    if (overlap.Length == 0)
                    {
                        continue;
                    }

                    suggestions.Add(new ExternalSkillSuggestionViewModel
                    {
                        Name = candidate.Name,
                        Summary = candidate.Summary,
                        SourceName = source.Name,
                        SkillUrl = candidate.Url,
                        MatchReason = $"Overlaps on {string.Join(", ", overlap.Take(4))}.",
                        Score = overlap.Length
                    });
                }

                source.LastCheckedUtc = DateTime.UtcNow;
                source.LastCheckStatus = candidates.Count == 0
                    ? "No skill links found."
                    : $"Found {candidates.Count} candidate skill links.";
            }
            catch (Exception ex)
            {
                source.LastCheckedUtc = DateTime.UtcNow;
                source.LastCheckStatus = TrimStatus(ex.Message);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return suggestions
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Name)
            .Take(Math.Clamp(limit, 1, 12))
            .ToArray();
    }

    public async Task<SkillEntry> ImportSuggestionAsync(string skillUrl, string sourceName, CancellationToken cancellationToken)
    {
        var markdown = await _httpClient.GetStringAsync(ResolveRawContentUrl(skillUrl), cancellationToken);
        var parsed = ParseSkillDocument(markdown, skillUrl, sourceName);
        var slug = SlugUtility.CreateSlug(parsed.Name);
        var existing = await _dbContext.Skills.FirstOrDefaultAsync(x => x.Slug == slug, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var skill = new SkillEntry
        {
            Name = parsed.Name,
            Slug = slug,
            Summary = parsed.Summary,
            Category = SkillCategory.Task,
            WhenToUse = parsed.WhenToUse,
            Flow = parsed.Flow,
            ExamplesText = parsed.ExamplesText,
            TriggerHintsText = parsed.TriggerHintsText,
            IsPinned = true,
            UpdatedUtc = DateTime.UtcNow
        };

        _dbContext.Skills.Add(skill);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return skill;
    }

    private static IReadOnlyCollection<ExternalSkillCandidate> ExtractCandidateLinks(ExternalSkillSource source, string catalogText)
    {
        if (string.IsNullOrWhiteSpace(catalogText))
        {
            return Array.Empty<ExternalSkillCandidate>();
        }

        var results = new List<ExternalSkillCandidate>();
        foreach (Match match in AnchorRegex().Matches(catalogText))
        {
            var href = match.Groups["href"].Value.Trim();
            var label = SanitizeCandidateLabel(match.Groups["label"].Value.Trim(), href);
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            var absoluteUrl = ToAbsoluteUrl(source.CatalogUrl, href);
            if (!absoluteUrl.Contains("skill", StringComparison.OrdinalIgnoreCase)
                && !absoluteUrl.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(label) ? GuessNameFromUrl(absoluteUrl) : label;
            results.Add(new ExternalSkillCandidate(name, string.Empty, absoluteUrl));
        }

        foreach (Match match in MarkdownLinkRegex().Matches(catalogText))
        {
            var label = SanitizeCandidateLabel(match.Groups["label"].Value.Trim(), match.Groups["href"].Value.Trim());
            var href = match.Groups["href"].Value.Trim();
            var absoluteUrl = ToAbsoluteUrl(source.CatalogUrl, href);
            if (!absoluteUrl.Contains("skill", StringComparison.OrdinalIgnoreCase)
                && !absoluteUrl.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            results.Add(new ExternalSkillCandidate(
                string.IsNullOrWhiteSpace(label) ? GuessNameFromUrl(absoluteUrl) : label,
                string.Empty,
                absoluteUrl));
        }

        return results
            .GroupBy(x => x.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static ParsedSkillDocument ParseSkillDocument(string markdown, string skillUrl, string sourceName)
    {
        var frontMatter = ExtractFrontMatter(markdown);
        var title = frontMatter.TryGetValue("name", out var frontMatterName) && !string.IsNullOrWhiteSpace(frontMatterName)
            ? frontMatterName
            : ExtractFirstHeading(markdown) ?? GuessNameFromUrl(skillUrl);
        var summary = frontMatter.TryGetValue("description", out var description) && !string.IsNullOrWhiteSpace(description)
            ? description
            : ExtractFirstParagraph(markdown) ?? $"Imported from {sourceName}.";

        return new ParsedSkillDocument(
            title.Trim(),
            TrimText(summary, 500),
            ExtractSection(markdown, "When to Use") ?? summary,
            ExtractSection(markdown, "Workflow") ?? ExtractSection(markdown, "Workflow Overview") ?? "Review the source skill and follow its documented workflow.",
            ExtractSection(markdown, "Examples") ?? $"Imported from {sourceName}: {skillUrl}",
            string.Join(", ", Tokenize($"{title} {summary}").Take(10)));
    }

    private static Dictionary<string, string> ExtractFrontMatter(string markdown)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var match = FrontMatterRegex().Match(markdown);
        if (!match.Success)
        {
            return result;
        }

        foreach (var line in match.Groups["body"].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            result[key] = value;
        }

        return result;
    }

    private static string? ExtractFirstHeading(string markdown)
    {
        foreach (var line in markdown.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                return line.TrimStart('#', ' ').Trim();
            }
        }

        return null;
    }

    private static string? ExtractFirstParagraph(string markdown)
    {
        return markdown.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(line => !line.StartsWith("#", StringComparison.Ordinal) && !line.StartsWith("---", StringComparison.Ordinal));
    }

    private static string? ExtractSection(string markdown, string heading)
    {
        var match = Regex.Match(
            markdown,
            $@"(?ms)^##\s+{Regex.Escape(heading)}.*?$\\n(?<body>.*?)(^##\s+|\z)",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["body"].Value.Trim() : null;
    }

    private static string ResolveRawContentUrl(string url)
    {
        if (url.Contains("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        if (url.Contains("github.com", StringComparison.OrdinalIgnoreCase)
            && url.Contains("/blob/", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath.Trim('/');
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 5)
            {
                return $"https://raw.githubusercontent.com/{parts[0]}/{parts[1]}/{parts[3]}/{string.Join('/', parts.Skip(4))}";
            }
        }

        return url;
    }

    private static string NormalizeUrl(string url)
        => url.Trim().TrimEnd('/');

    private static string ToAbsoluteUrl(string catalogUrl, string href)
    {
        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute))
        {
            return absolute.ToString();
        }

        return new Uri(new Uri(catalogUrl.EndsWith('/') ? catalogUrl : catalogUrl + "/"), href).ToString();
    }

    private static string GuessNameFromUrl(string url)
    {
        var uri = new Uri(url);
        var segments = uri.Segments
            .Select(segment => segment.Trim('/', ' '))
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();
        if (segments.Length == 0)
        {
            return "Imported skill";
        }

        var segment = segments[^1];
        if (segment.Equals("SKILL.md", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("README.md", StringComparison.OrdinalIgnoreCase))
        {
            segment = segments.Length >= 2 ? segments[^2] : segment;
        }

        segment = segment.Replace(".md", string.Empty, StringComparison.OrdinalIgnoreCase);
        return segment.Replace('-', ' ').Replace('_', ' ').Trim();
    }

    private static string HtmlDecode(string value)
        => System.Net.WebUtility.HtmlDecode(value);

    private static string SanitizeCandidateLabel(string value, string href)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return GuessNameFromUrl(ToAbsoluteOrOriginalUrl(href));
        }

        var decoded = HtmlDecode(value);
        if (decoded.Contains('<', StringComparison.Ordinal) || decoded.Contains('>', StringComparison.Ordinal))
        {
            var stripped = HtmlTagRegex().Replace(decoded, " ");
            stripped = Regex.Replace(stripped, "\\s+", " ").Trim();
            if (string.IsNullOrWhiteSpace(stripped) || LooksLikeCatalogRowNoise(stripped))
            {
                return GuessNameFromUrl(ToAbsoluteOrOriginalUrl(href));
            }

            return stripped;
        }

        return decoded.Trim();
    }

    private static bool LooksLikeCatalogRowNoise(string value)
    {
        var normalized = value.Trim();
        return normalized.Length > 80
               || Regex.IsMatch(normalized, @"\b\d+(\.\d+)?[KMB]?\b", RegexOptions.IgnoreCase)
               || normalized.Contains("lg:col-span", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("text-", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("font-", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToAbsoluteOrOriginalUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var absolute) ? absolute.ToString() : value;

    private static string TrimStatus(string value)
        => TrimText(value, 260);

    private static string TrimText(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength].TrimEnd();

    private static string[] Tokenize(string value)
    {
        return WordRegex()
            .Matches(value.ToLowerInvariant())
            .Select(static match => match.Value)
            .Where(token => token.Length > 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetOpportunityLabel(string question, IReadOnlyCollection<string> searchTokens)
    {
        var tokens = Tokenize(string.Join(' ', searchTokens.Prepend(question ?? string.Empty)));

        if (CountMatches(tokens, ExchangeOnlineTokens) >= 3)
        {
            return "Exchange Online PowerShell";
        }

        if (CountMatches(tokens, AzureTokens) >= 2)
        {
            return "Azure";
        }

        if (CountMatches(tokens, WebUiTokens) >= 2)
        {
            return "web UI";
        }

        if (CountMatches(tokens, DesktopTokens) >= 2)
        {
            return "desktop app";
        }

        if (CountMatches(tokens, DirectoryAdminTokens) >= 3)
        {
            return "directory admin";
        }

        return string.Empty;
    }

    private static int CountMatches(IEnumerable<string> tokens, IReadOnlySet<string> candidates)
        => tokens.Count(token => candidates.Contains(token));

    [GeneratedRegex("---\\s*(?<body>.*?)---", RegexOptions.Singleline)]
    private static partial Regex FrontMatterRegex();

    [GeneratedRegex("<a[^>]*href=[\"'](?<href>[^\"']+)[\"'][^>]*>(?<label>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnchorRegex();

    [GeneratedRegex("\\[(?<label>[^\\]]+)\\]\\((?<href>[^\\)]+)\\)", RegexOptions.IgnoreCase)]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("[a-z0-9]+", RegexOptions.IgnoreCase)]
    private static partial Regex WordRegex();

    private sealed record ExternalSkillCandidate(string Name, string Summary, string Url);

    private sealed record ParsedSkillDocument(
        string Name,
        string Summary,
        string WhenToUse,
        string Flow,
        string ExamplesText,
        string TriggerHintsText);
}
