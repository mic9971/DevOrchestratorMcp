using DevOrchestrator.Application.Abstractions;
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
        var connectionString =
            configuration.GetConnectionString("Orchestrator")
            ?? "Data Source=data/dev-orchestrator.db";

        services.AddDbContext<OrchestratorDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<ITargetProjectRepository, TargetProjectRepository>();
        services.AddScoped<IDevelopmentTaskRepository, DevelopmentTaskRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
