using DevOrchestrator.Application.Abstractions;
using DevOrchestrator.Infrastructure.GitHub;
using DevOrchestrator.Infrastructure.Persistence;
using DevOrchestrator.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevOrchestrator.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"]?.Trim().ToLowerInvariant() ?? "sqlite";
        var connectionString = configuration.GetConnectionString("Orchestrator");

        services.AddDbContext<OrchestratorDbContext>(options =>
        {
            if (provider == "postgres")
            {
                options.UseNpgsql(
                    connectionString
                    ?? "Host=localhost;Port=5432;Database=devorchestrator;Username=devorchestrator;Password=devorchestrator");
                return;
            }

            if (provider != "sqlite")
            {
                throw new InvalidOperationException(
                    $"Unsupported Database:Provider '{provider}'. Use 'sqlite' or 'postgres'.");
            }

            options.UseSqlite(connectionString ?? "Data Source=data/dev-orchestrator.db");
        });

        services.AddScoped<ITargetProjectRepository, TargetProjectRepository>();
        services.AddScoped<IDevelopmentTaskRepository, DevelopmentTaskRepository>();
        services.AddScoped<ITaskReadRepository, TaskReadRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IGitHubWebhookDeliveryStore, GitHubWebhookDeliveryStore>();

        services.AddSingleton<HttpClient>();
        services.AddSingleton<IGitHubBridgeClient, GitHubBridgeClient>();

        return services;
    }
}
