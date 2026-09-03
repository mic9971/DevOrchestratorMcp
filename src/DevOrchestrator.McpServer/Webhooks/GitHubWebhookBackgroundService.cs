using DevOrchestrator.Application.Abstractions;
using DevOrchestrator.Application.Services;
using Microsoft.Extensions.Options;

namespace DevOrchestrator.McpServer.Webhooks;

public sealed class GitHubWebhookBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<GitHubWebhookOptions> options,
    ILogger<GitHubWebhookBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan InboxLease = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);
    private readonly int _maxAttempts = Math.Clamp(options.Value.WebhookMaxAttempts, 1, 100);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            GitHubWebhookInboxLease? lease = null;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var inbox = scope.ServiceProvider.GetRequiredService<IGitHubWebhookInbox>();
                lease = await inbox.TryLeaseNextAsync(DateTimeOffset.UtcNow, InboxLease, stoppingToken);
                if (lease is null)
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                    continue;
                }

                var processor = scope.ServiceProvider.GetRequiredService<IGitHubWebhookProcessor>();
                var result = await processor.ProcessAsync(lease.Notification, stoppingToken);
                if (result.IsSuccess)
                {
                    await inbox.CompleteAsync(
                        lease.Notification.DeliveryId,
                        DateTimeOffset.UtcNow,
                        stoppingToken);
                    continue;
                }

                await RetryOrDeadLetterAsync(
                    inbox,
                    lease,
                    $"{result.Error.Code}: {result.Error.Message}",
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "GitHub webhook background processing failed for delivery {DeliveryId}.",
                    lease?.Notification.DeliveryId);

                if (lease is not null)
                {
                    try
                    {
                        using var retryScope = scopeFactory.CreateScope();
                        var inbox = retryScope.ServiceProvider.GetRequiredService<IGitHubWebhookInbox>();
                        await RetryOrDeadLetterAsync(inbox, lease, ex.Message, stoppingToken);
                    }
                    catch (Exception retryException) when (retryException is not OperationCanceledException)
                    {
                        logger.LogError(retryException, "Failed to release webhook inbox lease.");
                    }
                }
            }
        }
    }

    private async Task RetryOrDeadLetterAsync(
        IGitHubWebhookInbox inbox,
        GitHubWebhookInboxLease lease,
        string error,
        CancellationToken cancellationToken)
    {
        if (lease.AttemptCount >= _maxAttempts)
        {
            logger.LogError(
                "GitHub webhook delivery {DeliveryId} exhausted {AttemptCount} attempts and was dead-lettered: {Error}",
                lease.Notification.DeliveryId,
                lease.AttemptCount,
                error);
            await inbox.DeadLetterAsync(
                lease.Notification.DeliveryId,
                error,
                DateTimeOffset.UtcNow,
                cancellationToken);
            return;
        }

        await inbox.RetryAsync(
            lease.Notification.DeliveryId,
            error,
            NextAttempt(lease.AttemptCount),
            cancellationToken);
    }

    private static DateTimeOffset NextAttempt(int attemptCount)
    {
        var seconds = Math.Min(900, 5 * Math.Pow(2, Math.Min(attemptCount, 7)));
        return DateTimeOffset.UtcNow.AddSeconds(seconds);
    }
}
