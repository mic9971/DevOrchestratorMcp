using DevOrchestrator.Application.Abstractions;
using DevOrchestrator.Application.Contracts;
using Microsoft.EntityFrameworkCore;

namespace DevOrchestrator.Infrastructure.Persistence;

internal sealed class GitHubWebhookInboxStore(OrchestratorDbContext dbContext)
    : IGitHubWebhookInbox
{
    public async Task<bool> EnqueueAsync(
        GitHubWebhookNotification notification,
        CancellationToken cancellationToken)
    {
        if (await dbContext.GitHubWebhookInbox
                .AsNoTracking()
                .AnyAsync(x => x.DeliveryId == notification.DeliveryId, cancellationToken))
        {
            return false;
        }

        dbContext.GitHubWebhookInbox.Add(
            GitHubWebhookInboxItem.Create(notification, DateTimeOffset.UtcNow));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            return false;
        }
    }

    public async Task<GitHubWebhookInboxLease?> TryLeaseNextAsync(
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var candidateId = await dbContext.GitHubWebhookInbox
            .AsNoTracking()
            .Where(x =>
                x.CompletedAtUtc == null &&
                x.NextAttemptAtUtc <= now &&
                (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc <= now))
            .OrderBy(x => x.ReceivedAtUtc)
            .Select(x => x.DeliveryId)
            .FirstOrDefaultAsync(cancellationToken);

        if (candidateId is null)
        {
            return null;
        }

        var leaseUntil = now.Add(leaseDuration);
        var updated = await dbContext.GitHubWebhookInbox
            .Where(x =>
                x.DeliveryId == candidateId &&
                x.CompletedAtUtc == null &&
                x.NextAttemptAtUtc <= now &&
                (x.LeaseExpiresAtUtc == null || x.LeaseExpiresAtUtc <= now))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.LeaseExpiresAtUtc, leaseUntil)
                    .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1),
                cancellationToken);

        if (updated == 0)
        {
            return null;
        }

        var item = await dbContext.GitHubWebhookInbox
            .AsNoTracking()
            .SingleAsync(x => x.DeliveryId == candidateId, cancellationToken);

        return new GitHubWebhookInboxLease(item.ToNotification(), item.AttemptCount);
    }

    public Task CompleteAsync(
        string deliveryId,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken)
        => dbContext.GitHubWebhookInbox
            .Where(x => x.DeliveryId == deliveryId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.CompletedAtUtc, completedAtUtc)
                    .SetProperty(x => x.LeaseExpiresAtUtc, (DateTimeOffset?)null)
                    .SetProperty(x => x.LastError, (string?)null),
                cancellationToken);

    public Task RetryAsync(
        string deliveryId,
        string error,
        DateTimeOffset nextAttemptAtUtc,
        CancellationToken cancellationToken)
        => dbContext.GitHubWebhookInbox
            .Where(x => x.DeliveryId == deliveryId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.LeaseExpiresAtUtc, (DateTimeOffset?)null)
                    .SetProperty(x => x.NextAttemptAtUtc, nextAttemptAtUtc)
                    .SetProperty(x => x.LastError, error.Length <= 4000 ? error : error[..4000]),
                cancellationToken);
}
