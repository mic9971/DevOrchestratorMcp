using DevOrchestrator.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace DevOrchestrator.Infrastructure.Persistence;

internal sealed class GitHubWebhookDeliveryStore(OrchestratorDbContext dbContext)
    : IGitHubWebhookDeliveryStore
{
    private static readonly TimeSpan ProcessingLease = TimeSpan.FromMinutes(5);

    public async Task<bool> TryBeginAsync(
        string deliveryId,
        string eventName,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var leaseExpiresAtUtc = now.Add(ProcessingLease);
        var existing = await dbContext.GitHubWebhookDeliveries
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.DeliveryId == deliveryId, cancellationToken);

        if (existing is not null)
        {
            return await TryReclaimExpiredLeaseAsync(
                deliveryId,
                eventName,
                now,
                leaseExpiresAtUtc,
                existing.CompletedAtUtc,
                existing.LeaseExpiresAtUtc,
                cancellationToken);
        }

        var delivery = new GitHubWebhookDelivery(
            deliveryId,
            eventName,
            now,
            leaseExpiresAtUtc);

        dbContext.GitHubWebhookDeliveries.Add(delivery);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(delivery).State = EntityState.Detached;

            var concurrent = await dbContext.GitHubWebhookDeliveries
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.DeliveryId == deliveryId, cancellationToken);

            if (concurrent is null)
            {
                throw;
            }

            return await TryReclaimExpiredLeaseAsync(
                deliveryId,
                eventName,
                now,
                leaseExpiresAtUtc,
                concurrent.CompletedAtUtc,
                concurrent.LeaseExpiresAtUtc,
                cancellationToken);
        }
    }

    public async Task CompleteAsync(
        string deliveryId,
        CancellationToken cancellationToken)
    {
        var delivery = await dbContext.GitHubWebhookDeliveries
            .SingleOrDefaultAsync(x => x.DeliveryId == deliveryId, cancellationToken);

        if (delivery is null)
        {
            return;
        }

        delivery.Complete(DateTime.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task AbandonAsync(
        string deliveryId,
        CancellationToken cancellationToken)
    {
        var delivery = await dbContext.GitHubWebhookDeliveries
            .SingleOrDefaultAsync(x => x.DeliveryId == deliveryId, cancellationToken);

        if (delivery is null)
        {
            return;
        }

        dbContext.GitHubWebhookDeliveries.Remove(delivery);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> TryReclaimExpiredLeaseAsync(
        string deliveryId,
        string eventName,
        DateTime now,
        DateTime leaseExpiresAtUtc,
        DateTime? completedAtUtc,
        DateTime currentLeaseExpiresAtUtc,
        CancellationToken cancellationToken)
    {
        if (completedAtUtc is not null || currentLeaseExpiresAtUtc > now)
        {
            return false;
        }

        var updated = await dbContext.GitHubWebhookDeliveries
            .Where(x =>
                x.DeliveryId == deliveryId
                && x.CompletedAtUtc == null
                && x.LeaseExpiresAtUtc <= now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.EventName, eventName)
                    .SetProperty(x => x.ReceivedAtUtc, now)
                    .SetProperty(x => x.LeaseExpiresAtUtc, leaseExpiresAtUtc),
                cancellationToken);

        return updated == 1;
    }
}
