using DevOrchestrator.Common;

namespace DevOrchestrator.Domain.Tasks;

public sealed class TaskReview
{
    private TaskReview()
    {
    }

    internal TaskReview(
        Guid taskId,
        ReviewDecision decision,
        string actor,
        string summary,
        string findingsJson,
        DateTimeOffset createdAtUtc)
    {
        Id = Guid.NewGuid();
        TaskId = taskId;
        Decision = decision;
        Actor = Guard.NotBlank(actor, nameof(actor), 120);
        Summary = Guard.NotBlank(summary, nameof(summary), 5000);
        FindingsJson = Guard.NotBlank(findingsJson, nameof(findingsJson), 100_000);
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid TaskId { get; private set; }

    public ReviewDecision Decision { get; private set; }

    public string Actor { get; private set; } = string.Empty;

    public string Summary { get; private set; } = string.Empty;

    public string FindingsJson { get; private set; } = "[]";

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
