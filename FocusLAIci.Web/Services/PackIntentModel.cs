namespace FocusLAIci.Web.Services;

public interface IPackIntentModel
{
    string ModelId { get; }

    PackIntentPrediction Predict(string? question);
}

public sealed record PackIntentPrediction(
    decimal ExternalOperationsScore,
    decimal DirectoryAdminScore,
    decimal CodeIntentScore,
    decimal GenericAutomationScore,
    decimal RepositoryArchitectureScore,
    string ModelId)
{
    public bool IsExternalOperationsQuery => ExternalOperationsScore >= 0.54m;

    public bool IsDirectoryAdminQuery => DirectoryAdminScore >= 0.56m;

    public bool HasExplicitCodeIntent => CodeIntentScore >= 0.56m;

    public bool IsGenericAutomationQuery => GenericAutomationScore >= 0.55m;

    public bool IsRepositoryArchitectureQuery => RepositoryArchitectureScore >= 0.56m;
}

public sealed class TinyLocalPackIntentModel : IPackIntentModel
{
    private static readonly char[] TokenSeparators = [' ', '\r', '\n', '\t', ',', '.', ':', ';', '/', '\\', '(', ')', '[', ']', '{', '}', '-', '_', '"', '\''];

    private static readonly WeightedFeature[] ExternalOperationsFeatures =
    [
        new("powershell", 1.9m),
        new("script", 0.9m),
        new("active directory", 2.4m, true),
        new("ldap", 1.9m),
        new("ad", 1.1m),
        new("entra", 1.0m),
        new("graph", 0.9m),
        new("email", 0.8m),
        new("emails", 0.8m),
        new("mail", 0.7m),
        new("mailbox", 1.0m),
        new("mailboxes", 1.0m),
        new("office365", 1.0m),
        new("office 365", 1.0m, true),
        new("o365", 1.0m),
        new("m365", 1.0m),
        new("oauth", 0.9m),
        new("exchange", 0.8m),
        new("recipient type", 0.9m, true),
        new("recipient types", 0.9m, true),
        new("integration", 0.7m),
        new("domain", 0.8m),
        new("dns", 0.9m),
        new("proxy addresses", 1.0m, true),
        new("proxyaddresses", 1.0m),
        new("user principal name", 1.0m, true),
        new("upn", 0.8m),
        new("userprincipalname", 0.9m),
        new("mail nickname", 0.9m, true),
        new("mailnickname", 0.9m),
        new("export", 0.8m),
        new("csv", 0.8m),
        new("disabled", 0.6m),
        new("report", 0.5m),
        new("uninstall", 1.2m),
        new("callback", 1.0m),
        new("endpoint", 0.8m),
        new("platform", 0.5m),
        new("user", 0.35m),
        new("users", 0.35m),
        new("repo", -1.2m),
        new("project", -1.1m),
        new("code", -1.1m),
        new("class", -0.9m),
        new("method", -0.9m)
    ];

    private static readonly WeightedFeature[] DirectoryAdminFeatures =
    [
        new("active directory", 2.8m, true),
        new("ldap", 2.1m),
        new("entra", 1.9m),
        new("graph", 1.8m),
        new("email", 1.9m),
        new("emails", 1.9m),
        new("mail", 1.5m),
        new("mailbox", 2.1m),
        new("mailboxes", 2.1m),
        new("office365", 1.7m),
        new("office 365", 1.7m, true),
        new("o365", 1.5m),
        new("m365", 1.5m),
        new("oauth", 1.5m),
        new("exchange", 1.4m),
        new("recipient type", 1.2m, true),
        new("recipient types", 1.2m, true),
        new("integration", 1.0m),
        new("domain", 1.2m),
        new("dns", 1.4m),
        new("forwarder", 1.4m),
        new("forest", 1.4m),
        new("proxy addresses", 2.2m, true),
        new("proxyaddresses", 2.2m),
        new("user principal name", 1.9m, true),
        new("attribute", 1.3m),
        new("attributes", 1.3m),
        new("upn", 1.9m),
        new("userprincipalname", 2.0m),
        new("mail nickname", 1.7m, true),
        new("mailnickname", 1.6m),
        new("repo", -1.4m),
        new("project", -1.2m),
        new("code", -1.2m),
        new("class", -0.9m),
        new("method", -0.9m)
    ];

    private static readonly WeightedFeature[] CodeIntentFeatures =
    [
        new("code", 2.2m),
        new("repo", 2.0m),
        new("repository", 1.8m),
        new("codebase", 1.9m),
        new("architecture", 1.6m),
        new("map", 1.0m),
        new("refactor", 1.2m),
        new("design", 1.0m),
        new("structure", 1.0m),
        new("component", 1.0m),
        new("module", 1.2m),
        new("modules", 1.2m),
        new("startup", 1.0m),
        new("runtime", 0.9m),
        new("path", 0.7m),
        new("local", 0.5m),
        new("project", 1.6m),
        new("file", 1.8m),
        new("files", 1.8m),
        new("class", 1.8m),
        new("method", 1.8m),
        new("controller", 1.6m),
        new("service", 1.4m),
        new("services", 1.4m),
        new("implementation", 1.6m),
        new("source", 0.8m),
        new("symbol", 1.6m),
        new("symbols", 1.6m),
        new("blueprint", 0.9m),
        new("relationship", 1.0m),
        new("relationships", 1.0m),
        new("organized", 0.8m),
        new("folder", -1.2m),
        new("folders", -1.2m),
        new("compare", -0.9m),
        new("comparison", -0.7m),
        new("difference", -0.9m),
        new("differences", -0.9m),
        new("diff", -0.9m),
        new("destination", -0.8m),
        new("active directory", -1.1m, true),
        new("ldap", -1.4m),
        new("mailbox", -1.1m),
        new("email", -0.9m),
        new("emails", -0.9m),
        new("attribute", -0.8m),
        new("attributes", -0.8m),
        new("upn", -0.9m),
        new("userprincipalname", -1.0m),
        new("mailnickname", -1.0m)
    ];

    private static readonly WeightedFeature[] GenericAutomationFeatures =
    [
        new("powershell", 1.8m),
        new("script", 1.1m),
        new("export", 1.3m),
        new("csv", 1.4m),
        new("disabled", 1.1m),
        new("inactive", 1.0m),
        new("audit", 1.0m),
        new("report", 0.9m),
        new("reporting", 0.9m),
        new("query", 0.8m),
        new("list", 0.8m),
        new("automation", 1.1m),
        new("users", 0.5m),
        new("compare", 1.0m),
        new("comparison", 1.0m),
        new("difference", 1.0m),
        new("differences", 1.0m),
        new("diff", 0.9m),
        new("folder", 0.9m),
        new("folders", 0.9m),
        new("file", 0.7m),
        new("files", 0.7m),
        new("directory", 0.8m),
        new("directories", 0.8m),
        new("inventory", 0.7m),
        new("destination", 0.6m),
        new("grey", -1.6m),
        new("canary", -1.6m),
        new("focus", -1.1m),
        new("endpoint", -1.0m),
        new("repo", -1.4m),
        new("project", -1.3m),
        new("architecture", -1.1m),
        new("refactor", -1.0m),
        new("code", -1.2m)
    ];

    private static readonly WeightedFeature[] RepositoryArchitectureFeatures =
    [
        new("repo", 1.1m),
        new("repository", 1.1m),
        new("codebase", 1.8m),
        new("architecture", 2.6m),
        new("refactor", 1.9m),
        new("map", 1.5m),
        new("structure", 1.3m),
        new("design", 1.3m),
        new("system", 1.1m),
        new("module", 1.2m),
        new("modules", 1.2m),
        new("component", 1.2m),
        new("relationship", 1.1m),
        new("relationships", 1.1m),
        new("organized", 0.9m),
        new("onboarding", 1.0m),
        new("boundaries", 1.2m),
        new("blueprint", 1.6m),
        new("overview", 1.2m),
        new("fix", -1.1m),
        new("powershell", -1.2m),
        new("script", -1.0m),
        new("email", -0.9m),
        new("emails", -0.9m),
        new("mailbox", -1.0m),
        new("ldap", -1.1m),
        new("ad", -0.8m)
    ];

    public static TinyLocalPackIntentModel Shared { get; } = new();

    public string ModelId => "tiny-local-pack-intent-v2";

    public PackIntentPrediction Predict(string? question)
    {
        var normalized = Normalize(question);
        var tokens = Tokenize(question);
        if (tokens.Count == 0 && string.IsNullOrWhiteSpace(normalized))
        {
            return new PackIntentPrediction(0m, 0m, 0m, 0m, 0m, ModelId);
        }

        var externalOperationsScore = Score(normalized, tokens, -1.35m, ExternalOperationsFeatures);
        var directoryAdminScore = Score(normalized, tokens, -1.55m, DirectoryAdminFeatures);
        var codeIntentScore = Score(normalized, tokens, -1.75m, CodeIntentFeatures);
        var genericAutomationScore = Score(normalized, tokens, -1.85m, GenericAutomationFeatures);
        var repositoryArchitectureScore = Score(normalized, tokens, -1.95m, RepositoryArchitectureFeatures);

        return new PackIntentPrediction(
            externalOperationsScore,
            directoryAdminScore,
            codeIntentScore,
            genericAutomationScore,
            repositoryArchitectureScore,
            ModelId);
    }

    private static decimal Score(string normalizedQuestion, HashSet<string> tokens, decimal bias, IReadOnlyCollection<WeightedFeature> features)
    {
        decimal raw = bias;
        foreach (var feature in features)
        {
            if (feature.IsPhrase)
            {
                if (normalizedQuestion.Contains(feature.Feature, StringComparison.Ordinal))
                {
                    raw += feature.Weight;
                }

                continue;
            }

            if (tokens.Contains(feature.Feature))
            {
                raw += feature.Weight;
            }
        }

        var probability = 1d / (1d + Math.Exp((double)-raw));
        return decimal.Round((decimal)probability, 4);
    }

    private static string Normalize(string? question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return string.Empty;
        }

        return string.Join(' ', question
            .ToLowerInvariant()
            .Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static HashSet<string> Tokenize(string? question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return [];
        }

        return question
            .ToLowerInvariant()
            .Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 1)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record WeightedFeature(string Feature, decimal Weight, bool IsPhrase = false);
}
