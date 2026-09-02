using DevOrchestrator.Application.Abstractions;
using DevOrchestrator.Application.Services;

namespace DevOrchestrator.McpServer.Webhooks;

public sealed class GitHubWebhookBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<GitHubWebhookBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan InboxLease = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(1);

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

                await inbox.RetryAsync(
                    lease.Notification.DeliveryId,
                    $"{result.Error.Code}: {result.Error.Message}",
                    NextAttempt(lease.AttemptCount),
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
                        await inbox.RetryAsync(
                            lease.Notification.DeliveryId,
                            ex.Message,
                            NextAttempt(lease.AttemptCount),
                            stoppingToken);
                    }
                    catch (Exception retryException) when (retryException is not OperationCanceledException)
                    {
                        logger.LogError(retryException, "Failed to release webhook inbox lease.");
                    }
                }
            }
        }
    }

    private static DateTimeOffset NextAttempt(int attemptCount)
    {
        var seconds = Math.Min(900, 5 * Math.Pow(2, Math.Min(attemptCount, 7)));
        return DateTimeOffset.UtcNow.AddSeconds(seconds);
    }
}
