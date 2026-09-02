namespace DevOrchestrator.Infrastructure.Persistence;

public sealed class GitHubWebhookDelivery
{
    private GitHubWebhookDelivery()
    {
    }

    public GitHubWebhookDelivery(
        string deliveryId,
        string eventName,
        DateTimeOffset receivedAtUtc)
    {
        DeliveryId = deliveryId;
        EventName = eventName;
        ReceivedAtUtc = receivedAtUtc;
    }

    public string DeliveryId { get; private set; } = string.Empty;

    public string EventName { get; private set; } = string.Empty;

    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public void Complete(DateTimeOffset completedAtUtc)
        => CompletedAtUtc = completedAtUtc;
}
