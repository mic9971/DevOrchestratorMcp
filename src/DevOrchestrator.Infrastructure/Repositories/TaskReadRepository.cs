using DevOrchestrator.Application.Abstractions;
using DevOrchestrator.Domain.Tasks;
using DevOrchestrator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevOrchestrator.Infrastructure.Repositories;

internal sealed class TaskReadRepository(OrchestratorDbContext dbContext) : ITaskReadRepository
{
    public async Task<TaskSummaryReadPage> ListPageAsync(
        Guid projectId,
        DevelopmentTaskStatus? status,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.DevelopmentTasks
            .AsNoTracking()
            .Where(x => x.ProjectId == projectId);

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        var rows = await query
            .OrderBy(x => x.Code)
            .Skip(offset)
            .Take(limit + 1)
            .Select(x => new TaskSummaryReadModel(
                x.Code,
                x.Title,
                x.Priority,
                x.Status,
                x.ActiveBranch,
                x.LastCommitSha,
                x.PullRequestUrl,
                x.BlockReason,
                x.CreatedAtUtc,
                x.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > limit;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return new TaskSummaryReadPage(rows, hasMore);
    }
}
