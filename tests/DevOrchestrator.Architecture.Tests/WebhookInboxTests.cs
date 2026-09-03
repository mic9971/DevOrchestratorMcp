using DevOrchestrator.Application.Abstractions;
using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Infrastructure;
using DevOrchestrator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevOrchestrator.Architecture.Tests;

public sealed class WebhookInboxTests
{
    [Fact]
    public async Task Inbox_is_idempotent_leased_retryable_and_completed_durably()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "inbox-test-data");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, $"inbox-{Guid.NewGuid():N}.db");
        var configuration = CreateConfiguration(databasePath);

        await using var provider = BuildProvider(configuration);
        try
        {
            await provider.MigrateDatabaseAsync();
            var notification = new GitHubWebhookNotification(
                "delivery-1", "issues", "opened", "https://github.com/mic9971/sample", 42);

            await using (var scope = provider.CreateAsyncScope())
            {
                var inbox = scope.ServiceProvider.GetRequiredService<IGitHubWebhookInbox>();
                Assert.True(await inbox.EnqueueAsync(notification, CancellationToken.None));
                Assert.False(await inbox.EnqueueAsync(notification, CancellationToken.None));
            }

            var now = DateTimeOffset.UtcNow.AddSeconds(1);
            await using (var scope = provider.CreateAsyncScope())
            {
                var inbox = scope.ServiceProvider.GetRequiredService<IGitHubWebhookInbox>();
                var lease = await inbox.TryLeaseNextAsync(now, TimeSpan.FromMinutes(1), CancellationToken.None);
                Assert.NotNull(lease);
                Assert.Equal(1, lease!.AttemptCount);
                Assert.Equal("delivery-1", lease.Notification.DeliveryId);
                Assert.Null(await inbox.TryLeaseNextAsync(now.AddSeconds(10), TimeSpan.FromMinutes(1), CancellationToken.None));
                await inbox.RetryAsync("delivery-1", "temporary", now.AddMinutes(2), CancellationToken.None);
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                var inbox = scope.ServiceProvider.GetRequiredService<IGitHubWebhookInbox>();
                Assert.Null(await inbox.TryLeaseNextAsync(now.AddMinutes(1), TimeSpan.FromMinutes(1), CancellationToken.None));
                var retryLease = await inbox.TryLeaseNextAsync(now.AddMinutes(3), TimeSpan.FromMinutes(1), CancellationToken.None);
                Assert.NotNull(retryLease);
                Assert.Equal(2, retryLease!.AttemptCount);
                await inbox.CompleteAsync("delivery-1", now.AddMinutes(3), CancellationToken.None);
                Assert.Null(await inbox.TryLeaseNextAsync(now.AddMinutes(5), TimeSpan.FromMinutes(1), CancellationToken.None));
            }
        }
        finally
        {
            DeleteSqliteFiles(databasePath);
        }
    }

    [Fact]
    public async Task Dead_lettered_delivery_is_not_leased_until_operator_requeues_it()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "inbox-test-data");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, $"inbox-dlq-{Guid.NewGuid():N}.db");
        var configuration = CreateConfiguration(databasePath);

        await using var provider = BuildProvider(configuration);
        try
        {
            await provider.MigrateDatabaseAsync();
            var now = DateTimeOffset.UtcNow;
            var notification = new GitHubWebhookNotification(
                "delivery-dlq", "issues", "opened", "https://github.com/mic9971/sample", 43);

            await using (var scope = provider.CreateAsyncScope())
            {
                var inbox = scope.ServiceProvider.GetRequiredService<IGitHubWebhookInbox>();
                Assert.True(await inbox.EnqueueAsync(notification, CancellationToken.None));
                var lease = await inbox.TryLeaseNextAsync(now.AddSeconds(1), TimeSpan.FromMinutes(1), CancellationToken.None);
                Assert.NotNull(lease);
                await inbox.DeadLetterAsync("delivery-dlq", "permanent failure", now.AddSeconds(2), CancellationToken.None);
                Assert.Null(await inbox.TryLeaseNextAsync(now.AddHours(1), TimeSpan.FromMinutes(1), CancellationToken.None));
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
                var item = await db.GitHubWebhookInbox.SingleAsync(x => x.DeliveryId == "delivery-dlq");
                Assert.NotNull(item.DeadLetteredAtUtc);
                Assert.Equal("permanent failure", item.LastError);

                item.Requeue(now.AddMinutes(1));
                await db.SaveChangesAsync();
            }

            await using (var scope = provider.CreateAsyncScope())
            {
                var inbox = scope.ServiceProvider.GetRequiredService<IGitHubWebhookInbox>();
                var replayLease = await inbox.TryLeaseNextAsync(now.AddMinutes(2), TimeSpan.FromMinutes(1), CancellationToken.None);
                Assert.NotNull(replayLease);
                Assert.Equal("delivery-dlq", replayLease!.Notification.DeliveryId);
                Assert.Equal(2, replayLease.AttemptCount);
            }
        }
        finally
        {
            DeleteSqliteFiles(databasePath);
        }
    }

    private static IConfiguration CreateConfiguration(string databasePath)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "sqlite",
                ["ConnectionStrings:Orchestrator"] = $"Data Source={databasePath}"
            })
            .Build();

    private static ServiceProvider BuildProvider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration, AppContext.BaseDirectory);
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
