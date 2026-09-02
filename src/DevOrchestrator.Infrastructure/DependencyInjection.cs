using DevOrchestrator.Application.Abstractions;
using DevOrchestrator.Infrastructure.GitHub;
using DevOrchestrator.Infrastructure.Persistence;
using DevOrchestrator.Infrastructure.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevOrchestrator.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string? contentRootPath = null)
    {
        var provider = configuration["Database:Provider"]?.Trim().ToLowerInvariant() ?? "sqlite";
        var connectionString = configuration.GetConnectionString("Orchestrator");

        services.AddDbContext<OrchestratorDbContext>(options =>
        {
            if (provider == "postgres")
            {
                options.UseNpgsql(connectionString
                    ?? "Host=localhost;Port=5432;Database=devorchestrator;Username=devorchestrator;Password=devorchestrator");
                return;
            }

            if (provider != "sqlite")
            {
                throw new InvalidOperationException(
                    $"Unsupported Database:Provider '{provider}'. Use 'sqlite' or 'postgres'.");
            }

            options.UseSqlite(ResolveSqliteConnectionString(
                connectionString ?? "Data Source=data/dev-orchestrator.db",
                contentRootPath));
        });

        services.AddScoped<ITargetProjectRepository, TargetProjectRepository>();
        services.AddScoped<IDevelopmentTaskRepository, DevelopmentTaskRepository>();
        services.AddScoped<ITaskReadRepository, TaskReadRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IGitHubWebhookDeliveryStore, GitHubWebhookDeliveryStore>();
        services.AddScoped<IGitHubWebhookInbox, GitHubWebhookInboxStore>();

        services.AddSingleton<HttpClient>();
        services.AddSingleton<IGitHubAccessTokenProvider, GitHubAccessTokenProvider>();
        services.AddSingleton<IGitHubBridgeClient, GitHubBridgeClient>();

        return services;
    }

    private static string ResolveSqliteConnectionString(string connectionString, string? contentRootPath)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource)
            || builder.DataSource == ":memory:"
            || Path.IsPathRooted(builder.DataSource))
        {
            return builder.ToString();
        }

        var root = string.IsNullOrWhiteSpace(contentRootPath) ? Directory.GetCurrentDirectory() : contentRootPath;
        builder.DataSource = Path.GetFullPath(Path.Combine(root, builder.DataSource));
        return builder.ToString();
    }
}
