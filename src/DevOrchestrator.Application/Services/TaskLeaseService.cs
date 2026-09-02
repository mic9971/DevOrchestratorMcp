using DevOrchestrator.Application.Abstractions;
using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Application.Errors;
using DevOrchestrator.Common.Results;
using DevOrchestrator.Common.Time;
using DevOrchestrator.Domain.Projects;
using DevOrchestrator.Domain.Tasks;

namespace DevOrchestrator.Application.Services;

internal sealed class TaskLeaseService(
    ITargetProjectRepository projects,
    IDevelopmentTaskRepository tasks,
    IUnitOfWork unitOfWork,
    IClock clock) : ITaskLeaseService
{
    private static readonly TimeSpan WorkerLeaseDuration = TimeSpan.FromMinutes(10);

    public async Task<Result<TaskDto?>> ClaimNextAsync(
        string projectKey,
        string workerId,
        string actor,
        string? branch,
        CancellationToken cancellationToken)
    {
        var projectResult = await GetProjectAsync(projectKey, cancellationToken);
        if (projectResult.IsFailure)
        {
            return Result<TaskDto?>.Failure(projectResult.Error);
        }

        var project = projectResult.Value!;
        var now = clock.UtcNow;
        var task = await tasks.GetClaimCandidateAsync(project.Id, now, cancellationToken);
        if (task is null)
        {
            return Result<TaskDto?>.Success(null);
        }

        try
        {
            task.Claim(actor, workerId, branch, now, WorkerLeaseDuration);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<TaskDto?>.Success(await MapWithDependencyCodesAsync(project, task, cancellationToken));
        }
        catch (ConcurrencyConflictException ex)
        {
            return Result<TaskDto?>.Failure(OrchestratorErrors.ConcurrencyConflict(ex.Message));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            return Result<TaskDto?>.Failure(OrchestratorErrors.InvalidState(ex.Message));
        }
    }

    public async Task<Result<TaskDto>> HeartbeatAsync(
        string projectKey,
        string taskCode,
        string workerId,
        string actor,
        CancellationToken cancellationToken)
    {
        var projectResult = await GetProjectAsync(projectKey, cancellationToken);
        if (projectResult.IsFailure)
        {
            return Result<TaskDto>.Failure(projectResult.Error);
        }

        var project = projectResult.Value!;
        var normalizedCode = taskCode.Trim().ToUpperInvariant();
        var task = await tasks.GetByCodeAsync(project.Id, normalizedCode, cancellationToken);
        if (task is null)
        {
            return Result<TaskDto>.Failure(OrchestratorErrors.TaskNotFound(normalizedCode));
        }

        try
        {
            task.Heartbeat(actor, workerId, clock.UtcNow, WorkerLeaseDuration);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<TaskDto>.Success(await MapWithDependencyCodesAsync(project, task, cancellationToken));
        }
        catch (ConcurrencyConflictException ex)
        {
            return Result<TaskDto>.Failure(OrchestratorErrors.ConcurrencyConflict(ex.Message));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            return Result<TaskDto>.Failure(OrchestratorErrors.InvalidState(ex.Message));
        }
    }

    private async Task<Result<TargetProject>> GetProjectAsync(
        string projectKey,
        CancellationToken cancellationToken)
    {
        var key = projectKey.Trim().ToLowerInvariant();
        var project = await projects.GetByKeyAsync(key, cancellationToken);
        return project is null
            ? Result<TargetProject>.Failure(OrchestratorErrors.ProjectNotFound(key))
            : Result<TargetProject>.Success(project);
    }

    private async Task<TaskDto> MapWithDependencyCodesAsync(
        TargetProject project,
        DevelopmentTask task,
        CancellationToken cancellationToken)
    {
        if (task.Dependencies.Count == 0)
        {
            return TaskMapping.Map(task, project.Key);
        }

        var ids = task.Dependencies.Select(x => x.DependsOnTaskId).ToArray();
        var allTasks = await tasks.ListAsync(project.Id, null, cancellationToken);
        var codeById = allTasks
            .Where(x => ids.Contains(x.Id))
            .ToDictionary(x => x.Id, x => x.Code);
        return TaskMapping.Map(task, project.Key, codeById);
    }
}
