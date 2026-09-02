namespace DevOrchestrator.Infrastructure.Persistence;

public sealed class GitHubWebhookDelivery
{
    private GitHubWebhookDelivery()
    {
    }

    public GitHubWebhookDelivery(
        string deliveryId,
        string eventName,
        DateTime receivedAtUtc,
        DateTime leaseExpiresAtUtc)
    {
        DeliveryId = deliveryId;
        EventName = eventName;
        ReceivedAtUtc = receivedAtUtc;
        LeaseExpiresAtUtc = leaseExpiresAtUtc;
    }

    public string DeliveryId { get; private set; } = string.Empty;

    public string EventName { get; private set; } = string.Empty;

    public DateTime ReceivedAtUtc { get; private set; }

    public DateTime LeaseExpiresAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public void Complete(DateTime completedAtUtc)
        => CompletedAtUtc = completedAtUtc;
}
