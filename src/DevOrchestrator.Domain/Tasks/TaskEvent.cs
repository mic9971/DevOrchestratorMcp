using DevOrchestrator.Common;

namespace DevOrchestrator.Domain.Tasks;

public sealed class TaskEvent
{
    private TaskEvent()
    {
    }

    internal TaskEvent(
        Guid taskId,
        string eventType,
        string actor,
        string payloadJson,
        DateTimeOffset createdAtUtc)
    {
        Id = Guid.NewGuid();
        TaskId = taskId;
        EventType = Guard.NotBlank(eventType, nameof(eventType), 120);
        Actor = Guard.NotBlank(actor, nameof(actor), 120);
        PayloadJson = string.IsNullOrWhiteSpace(payloadJson) ? "{}" : payloadJson;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public Guid TaskId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    public string Actor { get; private set; } = string.Empty;

    public string PayloadJson { get; private set; } = "{}";

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
