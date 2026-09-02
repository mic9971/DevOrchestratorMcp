using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DevOrchestrator.Application;
using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Application.Services;
using DevOrchestrator.Infrastructure;
using DevOrchestrator.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevOrchestrator.Architecture.Tests;

public sealed class RealGitHubWorkflowTests
{
    [Fact]
    public async Task Real_GitHub_plan_to_review_contract_reaches_done_when_explicitly_enabled()
    {
        var repositoryUrl = Environment.GetEnvironmentVariable("DEVORCHESTRATOR_REAL_GITHUB_E2E_REPOSITORY_URL");
        var token = Environment.GetEnvironmentVariable("DEVORCHESTRATOR_REAL_GITHUB_E2E_TOKEN");
        if (string.IsNullOrWhiteSpace(repositoryUrl) || string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        var coordinates = ParseRepository(repositoryUrl);
        using var github = CreateGitHubClient(token);
        var projectKey = "phase5-real-e2e";
        var taskCode = "P5-E2E";
        var issueNumber = 0;
        var databaseDirectory = Path.Combine(AppContext.BaseDirectory, "real-e2e-data");
        Directory.CreateDirectory(databaseDirectory);
        var databasePath = Path.Combine(databaseDirectory, $"real-e2e-{Guid.NewGuid():N}.db");

        try
        {
            var issueBody = $$"""
                ```devorchestrator-plan
                {
                  "schema": "devorchestrator.plan.v1",
                  "projectKey": "{{projectKey}}",
                  "tasks": [
                    {
                      "code": "{{taskCode}}",
                      "title": "Real GitHub Phase 5 proof",
                      "objective": "Prove the durable GitHub contract end to end.",
                      "acceptanceCriteria": ["Task reaches Done after independent review contract"]
                    }
                  ]
                }
                ```
                """;

            using (var response = await github.PostAsJsonAsync(
                       $"repos/{coordinates.Owner}/{coordinates.Repository}/issues",
                       new
                       {
                           title = $"DevOrchestrator Phase 5 E2E {Guid.NewGuid():N}",
                           body = issueBody
                       }))
            {
                response.EnsureSuccessStatusCode();
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                issueNumber = document.RootElement.GetProperty("number").GetInt32();
            }

            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "sqlite",
                    ["ConnectionStrings:Orchestrator"] = $"Data Source={databasePath}",
                    ["GitHub:Token"] = token
                })
                .Build();

            var services = new ServiceCollection();
            services.AddInfrastructure(configuration, AppContext.BaseDirectory);
            services.AddApplication();
            await using var provider = services.BuildServiceProvider();
            await provider.MigrateDatabaseAsync();

            await using var scope = provider.CreateAsyncScope();
            var projectService = scope.ServiceProvider.GetRequiredService<IProjectService>();
            var bridge = scope.ServiceProvider.GetRequiredService<IGitHubBridgeService>();
            var leaseService = scope.ServiceProvider.GetRequiredService<ITaskLeaseService>();
            var taskService = scope.ServiceProvider.GetRequiredService<ITaskService>();

            var registered = await projectService.RegisterAsync(
                projectKey,
                "Phase 5 Real E2E",
                repositoryUrl,
                "main",
                "e2e:architect",
                CancellationToken.None);
            Assert.True(registered.IsSuccess);

            var imported = await bridge.ImportPlanIssueAsync(projectKey, issueNumber, CancellationToken.None);
            Assert.True(imported.IsSuccess);
            Assert.Equal(1, imported.Value!.Created);

            var claimed = await leaseService.ClaimNextAsync(
                projectKey,
                "e2e-worker",
                "e2e:implementer",
                "phase5-real-e2e",
                CancellationToken.None);
            Assert.True(claimed.IsSuccess);
            Assert.Equal(taskCode, claimed.Value!.Code);

            var evidence = await taskService.AddEvidenceAsync(
                projectKey,
                taskCode,
                new EvidenceInput(
                    "phase5-real-e2e",
                    Environment.GetEnvironmentVariable("GITHUB_SHA") ?? "real-e2e-proof",
                    null,
                    ["tests/DevOrchestrator.Architecture.Tests/RealGitHubWorkflowTests.cs"],
                    ["Real GitHub E2E"],
                    ["dotnet test"],
                    "Opt-in workflow proof"),
                "e2e:implementer",
                CancellationToken.None);
            Assert.True(evidence.IsSuccess);

            var submitted = await taskService.SubmitForReviewAsync(
                projectKey,
                taskCode,
                "e2e:implementer",
                CancellationToken.None);
            Assert.True(submitted.IsSuccess);

            var reviewBody = $$"""
                ```devorchestrator-review
                {
                  "schema": "devorchestrator.review.v1",
                  "taskCode": "{{taskCode}}",
                  "decision": "Pass",
                  "summary": "Real GitHub Phase 5 E2E passed",
                  "findings": [],
                  "completeOnPass": true
                }
                ```
                """;

            using (var response = await github.PostAsJsonAsync(
                       $"repos/{coordinates.Owner}/{coordinates.Repository}/issues/{issueNumber}/comments",
                       new { body = reviewBody }))
            {
                response.EnsureSuccessStatusCode();
            }

            var synced = await bridge.SyncReviewsAsync(projectKey, issueNumber, CancellationToken.None);
            Assert.True(synced.IsSuccess);
            Assert.Equal(1, synced.Value!.Applied);

            var completed = await taskService.GetAsync(projectKey, taskCode, CancellationToken.None);
            Assert.True(completed.IsSuccess);
            Assert.Equal("Done", completed.Value!.Status);
        }
        finally
        {
            if (issueNumber > 0)
            {
                using var close = new HttpRequestMessage(
                    HttpMethod.Patch,
                    $"repos/{coordinates.Owner}/{coordinates.Repository}/issues/{issueNumber}")
                {
                    Content = JsonContent.Create(new { state = "closed", state_reason = "completed" })
                };
                using var _ = await github.SendAsync(close);
            }

            foreach (var suffix in new[] { string.Empty, "-shm", "-wal" })
            {
                var path = databasePath + suffix;
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }

    private static HttpClient CreateGitHubClient(string token)
    {
        var client = new HttpClient { BaseAddress = new Uri("https://api.github.com/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DevOrchestratorMcp-E2E/1.0");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static RepositoryCoordinates ParseRepository(string repositoryUrl)
    {
        var uri = new Uri(repositoryUrl);
        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || parts.Length != 2)
        {
            throw new InvalidOperationException("Real E2E repository must be a github.com owner/repository URL.");
        }

        return new RepositoryCoordinates(parts[0], parts[1]);
    }

    private sealed record RepositoryCoordinates(string Owner, string Repository);
}
