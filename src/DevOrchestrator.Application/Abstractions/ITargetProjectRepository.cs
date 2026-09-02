using DevOrchestrator.Domain.Projects;

namespace DevOrchestrator.Application.Abstractions;

public interface ITargetProjectRepository
{
    Task<TargetProject?> GetByKeyAsync(string key, CancellationToken cancellationToken);

    Task<IReadOnlyList<TargetProject>> ListAsync(CancellationToken cancellationToken);

    void Add(TargetProject project);
}
