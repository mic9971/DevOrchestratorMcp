using DevOrchestrator.Domain.Tasks;

namespace DevOrchestrator.Application.Abstractions;

public sealed record TaskSummaryReadModel(
    string Code,
    string Title,
    TaskPriority Priority,
    DevelopmentTaskStatus Status,
    string? ActiveBranch,
    string? LastCommitSha,
    string? PullRequestUrl,
    string? BlockReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record TaskSummaryReadPage(
    IReadOnlyList<TaskSummaryReadModel> Items,
    bool HasMore);

public interface ITaskReadRepository
{
    Task<TaskSummaryReadPage> ListPageAsync(
        Guid projectId,
        DevelopmentTaskStatus? status,
        int offset,
        int limit,
        CancellationToken cancellationToken);
}
