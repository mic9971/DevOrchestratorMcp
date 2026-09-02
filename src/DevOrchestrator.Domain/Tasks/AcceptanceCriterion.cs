using DevOrchestrator.Common;

namespace DevOrchestrator.Domain.Tasks;

public sealed class AcceptanceCriterion
{
    private AcceptanceCriterion()
    {
    }

    internal AcceptanceCriterion(Guid taskId, string description)
    {
        Id = Guid.NewGuid();
        TaskId = taskId;
        Description = Guard.NotBlank(description, nameof(description), 1000);
    }

    public Guid Id { get; private set; }

    public Guid TaskId { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public bool IsSatisfied { get; private set; }

    internal void MarkSatisfied() => IsSatisfied = true;

    internal void Reset() => IsSatisfied = false;
}
