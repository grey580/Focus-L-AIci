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
            RecommendedSkillCount = pack.RecommendedSkills.Count
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
}
