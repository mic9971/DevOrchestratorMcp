using DevOrchestrator.Application.Abstractions;
using DevOrchestrator.Domain.Tasks;
using DevOrchestrator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevOrchestrator.Infrastructure.Repositories;

internal sealed class DevelopmentTaskRepository(OrchestratorDbContext dbContext)
    : IDevelopmentTaskRepository
{
    public Task<DevelopmentTask?> GetByCodeAsync(Guid projectId, string code, CancellationToken cancellationToken)
        => FullQuery().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Code == code, cancellationToken);

    public async Task<IReadOnlyList<DevelopmentTask>> ListAsync(Guid projectId, DevelopmentTaskStatus? status, CancellationToken cancellationToken)
    {
        var query = FullQuery().Where(x => x.ProjectId == projectId);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        return await query.OrderByDescending(x => x.Priority).ThenBy(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public Task<DevelopmentTask?> GetNextAsync(Guid projectId, CancellationToken cancellationToken)
        => FullQuery()
            .Where(x => x.ProjectId == projectId &&
                (x.Status == DevelopmentTaskStatus.ChangesRequested || x.Status == DevelopmentTaskStatus.Ready))
            .OrderBy(x => x.Status == DevelopmentTaskStatus.ChangesRequested ? 0 : 1)
            .ThenByDescending(x => x.Priority)
            .ThenBy(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<DevelopmentTask?> GetClaimCandidateAsync(
        Guid projectId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsSqlite())
        {
            var localCandidates = await FullQuery()
                .Where(x => x.ProjectId == projectId &&
                    (x.Status == DevelopmentTaskStatus.ChangesRequested ||
                     x.Status == DevelopmentTaskStatus.Ready ||
                     x.Status == DevelopmentTaskStatus.InProgress))
                .ToListAsync(cancellationToken);

            return localCandidates
                .Where(x => x.Status != DevelopmentTaskStatus.InProgress ||
                    (x.LeaseExpiresAtUtc.HasValue && x.LeaseExpiresAtUtc.Value <= now))
                .OrderBy(x => x.Status == DevelopmentTaskStatus.ChangesRequested ? 0 :
                              x.Status == DevelopmentTaskStatus.Ready ? 1 : 2)
                .ThenByDescending(x => x.Priority)
                .ThenBy(x => x.CreatedAtUtc)
                .FirstOrDefault();
        }

        return await FullQuery()
            .Where(x => x.ProjectId == projectId &&
                (x.Status == DevelopmentTaskStatus.ChangesRequested ||
                 x.Status == DevelopmentTaskStatus.Ready ||
                 (x.Status == DevelopmentTaskStatus.InProgress &&
                  x.LeaseExpiresAtUtc.HasValue &&
                  x.LeaseExpiresAtUtc.Value <= now)))
            .OrderBy(x => x.Status == DevelopmentTaskStatus.ChangesRequested ? 0 :
                          x.Status == DevelopmentTaskStatus.Ready ? 1 : 2)
            .ThenByDescending(x => x.Priority)
            .ThenBy(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DevelopmentTask>> GetDependentsAsync(Guid dependsOnTaskId, CancellationToken cancellationToken)
    {
        var dependentIds = await dbContext.TaskDependencies
            .Where(x => x.DependsOnTaskId == dependsOnTaskId)
            .Select(x => x.TaskId)
            .ToArrayAsync(cancellationToken);
        return await FullQuery().Where(x => dependentIds.Contains(x.Id)).ToListAsync(cancellationToken);
    }

    public async Task<bool> AreAllDependenciesDoneAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var dependencyIds = await dbContext.TaskDependencies
            .Where(x => x.TaskId == taskId)
            .Select(x => x.DependsOnTaskId)
            .ToArrayAsync(cancellationToken);
        if (dependencyIds.Length == 0) return true;
        var doneCount = await dbContext.DevelopmentTasks.CountAsync(
            x => dependencyIds.Contains(x.Id) && x.Status == DevelopmentTaskStatus.Done,
            cancellationToken);
        return doneCount == dependencyIds.Length;
    }

    public async Task<IReadOnlyDictionary<Guid, DevelopmentTaskStatus>> GetStatusesAsync(
        IEnumerable<Guid> taskIds,
        CancellationToken cancellationToken)
    {
        var ids = taskIds.Distinct().ToArray();
        return await dbContext.DevelopmentTasks
            .Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Status, cancellationToken);
    }

    public void Add(DevelopmentTask task) => dbContext.DevelopmentTasks.Add(task);
    public void AddRange(IEnumerable<DevelopmentTask> tasks) => dbContext.DevelopmentTasks.AddRange(tasks);

    private IQueryable<DevelopmentTask> FullQuery()
        => dbContext.DevelopmentTasks
            .Include(x => x.AcceptanceCriteria)
            .Include(x => x.Dependencies)
            .Include(x => x.Evidence)
            .Include(x => x.Reviews)
            .Include(x => x.Events);
}
