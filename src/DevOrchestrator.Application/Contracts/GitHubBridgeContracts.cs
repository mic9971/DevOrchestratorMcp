namespace DevOrchestrator.Application.Contracts;

public sealed record GitHubPlanTaskContract(
    string Code,
    string Title,
    string Objective,
    string[] AcceptanceCriteria,
    string[]? Dependencies = null,
    string[]? Constraints = null,
    string Priority = "Normal");

public sealed record GitHubPlanContract(
    string Schema,
    string ProjectKey,
    GitHubPlanTaskContract[] Tasks);

public sealed record GitHubReviewContract(
    string Schema,
    string TaskCode,
    string Decision,
    string Summary,
    string[]? Findings = null,
    bool CompleteOnPass = true);

public sealed record GitHubBridgeImportResult(
    int Created,
    int Skipped,
    IReadOnlyList<string> SkippedTaskCodes,
    string SourceIssueUrl,
    IReadOnlyList<TaskDto> CreatedTasks);

public sealed record GitHubBridgeAppliedReview(
    string TaskCode,
    string Decision,
    long CommentId,
    string Actor,
    string ResultingStatus);

public sealed record GitHubBridgeReviewSyncResult(
    int Applied,
    int Ignored,
    int Invalid,
    string SourceIssueUrl,
    IReadOnlyList<GitHubBridgeAppliedReview> Reviews);
