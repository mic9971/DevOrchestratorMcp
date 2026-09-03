namespace DevOrchestrator.McpServer.Webhooks;

public sealed class GitHubWebhookOptions
{
    public string? WebhookSecret { get; init; }
    public int WebhookMaxAttempts { get; init; } = 8;
}
