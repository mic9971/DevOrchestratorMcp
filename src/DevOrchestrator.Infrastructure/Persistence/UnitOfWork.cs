using DevOrchestrator.Application.Abstractions;

namespace DevOrchestrator.Infrastructure.Persistence;

internal sealed class UnitOfWork(OrchestratorDbContext dbContext) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => dbContext.SaveChangesAsync(cancellationToken);
}
