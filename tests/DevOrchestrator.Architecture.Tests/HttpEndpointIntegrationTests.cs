using System.Net;
using System.Text;
using DevOrchestrator.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevOrchestrator.Architecture.Tests;

public sealed class HttpEndpointIntegrationTests
{
    [Fact]
    public async Task Health_readiness_control_plane_mcp_ops_auth_and_webhook_signature_are_enforced_over_http()
    {
        var databaseDirectory = Path.Combine(AppContext.BaseDirectory, "integration-data");
        Directory.CreateDirectory(databaseDirectory);
        var databasePath = Path.Combine(
            databaseDirectory,
            $"devorchestrator-http-{Guid.NewGuid():N}.db");

        try
        {
            await using var factory = new TestFactory(databasePath);
            await factory.Services.MigrateDatabaseAsync();
            using var client = factory.CreateClient();

            var health = await client.GetAsync("/healthz");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);

            var ready = await client.GetAsync("/readyz");
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

            var control = await client.GetAsync("/control/index.html");
            Assert.Equal(HttpStatusCode.OK, control.StatusCode);
            Assert.Contains("DevOrchestrator Control Plane", await control.Content.ReadAsStringAsync());
            Assert.True(control.Headers.TryGetValues("Content-Security-Policy", out var csp));
            Assert.Contains("default-src 'self'", csp.Single());

            var controlUnauthorized = await client.GetAsync("/control/api/dashboard");
            Assert.Equal(HttpStatusCode.Unauthorized, controlUnauthorized.StatusCode);

            using var implementerControl = new HttpRequestMessage(HttpMethod.Get, "/control/api/dashboard");
            implementerControl.Headers.Add("X-DevOrchestrator-Key", "implementer-key-at-least-24-characters");
            var controlForbidden = await client.SendAsync(implementerControl);
            Assert.Equal(HttpStatusCode.Forbidden, controlForbidden.StatusCode);

            using var auditorControl = new HttpRequestMessage(HttpMethod.Get, "/control/api/dashboard");
            auditorControl.Headers.Add("X-DevOrchestrator-Key", "auditor-key-at-least-24-characters");
            var controlDashboard = await client.SendAsync(auditorControl);
            Assert.Equal(HttpStatusCode.OK, controlDashboard.StatusCode);
            Assert.Contains("projects", await controlDashboard.Content.ReadAsStringAsync());

            var mcp = await client.GetAsync("/mcp");
            Assert.Equal(HttpStatusCode.Unauthorized, mcp.StatusCode);

            var opsUnauthorized = await client.GetAsync("/ops/status");
            Assert.Equal(HttpStatusCode.Unauthorized, opsUnauthorized.StatusCode);

            using var implementerOps = new HttpRequestMessage(HttpMethod.Get, "/ops/status");
            implementerOps.Headers.Add("X-DevOrchestrator-Key", "implementer-key-at-least-24-characters");
            var opsForbidden = await client.SendAsync(implementerOps);
            Assert.Equal(HttpStatusCode.Forbidden, opsForbidden.StatusCode);

            using var auditorOps = new HttpRequestMessage(HttpMethod.Get, "/ops/status");
            auditorOps.Headers.Add("X-DevOrchestrator-Key", "auditor-key-at-least-24-characters");
            var ops = await client.SendAsync(auditorOps);
            Assert.Equal(HttpStatusCode.OK, ops.StatusCode);

            using var metricsRequest = new HttpRequestMessage(HttpMethod.Get, "/metrics");
            metricsRequest.Headers.Add("X-DevOrchestrator-Key", "auditor-key-at-least-24-characters");
            var metrics = await client.SendAsync(metricsRequest);
            Assert.Equal(HttpStatusCode.OK, metrics.StatusCode);
            Assert.Contains("devorchestrator_active_workers", await metrics.Content.ReadAsStringAsync());

            using var webhookRequest = new HttpRequestMessage(HttpMethod.Post, "/webhooks/github")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            webhookRequest.Headers.Add("X-Hub-Signature-256", "sha256=invalid");
            webhookRequest.Headers.Add("X-GitHub-Event", "ping");
            webhookRequest.Headers.Add("X-GitHub-Delivery", "integration-test");

            var webhook = await client.SendAsync(webhookRequest);
            Assert.Equal(HttpStatusCode.Unauthorized, webhook.StatusCode);
        }
        finally
        {
            DeleteSqliteFiles(databasePath);
        }
    }

    private sealed class TestFactory(string databasePath) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "sqlite",
                    ["Security:RequireAuthentication"] = "true",
                    ["Security:ArchitectKey"] = "architect-key-at-least-24-characters",
                    ["Security:ImplementerKey"] = "implementer-key-at-least-24-characters",
                    ["Security:AuditorKey"] = "auditor-key-at-least-24-characters",
                    ["GitHub:WebhookSecret"] = "integration-webhook-secret-at-least-24"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<OrchestratorDbContext>>();
                services.RemoveAll<OrchestratorDbContext>();
                services.AddDbContext<OrchestratorDbContext>(options =>
                    options.UseSqlite($"Data Source={databasePath}"));
            });
        }
    }

    private static void DeleteSqliteFiles(string databasePath)
    {
        foreach (var suffix in new[] { string.Empty, "-shm", "-wal" })
        {
            var path = databasePath + suffix;
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
