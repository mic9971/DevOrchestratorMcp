using DevOrchestrator.Application.Abstractions;
using DevOrchestrator.Domain.Projects;
using DevOrchestrator.Infrastructure;
using DevOrchestrator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevOrchestrator.Architecture.Tests;

public sealed class DatabaseLifecycleTests
{
    [Fact]
    public async Task Legacy_phase3_sqlite_schema_is_adopted_as_initial_migration()
    {
        var file = Path.Combine(Path.GetTempPath(), $"devorchestrator-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={file}";
        try
        {
            var options = new DbContextOptionsBuilder<OrchestratorDbContext>()
                .UseSqlite(connectionString)
                .Options;

            await using (var legacy = new OrchestratorDbContext(options))
            {
                await legacy.Database.EnsureCreatedAsync();
            }

            await using var provider = BuildProvider("sqlite", connectionString);
            await provider.MigrateDatabaseAsync();

            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
            var applied = await db.Database.GetAppliedMigrationsAsync();
            Assert.Contains(DatabaseInitializer.InitialMigrationId, applied);
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        }
        finally
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public async Task Unit_of_work_transaction_rolls_back_all_database_writes_on_failure()
    {
        var file = Path.Combine(Path.GetTempPath(), $"devorchestrator-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={file}";
        try
        {
            await using var provider = BuildProvider("sqlite", connectionString);
            await provider.MigrateDatabaseAsync();

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
                var unit = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    unit.ExecuteInTransactionAsync(async cancellationToken =>
                    {
                        db.Projects.Add(TargetProject.Create(
                            "rollback-test",
                            "Rollback Test",
                            "https://github.com/mic9971/rollback-test",
                            "main",
                            DateTimeOffset.UtcNow));
                        await unit.SaveChangesAsync(cancellationToken);
                        throw new InvalidOperationException("force rollback");
                    }, CancellationToken.None));
            }

            await using (var verification = provider.CreateAsyncScope())
            {
                var db = verification.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
                Assert.Equal(0, await db.Projects.CountAsync());
            }
        }
        finally
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    [Fact]
    public async Task PostgreSql_initial_migration_matches_current_model()
    {
        var connectionString = Environment.GetEnvironmentVariable("DEVORCHESTRATOR_POSTGRES_TEST");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<OrchestratorDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var db = new OrchestratorDbContext(options);
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        Assert.Contains(DatabaseInitializer.InitialMigrationId, await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.True(await db.Database.CanConnectAsync());
    }

    private static ServiceProvider BuildProvider(string provider, string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = provider,
                ["ConnectionStrings:Orchestrator"] = connectionString
            })
            .Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }
}
