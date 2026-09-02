using DevOrchestrator.Common;

namespace DevOrchestrator.Domain.Tasks;

public sealed class TaskEvidence
{
    private TaskEvidence()
    {
    }

    internal TaskEvidence(
        Guid taskId,
        string actor,
        string branch,
        string commitSha,
        string? pullRequestUrl,
        string payloadJson,
        DateTimeOffset createdAtUtc)
    {
        TaskId = taskId;
        Actor = Guard.NotBlank(actor, nameof(actor), 120);
        Branch = Guard.NotBlank(branch, nameof(branch), 300);
        CommitSha = Guard.NotBlank(commitSha, nameof(commitSha), 120);
        PullRequestUrl = string.IsNullOrWhiteSpace(pullRequestUrl) ? null : pullRequestUrl.Trim();
        PayloadJson = Guard.NotBlank(payloadJson, nameof(payloadJson), 100_000);
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid TaskId { get; private set; }

    public string Actor { get; private set; } = string.Empty;

    public string Branch { get; private set; } = string.Empty;

    public string CommitSha { get; private set; } = string.Empty;

    public string? PullRequestUrl { get; private set; }

    public string PayloadJson { get; private set; } = "{}";

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
