namespace DevOrchestrator.Architecture.Tests;

public sealed class ProductionSecurityGuardTests
{
    [Fact]
    public void Mcp_host_must_enforce_server_side_role_separation_and_actor_binding()
    {
        var root = FindRepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "src", "DevOrchestrator.McpServer", "Program.cs"));
        var taskTools = File.ReadAllText(Path.Combine(root, "src", "DevOrchestrator.McpServer", "Tools", "TaskTools.cs"));
        var reviewTools = File.ReadAllText(Path.Combine(root, "src", "DevOrchestrator.McpServer", "Tools", "ReviewTools.cs"));
        var projectTools = File.ReadAllText(Path.Combine(root, "src", "DevOrchestrator.McpServer", "Tools", "ProjectTools.cs"));

        Assert.Contains("UseMiddleware<McpApiKeyMiddleware>", program, StringComparison.Ordinal);
        Assert.Contains("UseRateLimiter", program, StringComparison.Ordinal);
        Assert.Contains("AddHostedService<GitHubWebhookBackgroundService>", program, StringComparison.Ordinal);
        Assert.Contains("RequireAndResolveActor(actor, McpCallerRole.Architect)", projectTools, StringComparison.Ordinal);
        Assert.Contains("RequireAndResolveActor(actor, McpCallerRole.Implementer)", taskTools, StringComparison.Ordinal);
        Assert.Contains("RequireAndResolveActor(actor, McpCallerRole.Auditor)", reviewTools, StringComparison.Ordinal);
        Assert.Contains("RequireAndResolveActor(actor, McpCallerRole.Auditor", taskTools, StringComparison.Ordinal);
        Assert.Contains("task_claim_next", taskTools, StringComparison.Ordinal);
        Assert.Contains("task_heartbeat", taskTools, StringComparison.Ordinal);
    }

    [Fact]
    public void Webhook_endpoint_must_verify_signature_before_durable_enqueue()
    {
        var root = FindRepositoryRoot();
        var endpoint = File.ReadAllText(Path.Combine(root, "src", "DevOrchestrator.McpServer", "Webhooks", "GitHubWebhookEndpoint.cs"));
        var verificationIndex = endpoint.IndexOf("signatureVerifier.IsValid", StringComparison.Ordinal);
        var enqueueIndex = endpoint.IndexOf("inbox.EnqueueAsync", StringComparison.Ordinal);
        Assert.True(verificationIndex >= 0, "Webhook endpoint must verify the GitHub signature.");
        Assert.True(enqueueIndex > verificationIndex, "Signature verification must happen before durable inbox enqueue.");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "DevOrchestratorMcp.sln"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate DevOrchestratorMcp.sln.");
    }
}
