using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevOrchestrator.Infrastructure.Persistence;

public static class DatabaseInitializer
{
    public static async Task InitializeDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await EnsurePhase3WebhookSchemaAsync(dbContext, cancellationToken);
    }

    private static async Task EnsurePhase3WebhookSchemaAsync(
        OrchestratorDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var provider = dbContext.Database.ProviderName ?? string.Empty;

        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS github_webhook_deliveries (
                    "DeliveryId" character varying(120) NOT NULL,
                    "EventName" character varying(80) NOT NULL,
                    "ReceivedAtUtc" timestamp with time zone NOT NULL,
                    "CompletedAtUtc" timestamp with time zone NULL,
                    CONSTRAINT "PK_github_webhook_deliveries" PRIMARY KEY ("DeliveryId")
                );
                CREATE INDEX IF NOT EXISTS "IX_github_webhook_deliveries_ReceivedAtUtc"
                    ON github_webhook_deliveries ("ReceivedAtUtc");
                """,
                cancellationToken);
            return;
        }

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS github_webhook_deliveries (
                DeliveryId TEXT NOT NULL CONSTRAINT PK_github_webhook_deliveries PRIMARY KEY,
                EventName TEXT NOT NULL,
                ReceivedAtUtc TEXT NOT NULL,
                CompletedAtUtc TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_github_webhook_deliveries_ReceivedAtUtc
                ON github_webhook_deliveries (ReceivedAtUtc);
            """,
            cancellationToken);
    }
}
