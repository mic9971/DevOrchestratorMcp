namespace DevOrchestrator.Application.Contracts;

public sealed record TaskSummaryDto(
    string ProjectKey,
    string Code,
    string Title,
    string Priority,
    string Status,
    string? ActiveBranch,
    string? LastCommitSha,
    string? PullRequestUrl,
    string? BlockReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record TaskPageDto(
    IReadOnlyList<TaskSummaryDto> Items,
    int Offset,
    int Limit,
    int? NextOffset);
