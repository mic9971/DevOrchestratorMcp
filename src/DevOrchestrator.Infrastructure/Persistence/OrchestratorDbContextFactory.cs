using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DevOrchestrator.Infrastructure.Persistence;

public sealed class OrchestratorDbContextFactory : IDesignTimeDbContextFactory<OrchestratorDbContext>
{
    public OrchestratorDbContext CreateDbContext(string[] args)
    {
        var provider = Environment.GetEnvironmentVariable("DEVORCHESTRATOR_DESIGN_PROVIDER")?.Trim().ToLowerInvariant() ?? "sqlite";
        var connection = Environment.GetEnvironmentVariable("DEVORCHESTRATOR_DESIGN_CONNECTION");
        var options = new DbContextOptionsBuilder<OrchestratorDbContext>();

        if (provider == "postgres")
        {
            options.UseNpgsql(connection ?? "Host=localhost;Port=5432;Database=devorchestrator;Username=devorchestrator;Password=devorchestrator");
        }
        else
        {
            options.UseSqlite(connection ?? "Data Source=dev-orchestrator-design.db");
        }

        return new OrchestratorDbContext(options.Options);
    }
}
