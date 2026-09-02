using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Common.Results;

namespace DevOrchestrator.Application.Services;

public interface IProjectService
{
    Task<Result<ProjectDto>> RegisterAsync(
        string key,
        string name,
        string repositoryUrl,
        string defaultBranch,
        string actor,
        CancellationToken cancellationToken);

    Task<Result<ProjectDto>> GetAsync(
        string key,
        CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<ProjectDto>>> ListAsync(
        CancellationToken cancellationToken);
}
