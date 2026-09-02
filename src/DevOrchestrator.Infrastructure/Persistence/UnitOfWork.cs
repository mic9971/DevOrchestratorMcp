using DevOrchestrator.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace DevOrchestrator.Infrastructure.Persistence;

internal sealed class UnitOfWork(OrchestratorDbContext dbContext) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(
                "The task was changed by another actor. Reload the latest task state and retry.",
                ex);
        }
    }
}
