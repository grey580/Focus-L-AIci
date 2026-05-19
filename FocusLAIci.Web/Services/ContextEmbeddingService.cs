using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Unicode;
using FocusLAIci.Web.Data;
using FocusLAIci.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace FocusLAIci.Web.Services;

public sealed class ContextEmbeddingService : IContextEmbeddingService
{
    private const int MaxSemanticCandidates = 24;
    private const decimal MinimumSemanticSimilarity = 0.42m;
    private readonly FocusMemoryContext _dbContext;
    private readonly ILogger<ContextEmbeddingService> _logger;
    private readonly string? _contentRootPath;

    private static readonly object RuntimeLock = new();
    private static SemanticEmbeddingRuntime? s_runtime;
    private static bool s_runtimeInitialized;

    public ContextEmbeddingService(
        FocusMemoryContext dbContext,
        ILogger<ContextEmbeddingService> logger,
        IHostEnvironment? hostEnvironment = null)
    {
        _dbContext = dbContext;
        _logger = logger;
        _contentRootPath = hostEnvironment?.ContentRootPath;
    }

    public bool IsEnabled => GetRuntime() is not null;

    public async Task<IReadOnlyDictionary<Guid, SemanticCandidateScore>> ScoreAsync(
        EmbeddingTargetKind targetKind,
        string query,
        IReadOnlyDictionary<Guid, string> candidates,
        CancellationToken cancellationToken)
    {
        var runtime = GetRuntime();
        if (runtime is null || string.IsNullOrWhiteSpace(query) || candidates.Count == 0)
        {
            return new Dictionary<Guid, SemanticCandidateScore>();
        }

        var candidateBatch = candidates
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Take(MaxSemanticCandidates)
            .ToArray();

        if (candidateBatch.Length == 0)
        {
            return new Dictionary<Guid, SemanticCandidateScore>();
        }

        var queryVector = runtime.Embed(query.Trim());
        var targetIds = candidateBatch.Select(x => x.Key).ToArray();
        var existingEntries = await _dbContext.Embeddings
            .Where(x => x.TargetKind == targetKind && targetIds.Contains(x.TargetId))
            .ToDictionaryAsync(x => x.TargetId, cancellationToken);

        var semanticScores = new Dictionary<Guid, SemanticCandidateScore>();
        var hasChanges = false;

        foreach (var (targetId, text) in candidateBatch)
        {
            var normalizedText = NormalizeSemanticText(text);
            if (string.IsNullOrWhiteSpace(normalizedText))
            {
                continue;
            }

            var contentHash = ComputeHash(normalizedText);
            if (!existingEntries.TryGetValue(targetId, out var entry) || !string.Equals(entry.ContentHash, contentHash, StringComparison.Ordinal))
            {
                var vector = runtime.Embed(normalizedText);
                entry ??= new EmbeddingEntry
                {
                    TargetKind = targetKind,
                    TargetId = targetId
                };

                entry.ContentHash = contentHash;
                entry.VectorSize = vector.Length;
                entry.VectorBlob = SerializeVector(vector);
                entry.UpdatedUtc = DateTime.UtcNow;

                if (_dbContext.Entry(entry).State == EntityState.Detached)
                {
                    _dbContext.Embeddings.Add(entry);
                }

                existingEntries[targetId] = entry;
                hasChanges = true;
            }

            var candidateVector = DeserializeVector(existingEntries[targetId]);
            if (candidateVector.Length == 0 || candidateVector.Length != queryVector.Length)
            {
                continue;
            }

            var similarity = CosineSimilarity(queryVector, candidateVector);
            if (similarity < MinimumSemanticSimilarity)
            {
                continue;
            }

            var boost = CalculateBoost(similarity);
            if (boost <= 0m)
            {
                continue;
            }

            semanticScores[targetId] = new SemanticCandidateScore(
                similarity,
                boost,
                FormattableString.Invariant($"Semantic similarity ({similarity:0.00})"));
        }

        if (hasChanges)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return semanticScores;
    }

    private SemanticEmbeddingRuntime? GetRuntime()
    {
        if (s_runtimeInitialized)
        {
            return s_runtime;
        }

        lock (RuntimeLock)
        {
            if (s_runtimeInitialized)
            {
                return s_runtime;
            }

            s_runtimeInitialized = true;

            try
            {
                var modelDirectory = ResolveModelDirectory(_contentRootPath);
                if (string.IsNullOrWhiteSpace(modelDirectory))
                {
                    _logger.LogWarning("Semantic model assets were not found. Hybrid semantic search is disabled.");
                    return null;
                }

                s_runtime = new SemanticEmbeddingRuntime(modelDirectory);
                _logger.LogInformation("Loaded local semantic model from {ModelDirectory}.", modelDirectory);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize local semantic model. Hybrid semantic search is disabled.");
                s_runtime = null;
            }

            return s_runtime;
        }
    }

    private static string? ResolveModelDirectory(string? contentRootPath)
    {
        var roots = new[]
        {
            contentRootPath,
            AppContext.BaseDirectory
        };

        foreach (var root in roots.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var candidate = Path.Combine(root!, "Models", "Embeddings", "all-MiniLM-L6-v2");
            if (File.Exists(Path.Combine(candidate, "model_quantized.onnx"))
                && File.Exists(Path.Combine(candidate, "vocab.txt")))
            {
                return candidate;
            }
        }

        return null;
    }

    private static decimal CalculateBoost(decimal similarity)
        => Math.Round(Math.Min(12m, Math.Max(0m, (similarity - 0.38m) * 28m)), 2, MidpointRounding.AwayFromZero);

    private static string NormalizeSemanticText(string value)
        => string.Join(' ', value
            .Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Trim();

    private static string ComputeHash(string value)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hashBytes);
    }

    private static byte[] SerializeVector(float[] vector)
    {
        var buffer = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, buffer, 0, buffer.Length);
        return buffer;
    }

    private static float[] DeserializeVector(EmbeddingEntry entry)
    {
        if (entry.VectorSize <= 0 || entry.VectorBlob.Length < entry.VectorSize * sizeof(float))
        {
            return [];
        }

        var vector = new float[entry.VectorSize];
        Buffer.BlockCopy(entry.VectorBlob, 0, vector, 0, entry.VectorSize * sizeof(float));
        return vector;
    }

    private static decimal CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        double dot = 0d;
        double leftMagnitude = 0d;
        double rightMagnitude = 0d;

        for (var index = 0; index < left.Count; index++)
        {
            dot += left[index] * right[index];
            leftMagnitude += left[index] * left[index];
            rightMagnitude += right[index] * right[index];
        }

        if (leftMagnitude <= 0d || rightMagnitude <= 0d)
        {
            return 0m;
        }

        return (decimal)(dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude)));
    }

    private sealed class SemanticEmbeddingRuntime : IDisposable
    {
        private const int MaxSequenceLength = 256;
        private readonly InferenceSession _session;
        private readonly MiniLmWordPieceTokenizer _tokenizer;
        private readonly string[] _inputNames;
        private readonly string _primaryOutputName;

        public SemanticEmbeddingRuntime(string modelDirectory)
        {
            _tokenizer = new MiniLmWordPieceTokenizer(Path.Combine(modelDirectory, "vocab.txt"));
            _session = new InferenceSession(
                Path.Combine(modelDirectory, "model_quantized.onnx"),
                new Microsoft.ML.OnnxRuntime.SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
                });
            _inputNames = _session.InputMetadata.Keys.ToArray();
            _primaryOutputName = _session.OutputMetadata.Keys.Contains("sentence_embedding", StringComparer.Ordinal)
                ? "sentence_embedding"
                : _session.OutputMetadata.Keys.Contains("last_hidden_state", StringComparer.Ordinal)
                    ? "last_hidden_state"
                    : _session.OutputMetadata.Keys.First();
        }

        public float[] Embed(string text)
        {
            var encoding = _tokenizer.Encode(text, MaxSequenceLength);
            var inputIdsTensor = new DenseTensor<long>(new[] { 1, encoding.InputIds.Length });
            var attentionMaskTensor = new DenseTensor<long>(new[] { 1, encoding.AttentionMask.Length });
            var tokenTypeTensor = new DenseTensor<long>(new[] { 1, encoding.InputIds.Length });

            for (var index = 0; index < encoding.InputIds.Length; index++)
            {
                inputIdsTensor[0, index] = encoding.InputIds[index];
                attentionMaskTensor[0, index] = encoding.AttentionMask[index];
                tokenTypeTensor[0, index] = 0;
            }

            var inputs = new List<NamedOnnxValue>();
            foreach (var inputName in _inputNames)
            {
                if (string.Equals(inputName, "input_ids", StringComparison.Ordinal))
                {
                    inputs.Add(NamedOnnxValue.CreateFromTensor(inputName, inputIdsTensor));
                }
                else if (string.Equals(inputName, "attention_mask", StringComparison.Ordinal))
                {
                    inputs.Add(NamedOnnxValue.CreateFromTensor(inputName, attentionMaskTensor));
                }
                else if (string.Equals(inputName, "token_type_ids", StringComparison.Ordinal))
                {
                    inputs.Add(NamedOnnxValue.CreateFromTensor(inputName, tokenTypeTensor));
                }
            }

            using var results = _session.Run(inputs);
            var output = results.First(result => string.Equals(result.Name, _primaryOutputName, StringComparison.Ordinal));
            var tensor = output.AsTensor<float>();

            if (string.Equals(_primaryOutputName, "sentence_embedding", StringComparison.Ordinal) && tensor.Rank == 2)
            {
                return NormalizeVector(ExtractRow(tensor, tensor.Dimensions[1]));
            }

            if (tensor.Rank == 3)
            {
                return MeanPool(tensor, encoding.AttentionMask);
            }

            return NormalizeVector(tensor.ToArray());
        }

        public void Dispose() => _session.Dispose();

        private static float[] ExtractRow(Tensor<float> tensor, int dimensionCount)
        {
            var vector = new float[dimensionCount];
            for (var index = 0; index < dimensionCount; index++)
            {
                vector[index] = tensor[0, index];
            }

            return vector;
        }

        private static float[] MeanPool(Tensor<float> tensor, IReadOnlyList<long> attentionMask)
        {
            var sequenceLength = tensor.Dimensions[1];
            var dimensionCount = tensor.Dimensions[2];
            var vector = new float[dimensionCount];
            double tokenCount = 0d;

            for (var tokenIndex = 0; tokenIndex < sequenceLength && tokenIndex < attentionMask.Count; tokenIndex++)
            {
                if (attentionMask[tokenIndex] == 0)
                {
                    continue;
                }

                tokenCount += 1d;
                for (var dimensionIndex = 0; dimensionIndex < dimensionCount; dimensionIndex++)
                {
                    vector[dimensionIndex] += tensor[0, tokenIndex, dimensionIndex];
                }
            }

            if (tokenCount <= 0d)
            {
                return NormalizeVector(vector);
            }

            for (var dimensionIndex = 0; dimensionIndex < dimensionCount; dimensionIndex++)
            {
                vector[dimensionIndex] = (float)(vector[dimensionIndex] / tokenCount);
            }

            return NormalizeVector(vector);
        }

        private static float[] NormalizeVector(float[] vector)
        {
            double magnitude = 0d;
            for (var index = 0; index < vector.Length; index++)
            {
                magnitude += vector[index] * vector[index];
            }

            if (magnitude <= 0d)
            {
                return vector;
            }

            var scale = 1d / Math.Sqrt(magnitude);
            for (var index = 0; index < vector.Length; index++)
            {
                vector[index] = (float)(vector[index] * scale);
            }

            return vector;
        }
    }

    private sealed class MiniLmWordPieceTokenizer
    {
        private const string UnknownToken = "[UNK]";
        private const string ClassifierToken = "[CLS]";
        private const string SeparatorToken = "[SEP]";
        private readonly IReadOnlyDictionary<string, int> _vocabulary;
        private readonly int _unknownTokenId;
        private readonly int _classifierTokenId;
        private readonly int _separatorTokenId;

        public MiniLmWordPieceTokenizer(string vocabularyPath)
        {
            _vocabulary = File.ReadAllLines(vocabularyPath)
                .Select((token, index) => new KeyValuePair<string, int>(token.Trim(), index))
                .Where(x => !string.IsNullOrWhiteSpace(x.Key))
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

            _unknownTokenId = _vocabulary[UnknownToken];
            _classifierTokenId = _vocabulary[ClassifierToken];
            _separatorTokenId = _vocabulary[SeparatorToken];
        }

        public TokenEncoding Encode(string text, int maxSequenceLength)
        {
            var wordPieces = BasicTokenize(text)
                .SelectMany(TokenizeWordPiece)
                .Take(Math.Max(1, maxSequenceLength - 2))
                .ToList();

            var ids = new List<long>(wordPieces.Count + 2) { _classifierTokenId };
            ids.AddRange(wordPieces.Select(piece => (long)_vocabulary.GetValueOrDefault(piece, _unknownTokenId)));
            ids.Add(_separatorTokenId);

            return new TokenEncoding(ids.ToArray(), Enumerable.Repeat(1L, ids.Count).ToArray());
        }

        private IEnumerable<string> BasicTokenize(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);

            foreach (var rune in normalized.EnumerateRunes())
            {
                if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                if (Rune.IsWhiteSpace(rune))
                {
                    builder.Append(' ');
                    continue;
                }

                if (Rune.IsLetterOrDigit(rune))
                {
                    builder.Append(rune.ToString().ToLowerInvariant());
                    continue;
                }

                builder.Append(' ').Append(rune.ToString()).Append(' ');
            }

            return builder.ToString()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private IEnumerable<string> TokenizeWordPiece(string token)
        {
            if (token.Length > 100)
            {
                yield return UnknownToken;
                yield break;
            }

            var start = 0;
            var pieces = new List<string>();

            while (start < token.Length)
            {
                string? currentPiece = null;
                var end = token.Length;

                while (start < end)
                {
                    var segment = token.Substring(start, end - start);
                    if (start > 0)
                    {
                        segment = $"##{segment}";
                    }

                    if (_vocabulary.ContainsKey(segment))
                    {
                        currentPiece = segment;
                        break;
                    }

                    end--;
                }

                if (currentPiece is null)
                {
                    yield return UnknownToken;
                    yield break;
                }

                pieces.Add(currentPiece);
                start = end;
            }

            foreach (var piece in pieces)
            {
                yield return piece;
            }
        }

        internal sealed record TokenEncoding(long[] InputIds, long[] AttentionMask);
    }
}
