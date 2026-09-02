using System.Net;
using DevOrchestrator.Domain.Identity;
using DevOrchestrator.Infrastructure;
using DevOrchestrator.Infrastructure.Persistence;
using DevOrchestrator.McpServer.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevOrchestrator.Architecture.Tests;

public sealed class IdentityHttpIntegrationTests
{
    [Fact]
    public async Task Dynamic_machine_credentials_are_hashed_revocable_and_never_gain_human_admin_access()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "integration-data");
        Directory.CreateDirectory(directory);
        var databasePath = Path.Combine(directory, $"devorchestrator-identity-{Guid.NewGuid():N}.db");
        const string dynamicSecret = "do_dynamic_auditor_credential_0123456789";

        try
        {
            await using var factory = new IdentityFactory(databasePath);
            await factory.Services.MigrateDatabaseAsync();

            Guid credentialId;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
                var now = DateTime.UtcNow;
                var credential = MachineCredential.Create(
                    "integration-auditor",
                    IdentityEndpointExtensions.Hash(dynamicSecret),
                    "do_dynamic",
                    HumanRoles.Auditor,
                    now,
                    now.AddDays(30),
                    "test");
                credentialId = credential.Id;
                db.MachineCredentials.Add(credential);
                await db.SaveChangesAsync();
            }

            using var client = factory.CreateClient();
            var status = await client.GetAsync("/auth/status");
            Assert.Equal(HttpStatusCode.OK, status.StatusCode);
            var statusBody = await status.Content.ReadAsStringAsync();
            Assert.Contains("\"githubConfigured\":false", statusBody);
            Assert.Contains("\"authenticated\":false", statusBody);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/auth/login")).StatusCode);

            using (var dynamicOps = Authenticated("/ops/status", dynamicSecret))
                Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(dynamicOps)).StatusCode);

            using (var dynamicAdmin = Authenticated("/control/api/users", dynamicSecret))
                Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(dynamicAdmin)).StatusCode);

            await using (var verification = factory.Services.CreateAsyncScope())
            {
                var db = verification.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
                var stored = await db.MachineCredentials.SingleAsync(x => x.Id == credentialId);
                Assert.Equal(IdentityEndpointExtensions.Hash(dynamicSecret), stored.KeyHash);
                Assert.NotEqual(dynamicSecret, stored.KeyHash);
                Assert.NotNull(stored.LastUsedAtUtc);
                stored.Revoke(DateTime.UtcNow);
                await db.SaveChangesAsync();
            }

            using (var revokedOps = Authenticated("/ops/status", dynamicSecret))
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(revokedOps)).StatusCode);

            using (var breakGlass = Authenticated("/ops/status", "auditor-key-at-least-24-characters"))
                Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(breakGlass)).StatusCode);
        }
        finally
        {
            foreach (var suffix in new[] { string.Empty, "-shm", "-wal" })
            {
                var path = databasePath + suffix;
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    private static HttpRequestMessage Authenticated(string path, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-DevOrchestrator-Key", key);
        return request;
    }

    private sealed class IdentityFactory(string databasePath) : WebApplicationFactory<Program>
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
                    ["Identity:GitHub:ClientId"] = "",
                    ["Identity:GitHub:ClientSecret"] = "",
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
}
