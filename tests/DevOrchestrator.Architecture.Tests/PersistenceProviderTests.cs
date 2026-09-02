using DevOrchestrator.Infrastructure;
using DevOrchestrator.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevOrchestrator.Architecture.Tests;

public sealed class PersistenceProviderTests
{
    [Fact]
    public void Sqlite_remains_the_local_default_provider()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "sqlite",
            ["ConnectionStrings:Orchestrator"] = "Data Source=:memory:"
        });

        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();

        Assert.Contains("Sqlite", dbContext.Database.ProviderName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostgreSql_can_be_selected_for_shared_deployment()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "postgres",
            ["ConnectionStrings:Orchestrator"] = "Host=localhost;Database=devorchestrator;Username=devorchestrator;Password=test"
        });

        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();

        Assert.Contains("Npgsql", dbContext.Database.ProviderName, StringComparison.OrdinalIgnoreCase);
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }
}
