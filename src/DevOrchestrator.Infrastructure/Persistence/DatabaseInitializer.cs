using System.Data;
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
        await EnsureTaskRevisionSchemaAsync(dbContext, cancellationToken);
        await EnsurePhase3WebhookSchemaAsync(dbContext, cancellationToken);
    }

    private static async Task EnsureTaskRevisionSchemaAsync(
        OrchestratorDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var provider = dbContext.Database.ProviderName ?? string.Empty;

        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                """
                ALTER TABLE tasks
                    ADD COLUMN IF NOT EXISTS "Revision" bigint NOT NULL DEFAULT 0;
                """,
                cancellationToken);
            return;
        }

        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var check = connection.CreateCommand();
            check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('tasks') WHERE name = 'Revision';";
            var exists = Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken)) > 0;
            if (exists)
            {
                return;
            }

            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE tasks ADD COLUMN Revision INTEGER NOT NULL DEFAULT 0;";
            await alter.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
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
                    "LeaseExpiresAtUtc" timestamp with time zone NOT NULL,
                    "CompletedAtUtc" timestamp with time zone NULL,
                    CONSTRAINT "PK_github_webhook_deliveries" PRIMARY KEY ("DeliveryId")
                );
                CREATE INDEX IF NOT EXISTS "IX_github_webhook_deliveries_CompletedAtUtc_LeaseExpiresAtUtc"
                    ON github_webhook_deliveries ("CompletedAtUtc", "LeaseExpiresAtUtc");
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
                LeaseExpiresAtUtc TEXT NOT NULL,
                CompletedAtUtc TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_github_webhook_deliveries_CompletedAtUtc_LeaseExpiresAtUtc
                ON github_webhook_deliveries (CompletedAtUtc, LeaseExpiresAtUtc);
            """,
            cancellationToken);
    }
}
