namespace DevOrchestrator.Application.Contracts;

public sealed record CreateTaskSeed(
    string Code,
    string Title,
    string Objective,
    string[] AcceptanceCriteria,
    string[]? Dependencies = null,
    string[]? Constraints = null,
    string Priority = "normal");

public sealed record EvidenceInput(
    string Branch,
    string CommitSha,
    string? PullRequestUrl,
    string[] FilesChanged,
    string[] Tests,
    string[] Commands,
    string? Notes);
