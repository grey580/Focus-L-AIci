namespace FocusLAIci.Web.Models;

/// <summary>
/// Shared low-level text splitting helpers. Several services (PackCriticEngine,
/// PalaceService, ContextService) each maintained their own copy of the same
/// punctuation-splitting character set and/or the same "low signal token" list
/// used to decide whether a match is specific enough to count as grounding.
/// This class centralizes the parts of that logic that were byte-for-byte
/// identical across call sites, so future changes to the delimiter set or the
/// shared stopword list only need to happen in one place. Callers still apply
/// their own length filtering, ordering, and distinct/hashing behavior on top,
/// since those differ intentionally by use case.
/// </summary>
public static class TextTokenizationUtility
{
    private static readonly char[] WordDelimiters =
        [' ', '\r', '\n', '\t', ',', '.', ':', ';', '/', '\\', '(', ')', '[', ']', '{', '}', '-', '_', '"', '\'', '*', '`', '#'];

    /// <summary>
    /// Lowercases <paramref name="value"/> and splits it on common punctuation and
    /// whitespace delimiters, dropping empty entries. Does not filter by token
    /// length, apply stopwords, or de-duplicate - callers are expected to layer
    /// that behavior on top to match their specific use case.
    /// </summary>
    public static IEnumerable<string> SplitIntoRawTokens(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value
            .ToLowerInvariant()
            .Split(WordDelimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Common, low-information tokens (generic verbs, filler words, and
    /// "powershell/windows/pc"-style words that appear in almost every automation
    /// request) that should not by themselves count as evidence that a
    /// retrieved record is specifically grounded in the user's question. Shared
    /// by PackCriticEngine's grounding check and ContextService's retrieval
    /// specificity scoring so the two heuristics can't silently drift apart.
    /// </summary>
    public static readonly HashSet<string> LowSignalGroundingTokens =
    [
        "build", "check", "checks", "command", "commands", "computer", "computers", "create", "find", "help", "line",
        "list", "local", "machine", "machines", "make", "need", "pc", "pcs", "please", "powershell", "run", "script",
        "show", "tell", "use", "using", "windows", "will", "with"
    ];
}
