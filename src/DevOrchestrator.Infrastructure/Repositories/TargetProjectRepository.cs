using DevOrchestrator.Application.Abstractions;
using DevOrchestrator.Domain.Projects;
using DevOrchestrator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevOrchestrator.Infrastructure.Repositories;

internal sealed class TargetProjectRepository(OrchestratorDbContext dbContext)
    : ITargetProjectRepository
{
    public Task<TargetProject?> GetByKeyAsync(
        string key,
        CancellationToken cancellationToken)
        => dbContext.Projects
            .SingleOrDefaultAsync(x => x.Key == key, cancellationToken);

    public async Task<IReadOnlyList<TargetProject>> ListAsync(
        CancellationToken cancellationToken)
        => await dbContext.Projects
            .AsNoTracking()
            .OrderBy(x => x.Key)
            .ToListAsync(cancellationToken);

    public void Add(TargetProject project)
        => dbContext.Projects.Add(project);
}
