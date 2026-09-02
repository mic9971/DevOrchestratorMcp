using DevOrchestrator.Application.Abstractions;
using DevOrchestrator.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace DevOrchestrator.Infrastructure.Persistence;

internal sealed class UnitOfWork(OrchestratorDbContext dbContext) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            IncrementTaskRevisions();
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(
                "The task was changed by another actor. Reload the latest task state and retry.",
                ex);
        }
    }

    private void IncrementTaskRevisions()
    {
        foreach (var entry in dbContext.ChangeTracker
                     .Entries<DevelopmentTask>()
                     .Where(x => x.State == EntityState.Modified))
        {
            var revision = entry.Property<long>("Revision");
            revision.CurrentValue = checked(revision.OriginalValue + 1);
        }
    }
}
