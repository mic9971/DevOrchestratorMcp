using System.Net;
using System.Text;
using DevOrchestrator.Application.Services;
using DevOrchestrator.Domain.Projects;
using DevOrchestrator.Domain.Tasks;
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
        var databasePath = Path.Combine(databaseDirectory, $"devorchestrator-http-{Guid.NewGuid():N}.db");

        try
        {
            await using var factory = new TestFactory(databasePath);
            await factory.Services.MigrateDatabaseAsync();

            await using (var seedScope = factory.Services.CreateAsyncScope())
            {
                var db = seedScope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
                var now = DateTimeOffset.UtcNow;
                var project = TargetProject.Create("control-test", "Control Test", "https://github.com/example/control-test", "main", now);
                var task = DevelopmentTask.Create(project.Id, "P7-001", "Control plane task", "Exercise populated UI read models", ["Task detail loads"], null, TaskPriority.High, "mcp:architect", now);
                task.MarkReady("mcp:architect", now);
                db.Projects.Add(project);
                db.DevelopmentTasks.Add(task);
                await db.SaveChangesAsync();
            }

            using var client = factory.CreateClient();

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/readyz")).StatusCode);

            var control = await client.GetAsync("/control/index.html");
            Assert.Equal(HttpStatusCode.OK, control.StatusCode);
            Assert.Contains("DevOrchestrator Control Plane", await control.Content.ReadAsStringAsync());
            Assert.True(control.Headers.TryGetValues("Content-Security-Policy", out var csp));
            Assert.Contains("default-src 'self'", csp.Single());

            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/control/api/dashboard")).StatusCode);

            using var implementerControl = Authenticated(HttpMethod.Get, "/control/api/dashboard", "implementer-key-at-least-24-characters");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(implementerControl)).StatusCode);

            using var auditorControl = Authenticated(HttpMethod.Get, "/control/api/dashboard", "auditor-key-at-least-24-characters");
            var controlDashboard = await client.SendAsync(auditorControl);
            Assert.Equal(HttpStatusCode.OK, controlDashboard.StatusCode);
            Assert.Contains("projects", await controlDashboard.Content.ReadAsStringAsync());

            using var tasksRequest = Authenticated(HttpMethod.Get, "/control/api/tasks?projectKey=control-test&status=Ready", "auditor-key-at-least-24-characters");
            var tasks = await client.SendAsync(tasksRequest);
            Assert.Equal(HttpStatusCode.OK, tasks.StatusCode);
            var tasksBody = await tasks.Content.ReadAsStringAsync();
            Assert.Contains("P7-001", tasksBody);
            Assert.Contains("Ready", tasksBody);

            using var detailRequest = Authenticated(HttpMethod.Get, "/control/api/tasks/control-test/P7-001", "auditor-key-at-least-24-characters");
            var detail = await client.SendAsync(detailRequest);
            Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
            var detailBody = await detail.Content.ReadAsStringAsync();
            Assert.Contains("Task detail loads", detailBody);
            Assert.Contains("High", detailBody);

            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/mcp")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/ops/status")).StatusCode);

            using var implementerOps = Authenticated(HttpMethod.Get, "/ops/status", "implementer-key-at-least-24-characters");
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(implementerOps)).StatusCode);

            using var auditorOps = Authenticated(HttpMethod.Get, "/ops/status", "auditor-key-at-least-24-characters");
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(auditorOps)).StatusCode);

            using var metricsRequest = Authenticated(HttpMethod.Get, "/metrics", "auditor-key-at-least-24-characters");
            var metrics = await client.SendAsync(metricsRequest);
            Assert.Equal(HttpStatusCode.OK, metrics.StatusCode);
            Assert.Contains("devorchestrator_active_workers", await metrics.Content.ReadAsStringAsync());

            await using (var pauseScope = factory.Services.CreateAsyncScope())
            {
                var db = pauseScope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
                var project = await db.Projects.SingleAsync(x => x.Key == "control-test");
                project.Deactivate();
                await db.SaveChangesAsync();
                var leaseService = pauseScope.ServiceProvider.GetRequiredService<ITaskLeaseService>();
                var claim = await leaseService.ClaimNextAsync("control-test", "worker-paused", "mcp:implementer", "feature/paused", CancellationToken.None);
                Assert.True(claim.IsFailure);
                Assert.Equal("task.invalid_state", claim.Error.Code);
                Assert.Contains("paused", claim.Error.Message, StringComparison.OrdinalIgnoreCase);
            }

            using var webhookRequest = new HttpRequestMessage(HttpMethod.Post, "/webhooks/github")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            webhookRequest.Headers.Add("X-Hub-Signature-256", "sha256=invalid");
            webhookRequest.Headers.Add("X-GitHub-Event", "ping");
            webhookRequest.Headers.Add("X-GitHub-Delivery", "integration-test");
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(webhookRequest)).StatusCode);
        }
        finally
        {
            DeleteSqliteFiles(databasePath);
        }
    }

    private static HttpRequestMessage Authenticated(HttpMethod method, string path, string key)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-DevOrchestrator-Key", key);
        return request;
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
                services.AddDbContext<OrchestratorDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
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
