namespace DevOrchestrator.Application.Contracts;

public sealed record ProjectDto(
    string Key,
    string Name,
    string RepositoryUrl,
    string DefaultBranch,
    bool IsActive);

public sealed record AcceptanceCriterionDto(
    Guid Id,
    string Description,
    bool IsSatisfied);

public sealed record EvidenceDto(
    string Actor,
    string Branch,
    string CommitSha,
    string? PullRequestUrl,
    string PayloadJson,
    DateTimeOffset CreatedAtUtc);

public sealed record ReviewDto(
    string Decision,
    string Actor,
    string Summary,
    string FindingsJson,
    DateTimeOffset CreatedAtUtc);

public sealed record TaskDto(
    string ProjectKey,
    string Code,
    string Title,
    string Objective,
    string[] Constraints,
    string Priority,
    string Status,
    string? ActiveBranch,
    string? LastCommitSha,
    string? PullRequestUrl,
    string? BlockReason,
    string? LeaseOwner,
    DateTimeOffset? LeaseExpiresAtUtc,
    DateTimeOffset? LastHeartbeatAtUtc,
    IReadOnlyList<AcceptanceCriterionDto> AcceptanceCriteria,
    IReadOnlyList<string> DependencyTaskCodes,
    IReadOnlyList<EvidenceDto> Evidence,
    IReadOnlyList<ReviewDto> Reviews,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record BatchCreateResult(
    int Created,
    IReadOnlyList<TaskDto> Tasks);
