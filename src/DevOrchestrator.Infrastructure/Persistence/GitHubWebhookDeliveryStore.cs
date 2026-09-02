using DevOrchestrator.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace DevOrchestrator.Infrastructure.Persistence;

internal sealed class GitHubWebhookDeliveryStore(OrchestratorDbContext dbContext)
    : IGitHubWebhookDeliveryStore
{
    public async Task<bool> TryBeginAsync(
        string deliveryId,
        string eventName,
        CancellationToken cancellationToken)
    {
        if (await dbContext.GitHubWebhookDeliveries
                .AsNoTracking()
                .AnyAsync(x => x.DeliveryId == deliveryId, cancellationToken))
        {
            return false;
        }

        var delivery = new GitHubWebhookDelivery(
            deliveryId,
            eventName,
            DateTimeOffset.UtcNow);

        dbContext.GitHubWebhookDeliveries.Add(delivery);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(delivery).State = EntityState.Detached;

            if (await dbContext.GitHubWebhookDeliveries
                    .AsNoTracking()
                    .AnyAsync(x => x.DeliveryId == deliveryId, cancellationToken))
            {
                return false;
            }

            throw;
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

        delivery.Complete(DateTimeOffset.UtcNow);
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
}
