using System.Text.Json;
using FocusLAIci.Web.Data;
using FocusLAIci.Web.Models;

namespace FocusLAIci.Web.Services;

public sealed class PackBuildArchiveService
{
    private readonly FocusMemoryContext _dbContext;

    public PackBuildArchiveService(FocusMemoryContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Guid> RecordAsync(ContextPackViewModel pack, CancellationToken cancellationToken)
    {
        var (reviewScore, reviewNotes) = BuildInitialReview(pack);
        var record = new PackBuildRecord
        {
            Question = pack.Question,
            GoalLabel = pack.GoalLabel,
            Summary = pack.Summary,
            ExportText = pack.ExportText,
            SearchTokensJson = JsonSerializer.Serialize(pack.SearchTokens),
            SuggestedSkillNamesJson = JsonSerializer.Serialize(pack.RecommendedSkills.Select(x => x.Name).ToArray()),
            ResultsPerSection = pack.Input.ResultsPerSection,
            TopMatchCount = pack.TopMatches.Count,
            MemoryCount = pack.Memories.Count,
            TodoCount = pack.Todos.Count,
            TicketCount = pack.Tickets.Count,
            CodeGraphProjectCount = pack.CodeGraphProjects.Count,
            CodeGraphFileCount = pack.CodeGraphFiles.Count,
            CodeGraphNodeCount = pack.CodeGraphNodes.Count,
            RecommendedSkillCount = pack.RecommendedSkills.Count,
            ReviewScore = reviewScore,
            ReviewNotes = reviewNotes
        };

        _dbContext.PackBuildRecords.Add(record);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return record.Id;
    }

    public async Task UpdateSuggestedExternalSkillCountAsync(Guid buildId, int suggestedCount, CancellationToken cancellationToken)
    {
        var record = await _dbContext.PackBuildRecords.FindAsync([buildId], cancellationToken);
        if (record is null)
        {
            return;
        }

        record.SuggestedExternalSkillCount = suggestedCount;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static (int? ReviewScore, string ReviewNotes) BuildInitialReview(ContextPackViewModel pack)
    {
        var notes = new List<string>();
        if (!string.IsNullOrWhiteSpace(pack.Decision.Kind))
        {
            notes.Add($"decision {pack.Decision.Kind}");
        }

        if (pack.Decision.Causes.Count > 0)
        {
            notes.Add($"causes: {string.Join(", ", pack.Decision.Causes)}");
        }

        if (pack.TopMatches.Count > 0 || pack.RecommendedSkills.Count > 0)
        {
            notes.Add($"retrieval: {pack.TopMatches.Count} matches, {pack.RecommendedSkills.Count} skills");
        }

        if (pack.ClarifyingQuestions.Count > 0)
        {
            notes.Add($"{pack.ClarifyingQuestions.Count} follow-ups");
        }

        var score = pack.NeedsMoreContext
            ? pack.Decision.Causes.Count > 0 && pack.ClarifyingQuestions.Count > 0
                ? 4
                : 3
            : pack.TopMatches.Count > 0 || pack.RecommendedSkills.Count > 0
                ? 5
                : 3;

        var reviewNotes = string.Join("; ", notes);
        if (reviewNotes.Length > 500)
        {
            reviewNotes = reviewNotes[..500];
        }

        return (score, reviewNotes);
    }
}
