using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevOrchestrator.Infrastructure.Persistence;

public sealed record DatabaseReadiness(bool Ready, string? Reason, IReadOnlyList<string> PendingMigrations);

public static class DatabaseInitializer
{
    public const string InitialMigrationId = "202609020001_InitialProductionSchema";
    private const string EfProductVersion = "8.0.30";

    public static async Task MigrateDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
        await AdoptPhase3SchemaIfCompatibleAsync(dbContext, cancellationToken);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    public static async Task<DatabaseReadiness> GetDatabaseReadinessAsync(
        this OrchestratorDbContext dbContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await dbContext.Database.CanConnectAsync(cancellationToken))
            {
                return new DatabaseReadiness(false, "database_unreachable", []);
            }

            var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
            return pending.Length == 0
                ? new DatabaseReadiness(true, null, pending)
                : new DatabaseReadiness(false, "pending_migrations", pending);
        }
        catch (Exception ex)
        {
            return new DatabaseReadiness(false, ex.GetType().Name, []);
        }
    }

    private static async Task AdoptPhase3SchemaIfCompatibleAsync(
        OrchestratorDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.Database.CanConnectAsync(cancellationToken))
        {
            return;
        }

        var applied = (await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
        if (applied.Length > 0)
        {
            return;
        }

        var requiredTables = new[]
        {
            "projects",
            "tasks",
            "task_acceptance_criteria",
            "task_dependencies",
            "task_evidence",
            "task_reviews",
            "task_events",
            "github_webhook_deliveries"
        };

        var connection = dbContext.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var existing = 0;
            foreach (var table in requiredTables)
            {
                if (await TableExistsAsync(connection, dbContext.Database.ProviderName, table, cancellationToken))
                {
                    existing++;
                }
            }

            if (existing == 0)
            {
                return;
            }

            var revisionExists = await ColumnExistsAsync(
                connection,
                dbContext.Database.ProviderName,
                "tasks",
                "Revision",
                cancellationToken);

            if (existing != requiredTables.Length || !revisionExists)
            {
                throw new InvalidOperationException(
                    "A partial legacy DevOrchestrator schema was detected. Back it up and migrate it explicitly before starting Phase 4.");
            }

            await CreateMigrationHistoryAsync(connection, dbContext.Database.ProviderName, cancellationToken);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static async Task<bool> TableExistsAsync(
        System.Data.Common.DbConnection connection,
        string? providerName,
        string table,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = IsPostgres(providerName)
            ? $"SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = '{table}';"
            : $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{table}';";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<bool> ColumnExistsAsync(
        System.Data.Common.DbConnection connection,
        string? providerName,
        string table,
        string column,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = IsPostgres(providerName)
            ? $"SELECT COUNT(*) FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = '{table}' AND column_name = '{column}';"
            : $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = '{column}';";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task CreateMigrationHistoryAsync(
        System.Data.Common.DbConnection connection,
        string? providerName,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = IsPostgres(providerName)
            ? $$"""
              CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                  "MigrationId" character varying(150) NOT NULL,
                  "ProductVersion" character varying(32) NOT NULL,
                  CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
              );
              INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
              VALUES ('{{InitialMigrationId}}', '{{EfProductVersion}}')
              ON CONFLICT ("MigrationId") DO NOTHING;
              """
            : $$"""
              CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                  "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
                  "ProductVersion" TEXT NOT NULL
              );
              INSERT OR IGNORE INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
              VALUES ('{{InitialMigrationId}}', '{{EfProductVersion}}');
              """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool IsPostgres(string? providerName)
        => providerName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
}
