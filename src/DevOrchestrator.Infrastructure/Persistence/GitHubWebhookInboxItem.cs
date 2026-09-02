using DevOrchestrator.Application.Contracts;

namespace DevOrchestrator.Infrastructure.Persistence;

public sealed class GitHubWebhookInboxItem
{
    private GitHubWebhookInboxItem()
    {
    }

    private GitHubWebhookInboxItem(
        GitHubWebhookNotification notification,
        DateTimeOffset receivedAtUtc)
    {
        DeliveryId = notification.DeliveryId;
        EventName = notification.EventName;
        Action = notification.Action;
        RepositoryUrl = notification.RepositoryUrl;
        IssueNumber = notification.IssueNumber;
        ReceivedAtUtc = receivedAtUtc;
        NextAttemptAtUtc = receivedAtUtc;
    }

    public string DeliveryId { get; private set; } = string.Empty;
    public string EventName { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string RepositoryUrl { get; private set; } = string.Empty;
    public int IssueNumber { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset ReceivedAtUtc { get; private set; }
    public DateTimeOffset NextAttemptAtUtc { get; private set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public string? LastError { get; private set; }

    public static GitHubWebhookInboxItem Create(
        GitHubWebhookNotification notification,
        DateTimeOffset receivedAtUtc)
        => new(notification, receivedAtUtc);

    public GitHubWebhookNotification ToNotification()
        => new(DeliveryId, EventName, Action, RepositoryUrl, IssueNumber);
}
