using DevOrchestrator.Application.Contracts;

namespace DevOrchestrator.Application.Abstractions;

public sealed record GitHubWebhookInboxLease(
    GitHubWebhookNotification Notification,
    int AttemptCount);

public interface IGitHubWebhookInbox
{
    Task<bool> EnqueueAsync(
        GitHubWebhookNotification notification,
        CancellationToken cancellationToken);

    Task<GitHubWebhookInboxLease?> TryLeaseNextAsync(
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    Task CompleteAsync(
        string deliveryId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken);

    Task RetryAsync(
        string deliveryId,
        string error,
        DateTimeOffset nextAttemptAtUtc,
        CancellationToken cancellationToken);

    Task DeadLetterAsync(
        string deliveryId,
        string error,
        DateTimeOffset deadLetteredAtUtc,
        CancellationToken cancellationToken);
}
