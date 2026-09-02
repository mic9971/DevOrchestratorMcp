using DevOrchestrator.Domain.Tasks;

namespace DevOrchestrator.Application.Abstractions;

public interface IDevelopmentTaskRepository
{
    Task<DevelopmentTask?> GetByCodeAsync(
        Guid projectId,
        string code,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DevelopmentTask>> ListAsync(
        Guid projectId,
        DevelopmentTaskStatus? status,
        CancellationToken cancellationToken);

    Task<DevelopmentTask?> GetNextAsync(
        Guid projectId,
        CancellationToken cancellationToken);

    Task<DevelopmentTask?> GetClaimCandidateAsync(
        Guid projectId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<DevelopmentTask>> GetDependentsAsync(
        Guid dependsOnTaskId,
        CancellationToken cancellationToken);

    Task<bool> AreAllDependenciesDoneAsync(
        Guid taskId,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, DevelopmentTaskStatus>> GetStatusesAsync(
        IEnumerable<Guid> taskIds,
        CancellationToken cancellationToken);

    void Add(DevelopmentTask task);
    void AddRange(IEnumerable<DevelopmentTask> tasks);
}
