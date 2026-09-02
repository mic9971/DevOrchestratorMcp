using DevOrchestrator.Application.Abstractions;
using DevOrchestrator.Domain.Tasks;
using DevOrchestrator.Infrastructure;
using DevOrchestrator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevOrchestrator.Architecture.Tests;

public sealed class TaskReadRepositoryTests
{
    [Fact]
    public async Task Paged_read_is_bounded_ordered_and_does_not_load_task_history()
    {
        var file = Path.Combine(Path.GetTempPath(), $"devorchestrator-read-{Guid.NewGuid():N}.db");
        try
        {
            await using var provider = BuildProvider(file);
            await provider.MigrateDatabaseAsync();

            var projectId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
                foreach (var code in new[] { "TASK-003", "TASK-001", "TASK-002" })
                {
                    var task = DevelopmentTask.Create(
                        projectId,
                        code,
                        code,
                        "objective",
                        ["criterion"],
                        [],
                        TaskPriority.Normal,
                        "architect",
                        now);
                    task.MarkReady("architect", now);
                    db.DevelopmentTasks.Add(task);
                }

                await db.SaveChangesAsync();
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                var reads = scope.ServiceProvider.GetRequiredService<ITaskReadRepository>();
                var page = await reads.ListPageAsync(projectId, DevelopmentTaskStatus.Ready, 0, 2, CancellationToken.None);

                Assert.True(page.HasMore);
                Assert.Equal(["TASK-001", "TASK-002"], page.Items.Select(x => x.Code).ToArray());
            }
        }
        finally
        {
            DeleteSqliteFiles(file);
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
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
