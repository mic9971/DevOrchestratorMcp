using DevOrchestrator.Application.Abstractions;
using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Infrastructure;
using DevOrchestrator.Infrastructure.Persistence;
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
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "sqlite",
                ["ConnectionStrings:Orchestrator"] = $"Data Source={databasePath}"
            })
            .Build();

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
            foreach (var suffix in new[] { string.Empty, "-shm", "-wal" })
            {
                var path = databasePath + suffix;
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    private static ServiceProvider BuildProvider(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration, AppContext.BaseDirectory);
        return services.BuildServiceProvider();
    }
}
