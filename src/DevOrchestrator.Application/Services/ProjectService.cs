using DevOrchestrator.Application.Abstractions;
using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Application.Errors;
using DevOrchestrator.Common.Results;
using DevOrchestrator.Common.Time;
using DevOrchestrator.Domain.Projects;

namespace DevOrchestrator.Application.Services;

internal sealed class ProjectService(
    ITargetProjectRepository projects,
    IUnitOfWork unitOfWork,
    IClock clock) : IProjectService
{
    public async Task<Result<ProjectDto>> RegisterAsync(
        string key,
        string name,
        string repositoryUrl,
        string defaultBranch,
        string actor,
        CancellationToken cancellationToken)
    {
        key = NormalizeKey(key);

        if (await projects.GetByKeyAsync(key, cancellationToken) is not null)
        {
            return Result<ProjectDto>.Failure(OrchestratorErrors.ProjectAlreadyExists(key));
        }

        try
        {
            _ = actor;
            var project = TargetProject.Create(key, name, repositoryUrl, defaultBranch, clock.UtcNow);
            projects.Add(project);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<ProjectDto>.Success(Map(project));
        }
        catch (ArgumentException ex)
        {
            return Result<ProjectDto>.Failure(OrchestratorErrors.InvalidInput(ex.Message));
        }
    }

    public async Task<Result<ProjectDto>> GetAsync(
        string key,
        CancellationToken cancellationToken)
    {
        key = NormalizeKey(key);
        var project = await projects.GetByKeyAsync(key, cancellationToken);

        return project is null
            ? Result<ProjectDto>.Failure(OrchestratorErrors.ProjectNotFound(key))
            : Result<ProjectDto>.Success(Map(project));
    }

    public async Task<Result<IReadOnlyList<ProjectDto>>> ListAsync(
        CancellationToken cancellationToken)
    {
        var items = await projects.ListAsync(cancellationToken);
        return Result<IReadOnlyList<ProjectDto>>.Success(items.Select(Map).ToArray());
    }

    private static ProjectDto Map(TargetProject project)
        => new(
            project.Key,
            project.Name,
            project.RepositoryUrl,
            project.DefaultBranch,
            project.IsActive);

    private static string NormalizeKey(string key)
        => key.Trim().ToLowerInvariant();
}
