using DevOrchestrator.Application.Abstractions;
using DevOrchestrator.Domain.Tasks;
using DevOrchestrator.Infrastructure;
using DevOrchestrator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevOrchestrator.Architecture.Tests;

public sealed class TaskConcurrencyTests
{
    [Fact]
    public async Task Stale_task_mutation_is_rejected_instead_of_overwriting_first_claim()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"dev-orchestrator-concurrency-{Guid.NewGuid():N}.db");

        try
        {
            await using var provider = BuildProvider(databasePath);
            await provider.InitializeDatabaseAsync();

            var now = DateTimeOffset.UtcNow;
            var task = DevelopmentTask.Create(
                Guid.NewGuid(),
                "CONC-001",
                "Concurrency claim",
                "Prove one task cannot be claimed by two implementers.",
                ["Second stale mutation is rejected."],
                [],
                TaskPriority.Normal,
                "architect",
                now);
            task.MarkReady("architect", now.AddSeconds(1));

            await using (var seedScope = provider.CreateAsyncScope())
            {
                var db = seedScope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
                db.DevelopmentTasks.Add(task);
                await db.SaveChangesAsync();
            }

            await using var firstScope = provider.CreateAsyncScope();
            await using var secondScope = provider.CreateAsyncScope();
            var firstDb = firstScope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
            var secondDb = secondScope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
            var firstUnitOfWork = firstScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var secondUnitOfWork = secondScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var first = await firstDb.DevelopmentTasks.SingleAsync(x => x.Code == "CONC-001");
            var second = await secondDb.DevelopmentTasks.SingleAsync(x => x.Code == "CONC-001");

            first.Start("codex-a", "codex/conc-a", now.AddSeconds(2));
            second.Start("codex-b", "codex/conc-b", now.AddSeconds(3));

            await firstUnitOfWork.SaveChangesAsync(CancellationToken.None);

            var exception = await Assert.ThrowsAsync<ConcurrencyConflictException>(
                () => secondUnitOfWork.SaveChangesAsync(CancellationToken.None));

            Assert.Contains("changed by another actor", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteSqliteFiles(databasePath);
        }
    }

    private static ServiceProvider BuildProvider(string databasePath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "sqlite",
                ["ConnectionStrings:Orchestrator"] = $"Data Source={databasePath}"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    private static void DeleteSqliteFiles(string databasePath)
    {
        foreach (var suffix in new[] { string.Empty, "-shm", "-wal" })
        {
            var path = databasePath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
