using DevOrchestrator.Application.Abstractions;
using DevOrchestrator.Infrastructure;
using DevOrchestrator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevOrchestrator.Architecture.Tests;

public sealed class WebhookDeliveryStoreTests
{
    [Fact]
    public async Task Active_delivery_is_deduplicated_and_expired_lease_can_be_reclaimed()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"dev-orchestrator-webhook-{Guid.NewGuid():N}.db");

        try
        {
            await using var provider = BuildProvider(databasePath);
            await provider.MigrateDatabaseAsync();

            await using var scope = provider.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IGitHubWebhookDeliveryStore>();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();

            Assert.True(await store.TryBeginAsync("delivery-lease", "issues", CancellationToken.None));
            Assert.False(await store.TryBeginAsync("delivery-lease", "issues", CancellationToken.None));

            var expired = DateTime.UtcNow.AddMinutes(-1);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE github_webhook_deliveries SET LeaseExpiresAtUtc = {expired} WHERE DeliveryId = {"delivery-lease"}");

            Assert.True(await store.TryBeginAsync("delivery-lease", "issues", CancellationToken.None));
        }
        finally
        {
            DeleteSqliteFiles(databasePath);
        }
    }

    [Fact]
    public async Task Completed_delivery_is_never_reclaimed()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"dev-orchestrator-webhook-{Guid.NewGuid():N}.db");

        try
        {
            await using var provider = BuildProvider(databasePath);
            await provider.MigrateDatabaseAsync();

            await using var scope = provider.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<IGitHubWebhookDeliveryStore>();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();

            Assert.True(await store.TryBeginAsync("delivery-complete", "issue_comment", CancellationToken.None));
            await store.CompleteAsync("delivery-complete", CancellationToken.None);

            var expired = DateTime.UtcNow.AddMinutes(-1);
            await dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE github_webhook_deliveries SET LeaseExpiresAtUtc = {expired} WHERE DeliveryId = {"delivery-complete"}");

            Assert.False(await store.TryBeginAsync("delivery-complete", "issue_comment", CancellationToken.None));
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
