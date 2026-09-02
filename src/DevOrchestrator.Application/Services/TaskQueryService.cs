using DevOrchestrator.Application.Abstractions;
using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Application.Errors;
using DevOrchestrator.Common.Results;
using DevOrchestrator.Domain.Tasks;

namespace DevOrchestrator.Application.Services;

internal sealed class TaskQueryService(
    ITargetProjectRepository projects,
    ITaskReadRepository taskReads) : ITaskQueryService
{
    public async Task<Result<TaskPageDto>> ListPageAsync(
        string projectKey,
        string? status,
        int offset,
        int limit,
        CancellationToken cancellationToken)
    {
        if (offset < 0)
        {
            return Result<TaskPageDto>.Failure(
                OrchestratorErrors.InvalidInput("offset must be greater than or equal to zero."));
        }

        if (limit is < 1 or > 100)
        {
            return Result<TaskPageDto>.Failure(
                OrchestratorErrors.InvalidInput("limit must be between 1 and 100."));
        }

        DevelopmentTaskStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<DevelopmentTaskStatus>(status, true, out var value))
            {
                return Result<TaskPageDto>.Failure(
                    OrchestratorErrors.InvalidInput($"Unknown task status '{status}'."));
            }

            parsedStatus = value;
        }

        var key = projectKey.Trim().ToLowerInvariant();
        var project = await projects.GetByKeyAsync(key, cancellationToken);
        if (project is null)
        {
            return Result<TaskPageDto>.Failure(OrchestratorErrors.ProjectNotFound(key));
        }

        var page = await taskReads.ListPageAsync(
            project.Id,
            parsedStatus,
            offset,
            limit,
            cancellationToken);

        var items = page.Items
            .Select(x => new TaskSummaryDto(
                project.Key,
                x.Code,
                x.Title,
                x.Priority.ToString(),
                x.Status.ToString(),
                x.ActiveBranch,
                x.LastCommitSha,
                x.PullRequestUrl,
                x.BlockReason,
                x.CreatedAtUtc,
                x.UpdatedAtUtc))
            .ToArray();

        return Result<TaskPageDto>.Success(
            new TaskPageDto(
                items,
                offset,
                limit,
                page.HasMore ? offset + items.Length : null));
    }
}
