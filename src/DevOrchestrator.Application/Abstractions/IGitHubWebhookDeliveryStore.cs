namespace DevOrchestrator.Application.Abstractions;

public interface IGitHubWebhookDeliveryStore
{
    Task<bool> TryBeginAsync(
        string deliveryId,
        string eventName,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        string deliveryId,
        CancellationToken cancellationToken);

    Task AbandonAsync(
        string deliveryId,
        CancellationToken cancellationToken);
}
