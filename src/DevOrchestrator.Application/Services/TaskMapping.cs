using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Domain.Tasks;

namespace DevOrchestrator.Application.Services;

internal static class TaskMapping
{
    public static TaskDto Map(
        DevelopmentTask task,
        string projectKey,
        IReadOnlyDictionary<Guid, string>? dependencyCodes = null)
    {
        var constraints = string.IsNullOrWhiteSpace(task.Constraints)
            ? []
            : task.Constraints.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var dependencies = task.Dependencies
            .Select(x => dependencyCodes is not null && dependencyCodes.TryGetValue(x.DependsOnTaskId, out var code)
                ? code
                : x.DependsOnTaskId.ToString())
            .ToArray();

        return new TaskDto(
            projectKey,
            task.Code,
            task.Title,
            task.Objective,
            constraints,
            task.Priority.ToString(),
            task.Status.ToString(),
            task.ActiveBranch,
            task.LastCommitSha,
            task.PullRequestUrl,
            task.BlockReason,
            task.LeaseOwner,
            task.LeaseExpiresAtUtc,
            task.LastHeartbeatAtUtc,
            task.AcceptanceCriteria.Select(x => new AcceptanceCriterionDto(x.Id, x.Description, x.IsSatisfied)).ToArray(),
            dependencies,
            task.Evidence.OrderBy(x => x.CreatedAtUtc).Select(x => new EvidenceDto(
                x.Actor,
                x.Branch,
                x.CommitSha,
                x.PullRequestUrl,
                x.PayloadJson,
                x.CreatedAtUtc)).ToArray(),
            task.Reviews.OrderBy(x => x.CreatedAtUtc).Select(x => new ReviewDto(
                x.Decision.ToString(),
                x.Actor,
                x.Summary,
                x.FindingsJson,
                x.CreatedAtUtc)).ToArray(),
            task.CreatedAtUtc,
            task.UpdatedAtUtc);
    }
}
