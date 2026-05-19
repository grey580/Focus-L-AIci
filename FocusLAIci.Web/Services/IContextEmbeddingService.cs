using FocusLAIci.Web.Models;

namespace FocusLAIci.Web.Services;

public interface IContextEmbeddingService
{
    bool IsEnabled { get; }

    Task<IReadOnlyDictionary<Guid, SemanticCandidateScore>> ScoreAsync(
        EmbeddingTargetKind targetKind,
        string query,
        IReadOnlyDictionary<Guid, string> candidates,
        CancellationToken cancellationToken);
}

public sealed record SemanticCandidateScore(decimal Similarity, decimal Boost, string Label);
