using System.Reflection;
using DevOrchestrator.Domain.Tasks;
using DevOrchestrator.McpServer.Tools;

namespace DevOrchestrator.Architecture.Tests;

public sealed class LayerDependencyTests
{
    [Fact]
    public void Domain_does_not_reference_outer_layers()
    {
        var references = ReferencesOf(typeof(DevelopmentTask).Assembly);

        Assert.DoesNotContain("DevOrchestrator.Application", references);
        Assert.DoesNotContain("DevOrchestrator.Infrastructure", references);
        Assert.DoesNotContain("DevOrchestrator.McpServer", references);
        Assert.DoesNotContain("ModelContextProtocol", references);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", references);
    }

    [Fact]
    public void Application_does_not_reference_infrastructure_mcp_or_http_transport()
    {
        var references = ReferencesOf(typeof(DevOrchestrator.Application.DependencyInjection).Assembly);

        Assert.DoesNotContain("DevOrchestrator.Infrastructure", references);
        Assert.DoesNotContain("DevOrchestrator.McpServer", references);
        Assert.DoesNotContain("ModelContextProtocol", references);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", references);
        Assert.DoesNotContain("System.Net.Http", references);
    }

    [Fact]
    public void Infrastructure_does_not_reference_mcp_host()
    {
        var references = ReferencesOf(typeof(DevOrchestrator.Infrastructure.DependencyInjection).Assembly);

        Assert.DoesNotContain("DevOrchestrator.McpServer", references);
        Assert.DoesNotContain("ModelContextProtocol", references);
    }

    [Fact]
    public void Mcp_host_is_the_only_layer_that_references_mcp_sdk()
    {
        var references = ReferencesOf(typeof(ProjectTools).Assembly);

        Assert.Contains(references, x => x.StartsWith("ModelContextProtocol", StringComparison.Ordinal));
    }

    private static string[] ReferencesOf(Assembly assembly)
        => assembly.GetReferencedAssemblies()
            .Select(x => x.Name ?? string.Empty)
            .ToArray();
}
