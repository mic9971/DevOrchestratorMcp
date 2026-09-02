using System.Text.Json;
using DevOrchestrator.Application.Abstractions;
using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Application.Errors;
using DevOrchestrator.Common.Results;
using DevOrchestrator.Common.Time;
using DevOrchestrator.Domain.Tasks;

namespace DevOrchestrator.Application.Services;

internal sealed class ReviewService(
    ITargetProjectRepository projects,
    IDevelopmentTaskRepository tasks,
    IUnitOfWork unitOfWork,
    IClock clock) : IReviewService
{
    public async Task<Result<TaskDto>> SubmitAsync(
        string projectKey,
        string taskCode,
        string decision,
        string summary,
        IReadOnlyList<string> findings,
        string actor,
        bool completeOnPass,
        CancellationToken cancellationToken)
    {
        var key = projectKey.Trim().ToLowerInvariant();
        var project = await projects.GetByKeyAsync(key, cancellationToken);
        if (project is null)
        {
            return Result<TaskDto>.Failure(OrchestratorErrors.ProjectNotFound(key));
        }

        var code = taskCode.Trim().ToUpperInvariant();
        var task = await tasks.GetByCodeAsync(project.Id, code, cancellationToken);
        if (task is null)
        {
            return Result<TaskDto>.Failure(OrchestratorErrors.TaskNotFound(code));
        }

        if (!Enum.TryParse<ReviewDecision>(decision, true, out var reviewDecision))
        {
            return Result<TaskDto>.Failure(
                OrchestratorErrors.InvalidInput(
                    "decision must be 'Pass' or 'ChangesRequested'."));
        }

        try
        {
            var findingsJson = JsonSerializer.Serialize(findings ?? []);
            task.ApplyReview(
                reviewDecision,
                actor,
                summary,
                findingsJson,
                completeOnPass,
                clock.UtcNow);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            if (reviewDecision == ReviewDecision.Pass && completeOnPass)
            {
                await PromoteDependentsAsync(task.Id, actor, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            var allTasks = await tasks.ListAsync(project.Id, null, cancellationToken);
            var dependencyCodes = allTasks.ToDictionary(x => x.Id, x => x.Code);

            return Result<TaskDto>.Success(TaskMapping.Map(task, project.Key, dependencyCodes));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Result<TaskDto>.Failure(OrchestratorErrors.InvalidState(ex.Message));
        }
    }

    private async Task PromoteDependentsAsync(
        Guid completedTaskId,
        string actor,
        CancellationToken cancellationToken)
    {
        var dependents = await tasks.GetDependentsAsync(completedTaskId, cancellationToken);

        foreach (var dependent in dependents.Where(x => x.Status == DevelopmentTaskStatus.Draft))
        {
            if (await tasks.AreAllDependenciesDoneAsync(dependent.Id, cancellationToken))
            {
                dependent.MarkReady(actor, clock.UtcNow);
            }
        }
    }
}
