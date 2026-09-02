namespace DevOrchestrator.Application.Contracts;

public sealed record GitHubWebhookNotification(
    string DeliveryId,
    string EventName,
    string Action,
    string RepositoryUrl,
    int IssueNumber);

public sealed record GitHubWebhookProcessResult(
    string DeliveryId,
    string EventName,
    string Action,
    string Outcome,
    string? ProjectKey,
    int IssueNumber);
