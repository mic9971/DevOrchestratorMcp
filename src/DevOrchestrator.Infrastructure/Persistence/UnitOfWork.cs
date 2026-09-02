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
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var taskEntry = ex.Entries.FirstOrDefault(x => x.Entity is DevelopmentTask);
            if (taskEntry is not null)
            {
                var original = taskEntry.Property(nameof(DevelopmentTask.Revision)).OriginalValue;
                var current = taskEntry.Property(nameof(DevelopmentTask.Revision)).CurrentValue;
                var databaseValues = await taskEntry.GetDatabaseValuesAsync(cancellationToken);
                var database = databaseValues?[nameof(DevelopmentTask.Revision)];

                throw new ConcurrencyConflictException(
                    $"The task was changed by another actor. revision original={original}, current={current}, database={database}. Reload the latest task state and retry.",
                    ex);
            }

            throw new ConcurrencyConflictException(
                "The task was changed by another actor. Reload the latest task state and retry.",
                ex);
        }
    }
}
