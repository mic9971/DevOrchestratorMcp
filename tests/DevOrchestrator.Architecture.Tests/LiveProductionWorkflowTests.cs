using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DevOrchestrator.Application.Contracts;
using DevOrchestrator.McpServer.Tools;
using ModelContextProtocol.Client;

namespace DevOrchestrator.Architecture.Tests;

public sealed class LiveProductionWorkflowTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public async Task Live_webhook_mcp_github_pr_and_review_reach_done_when_explicitly_enabled()
    {
        var config = LiveConfig.TryLoad();
        if (config is null) return;

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var cancellationToken = timeout.Token;
        var coordinates = ParseRepository(config.RepositoryUrl);
        using var github = CreateGitHubClient(config.GitHubToken);
        await using var architect = await CreateMcpClientAsync(config.BaseUrl, config.ArchitectKey, cancellationToken);
        await using var implementer = await CreateMcpClientAsync(config.BaseUrl, config.ImplementerKey, cancellationToken);
        using var auditorHttp = CreateOrchestratorHttpClient(config.BaseUrl, config.AuditorKey);

        var defaultBranch = await GetDefaultBranchAsync(github, coordinates, cancellationToken);
        var projectKey = await EnsureProjectAsync(
            architect,
            auditorHttp,
            config.ProjectKey,
            config.RepositoryUrl,
            defaultBranch,
            cancellationToken);
        await EnsureNoClaimableBacklogAsync(architect, projectKey, cancellationToken);

        var suffix = UniqueSuffix();
        var taskCode = $"P9-E2E-{suffix}".ToUpperInvariant();
        var workerId = $"phase9-e2e-{suffix}";
        var branch = $"devorchestrator/e2e/{suffix.ToLowerInvariant()}";
        var issueNumber = 0;
        var pullRequestNumber = 0;

        try
        {
            issueNumber = await CreatePlanIssueAsync(
                github,
                coordinates,
                projectKey,
                $"DevOrchestrator Phase 9 live E2E {suffix}",
                [new PlanTask(taskCode, "Live GitHub pull-request proof", "Prove automatic webhook import, remote MCP execution, real GitHub evidence and automatic review sync.")],
                cancellationToken);

            var ready = await WaitForTaskAsync(
                architect,
                projectKey,
                taskCode,
                task => task.Status == "Ready",
                "GitHub issue webhook did not import the task as Ready.",
                cancellationToken);
            Assert.Equal(taskCode, ready.Code);

            var claimed = await CallAsync<TaskDto>(implementer, "task_claim_next", new Dictionary<string, object?>
            {
                ["projectKey"] = projectKey,
                ["workerId"] = workerId,
                ["branch"] = branch,
                ["actor"] = "phase9:implementer"
            }, cancellationToken);
            AssertSuccess(claimed);
            Assert.NotNull(claimed.Data);
            Assert.Equal(taskCode, claimed.Data!.Code);
            Assert.Equal(workerId, claimed.Data.LeaseOwner);

            var heartbeat = await CallAsync<TaskDto>(implementer, "task_heartbeat", new Dictionary<string, object?>
            {
                ["projectKey"] = projectKey,
                ["taskCode"] = taskCode,
                ["workerId"] = workerId,
                ["actor"] = "phase9:implementer"
            }, cancellationToken);
            AssertSuccess(heartbeat);

            var gitEvidence = await CreateBranchCommitAndPullRequestAsync(
                github,
                coordinates,
                defaultBranch,
                branch,
                suffix,
                issueNumber,
                cancellationToken);
            pullRequestNumber = gitEvidence.PullRequestNumber;

            var evidence = await CallAsync<TaskDto>(implementer, "task_add_evidence", new Dictionary<string, object?>
            {
                ["projectKey"] = projectKey,
                ["taskCode"] = taskCode,
                ["branch"] = branch,
                ["commitSha"] = gitEvidence.CommitSha,
                ["pullRequestUrl"] = gitEvidence.PullRequestUrl,
                ["filesChanged"] = new[] { gitEvidence.MarkerPath },
                ["tests"] = new[] { "Phase 9 live MCP/GitHub lifecycle" },
                ["commands"] = new[] { "synthetic live worker marker commit" },
                ["notes"] = "Synthetic MCP worker proof; no external Codex executable is claimed by this test.",
                ["actor"] = "phase9:implementer"
            }, cancellationToken);
            AssertSuccess(evidence);

            var submitted = await CallAsync<TaskDto>(implementer, "task_submit_review", new Dictionary<string, object?>
            {
                ["projectKey"] = projectKey,
                ["taskCode"] = taskCode,
                ["actor"] = "phase9:implementer"
            }, cancellationToken);
            AssertSuccess(submitted);
            Assert.Equal("ReadyForReview", submitted.Data!.Status);

            await PostReviewCommentAsync(github, coordinates, issueNumber, taskCode, cancellationToken);

            var completed = await WaitForTaskAsync(
                architect,
                projectKey,
                taskCode,
                task => task.Status == "Done",
                "GitHub review comment webhook did not transition the task to Done.",
                cancellationToken);
            Assert.Equal(gitEvidence.CommitSha, completed.LastCommitSha);
            Assert.Equal(gitEvidence.PullRequestUrl, completed.PullRequestUrl);
            Assert.NotEmpty(completed.Evidence);
            Assert.Contains(completed.Reviews, review => review.Decision == "Pass");

            using var detail = await auditorHttp.GetAsync(
                $"control/api/tasks/{Uri.EscapeDataString(projectKey)}/{Uri.EscapeDataString(taskCode)}",
                cancellationToken);
            detail.EnsureSuccessStatusCode();
            var detailText = await detail.Content.ReadAsStringAsync(cancellationToken);
            Assert.Contains(gitEvidence.PullRequestUrl, detailText, StringComparison.Ordinal);
            Assert.Contains(gitEvidence.CommitSha, detailText, StringComparison.Ordinal);
        }
        finally
        {
            if (pullRequestNumber > 0)
                await TryClosePullRequestAsync(github, coordinates, pullRequestNumber, cancellationToken);
            await TryDeleteBranchAsync(github, coordinates, branch, cancellationToken);
            if (issueNumber > 0)
                await TryCloseIssueAsync(github, coordinates, issueNumber, cancellationToken);
        }
    }

    [Fact]
    public async Task Live_three_workers_claim_distinct_tasks_and_recover_a_released_lease_when_explicitly_enabled()
    {
        var config = LiveConfig.TryLoad();
        if (config is null) return;

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var cancellationToken = timeout.Token;
        var coordinates = ParseRepository(config.RepositoryUrl);
        using var github = CreateGitHubClient(config.GitHubToken);
        await using var architect = await CreateMcpClientAsync(config.BaseUrl, config.ArchitectKey, cancellationToken);
        await using var implementer = await CreateMcpClientAsync(config.BaseUrl, config.ImplementerKey, cancellationToken);
        using var auditorHttp = CreateOrchestratorHttpClient(config.BaseUrl, config.AuditorKey);

        var defaultBranch = await GetDefaultBranchAsync(github, coordinates, cancellationToken);
        var projectKey = await EnsureProjectAsync(
            architect,
            auditorHttp,
            config.ProjectKey,
            config.RepositoryUrl,
            defaultBranch,
            cancellationToken);
        await EnsureNoClaimableBacklogAsync(architect, projectKey, cancellationToken);

        var suffix = UniqueSuffix();
        var taskCodes = Enumerable.Range(1, 3).Select(i => $"P9-MW-{suffix}-{i}".ToUpperInvariant()).ToArray();
        var issueNumber = 0;
        var claimed = new List<TaskDto>();

        try
        {
            issueNumber = await CreatePlanIssueAsync(
                github,
                coordinates,
                projectKey,
                $"DevOrchestrator Phase 9 multi-worker proof {suffix}",
                taskCodes.Select((code, index) => new PlanTask(code, $"Multi-worker proof {index + 1}", "Prove distinct concurrent claims and lease recovery.")).ToArray(),
                cancellationToken);

            foreach (var taskCode in taskCodes)
            {
                await WaitForTaskAsync(
                    architect,
                    projectKey,
                    taskCode,
                    task => task.Status == "Ready",
                    $"Webhook did not import {taskCode} as Ready.",
                    cancellationToken);
            }

            var claimTasks = Enumerable.Range(1, 3)
                .Select(i => ClaimWithRetryAsync(implementer, projectKey, $"phase9-worker-{suffix}-{i}", $"devorchestrator/multi/{suffix}/{i}", cancellationToken))
                .ToArray();
            var results = await Task.WhenAll(claimTasks);
            claimed.AddRange(results);

            Assert.Equal(3, claimed.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(taskCodes.OrderBy(x => x), claimed.Select(x => x.Code).OrderBy(x => x));
            Assert.Equal(3, claimed.Select(x => x.LeaseOwner).Distinct(StringComparer.Ordinal).Count());

            foreach (var task in claimed)
            {
                var heartbeat = await CallAsync<TaskDto>(implementer, "task_heartbeat", new Dictionary<string, object?>
                {
                    ["projectKey"] = projectKey,
                    ["taskCode"] = task.Code,
                    ["workerId"] = task.LeaseOwner,
                    ["actor"] = "phase9:implementer"
                }, cancellationToken);
                AssertSuccess(heartbeat);
            }

            var released = claimed[1];
            using (var response = await auditorHttp.PostAsync(
                       $"ops/tasks/{Uri.EscapeDataString(projectKey)}/{Uri.EscapeDataString(released.Code)}/expire-lease",
                       null,
                       cancellationToken))
            {
                response.EnsureSuccessStatusCode();
            }

            var recoveryWorker = $"phase9-recovery-{suffix}";
            var recovered = await ClaimWithRetryAsync(
                implementer,
                projectKey,
                recoveryWorker,
                $"devorchestrator/recovered/{suffix}",
                cancellationToken);
            Assert.Equal(released.Code, recovered.Code);
            Assert.Equal(recoveryWorker, recovered.LeaseOwner);

            var recoveredDetail = await WaitForTaskAsync(
                architect,
                projectKey,
                recovered.Code,
                task => task.Status == "InProgress" && task.LeaseOwner == recoveryWorker,
                "Released task was not reclaimed by the recovery worker.",
                cancellationToken);
            Assert.Equal(recoveryWorker, recoveredDetail.LeaseOwner);

            foreach (var taskCode in taskCodes)
            {
                var blocked = await CallAsync<TaskDto>(implementer, "task_block", new Dictionary<string, object?>
                {
                    ["projectKey"] = projectKey,
                    ["taskCode"] = taskCode,
                    ["reason"] = "Phase 9 live multi-worker proof cleanup",
                    ["actor"] = "phase9:implementer"
                }, cancellationToken);
                AssertSuccess(blocked);
            }

            using var metrics = await auditorHttp.GetAsync("metrics", cancellationToken);
            metrics.EnsureSuccessStatusCode();
            var metricsText = await metrics.Content.ReadAsStringAsync(cancellationToken);
            Assert.Contains("devorchestrator_task_reclaim_total", metricsText, StringComparison.Ordinal);
            Assert.Contains("devorchestrator_manual_lease_expiry_total", metricsText, StringComparison.Ordinal);
        }
        finally
        {
            if (issueNumber > 0)
                await TryCloseIssueAsync(github, coordinates, issueNumber, cancellationToken);
        }
    }

    private static async Task<string> EnsureProjectAsync(
        McpClient architect,
        HttpClient auditorHttp,
        string? requestedProjectKey,
        string repositoryUrl,
        string defaultBranch,
        CancellationToken cancellationToken)
    {
        var projects = await CallAsync<ProjectDto[]>(architect, "project_list", [], cancellationToken);
        AssertSuccess(projects);
        var all = projects.Data ?? [];
        ProjectDto? project;

        if (!string.IsNullOrWhiteSpace(requestedProjectKey))
        {
            project = all.SingleOrDefault(x => string.Equals(x.Key, requestedProjectKey.Trim(), StringComparison.OrdinalIgnoreCase));
            if (project is not null && NormalizeRepository(project.RepositoryUrl) != NormalizeRepository(repositoryUrl))
                throw new InvalidOperationException($"Configured project '{requestedProjectKey}' points to {project.RepositoryUrl}, not {repositoryUrl}.");
        }
        else
        {
            var matches = all.Where(x => NormalizeRepository(x.RepositoryUrl) == NormalizeRepository(repositoryUrl)).ToArray();
            if (matches.Length > 1)
                throw new InvalidOperationException($"Live repository has multiple registered projects: {string.Join(", ", matches.Select(x => x.Key))}.");
            project = matches.SingleOrDefault();
        }

        if (project is null)
        {
            var generatedKey = string.IsNullOrWhiteSpace(requestedProjectKey)
                ? $"phase9-live-{NormalizeKey(repositoryUrl)}"
                : requestedProjectKey.Trim().ToLowerInvariant();
            var registered = await CallAsync<ProjectDto>(architect, "project_register", new Dictionary<string, object?>
            {
                ["projectKey"] = generatedKey,
                ["name"] = "Phase 9 Live Production Proof",
                ["repositoryUrl"] = repositoryUrl,
                ["defaultBranch"] = defaultBranch,
                ["actor"] = "phase9:architect"
            }, cancellationToken);
            AssertSuccess(registered);
            project = registered.Data!;
        }

        if (!project.IsActive)
        {
            using var response = await auditorHttp.PostAsync($"ops/projects/{Uri.EscapeDataString(project.Key)}/resume", null, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        return project.Key;
    }

    private static async Task EnsureNoClaimableBacklogAsync(McpClient architect, string projectKey, CancellationToken cancellationToken)
    {
        foreach (var status in new[] { "Ready", "ChangesRequested", "InProgress" })
        {
            var page = await CallAsync<TaskPageDto>(architect, "task_list_page", new Dictionary<string, object?>
            {
                ["projectKey"] = projectKey,
                ["status"] = status,
                ["offset"] = 0,
                ["limit"] = 100
            }, cancellationToken);
            AssertSuccess(page);
            if (page.Data!.Items.Count != 0)
                throw new InvalidOperationException($"Live proof requires an idle project; found {page.Data.Items.Count} {status} task(s) in '{projectKey}'.");
        }
    }

    private static async Task<TaskDto> ClaimWithRetryAsync(
        McpClient implementer,
        string projectKey,
        string workerId,
        string branch,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var result = await CallAsync<TaskDto>(implementer, "task_claim_next", new Dictionary<string, object?>
            {
                ["projectKey"] = projectKey,
                ["workerId"] = workerId,
                ["branch"] = branch,
                ["actor"] = "phase9:implementer"
            }, cancellationToken);
            if (result.Success && result.Data is not null) return result.Data;
            if (result.ErrorCode is not "task.concurrency_conflict")
                throw new InvalidOperationException($"Worker {workerId} failed to claim: {result.ErrorCode} {result.ErrorMessage}");
            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        throw new TimeoutException($"Worker {workerId} could not claim a task after concurrency retries.");
    }

    private static async Task<TaskDto> WaitForTaskAsync(
        McpClient client,
        string projectKey,
        string taskCode,
        Func<TaskDto, bool> predicate,
        string timeoutMessage,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var result = await CallAsync<TaskDto>(client, "task_get", new Dictionary<string, object?>
            {
                ["projectKey"] = projectKey,
                ["taskCode"] = taskCode
            }, cancellationToken);
            if (result.Success && result.Data is not null && predicate(result.Data)) return result.Data;
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw new TimeoutException(timeoutMessage);
    }

    private static async Task<ToolResponse<T>> CallAsync<T>(
        McpClient client,
        string toolName,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var result = await client.CallToolAsync(toolName, arguments, cancellationToken: cancellationToken);
        if (result.IsError == true)
            throw new InvalidOperationException($"MCP tool '{toolName}' returned protocol error content.");
        if (!result.StructuredContent.HasValue)
            throw new InvalidOperationException($"MCP tool '{toolName}' did not return structured content.");

        return result.StructuredContent.Value.Deserialize<ToolResponse<T>>(JsonOptions)
               ?? throw new InvalidOperationException($"MCP tool '{toolName}' returned invalid structured content.");
    }

    private static void AssertSuccess<T>(ToolResponse<T> response)
        => Assert.True(response.Success, $"{response.ErrorCode}: {response.ErrorMessage}");

    private static Task<McpClient> CreateMcpClientAsync(string baseUrl, string apiKey, CancellationToken cancellationToken)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"{baseUrl.TrimEnd('/')}/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            ConnectionTimeout = TimeSpan.FromSeconds(30),
            AdditionalHeaders = new Dictionary<string, string>
            {
                ["X-DevOrchestrator-Key"] = apiKey
            }
        });
        return McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
    }

    private static HttpClient CreateOrchestratorHttpClient(string baseUrl, string auditorKey)
    {
        var client = new HttpClient { BaseAddress = new Uri($"{baseUrl.TrimEnd('/')}/") };
        client.DefaultRequestHeaders.Add("X-DevOrchestrator-Key", auditorKey);
        return client;
    }

    private static HttpClient CreateGitHubClient(string token)
    {
        var client = new HttpClient { BaseAddress = new Uri("https://api.github.com/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DevOrchestratorMcp-Phase9/1.0");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    private static async Task<string> GetDefaultBranchAsync(HttpClient github, RepositoryCoordinates coordinates, CancellationToken cancellationToken)
    {
        using var response = await github.GetAsync($"repos/{coordinates.Owner}/{coordinates.Repository}", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("default_branch").GetString()
               ?? throw new InvalidOperationException("GitHub repository did not return default_branch.");
    }

    private static async Task<int> CreatePlanIssueAsync(
        HttpClient github,
        RepositoryCoordinates coordinates,
        string projectKey,
        string title,
        IReadOnlyList<PlanTask> tasks,
        CancellationToken cancellationToken)
    {
        var contract = new
        {
            schema = "devorchestrator.plan.v1",
            projectKey,
            tasks = tasks.Select(task => new
            {
                code = task.Code,
                title = task.Title,
                objective = task.Objective,
                acceptanceCriteria = new[] { "Phase 9 live proof reaches the expected terminal state" },
                priority = "High"
            }).ToArray()
        };
        var body = $"```devorchestrator-plan\n{JsonSerializer.Serialize(contract, new JsonSerializerOptions { WriteIndented = true })}\n```";
        using var response = await github.PostAsJsonAsync(
            $"repos/{coordinates.Owner}/{coordinates.Repository}/issues",
            new { title, body },
            cancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("number").GetInt32();
    }

    private static async Task<GitEvidence> CreateBranchCommitAndPullRequestAsync(
        HttpClient github,
        RepositoryCoordinates coordinates,
        string defaultBranch,
        string branch,
        string suffix,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        using var refResponse = await github.GetAsync(
            $"repos/{coordinates.Owner}/{coordinates.Repository}/git/ref/heads/{defaultBranch}",
            cancellationToken);
        refResponse.EnsureSuccessStatusCode();
        using var refDocument = JsonDocument.Parse(await refResponse.Content.ReadAsStringAsync(cancellationToken));
        var baseSha = refDocument.RootElement.GetProperty("object").GetProperty("sha").GetString()!;

        using (var createRef = await github.PostAsJsonAsync(
                   $"repos/{coordinates.Owner}/{coordinates.Repository}/git/refs",
                   new { @ref = $"refs/heads/{branch}", sha = baseSha },
                   cancellationToken))
        {
            createRef.EnsureSuccessStatusCode();
        }

        var markerPath = $".devorchestrator/e2e/{suffix.ToLowerInvariant()}.txt";
        var marker = $"DevOrchestrator Phase 9 synthetic live proof\nIssue: #{issueNumber}\nCreated: {DateTimeOffset.UtcNow:O}\n";
        using var contentResponse = await github.PutAsJsonAsync(
            $"repos/{coordinates.Owner}/{coordinates.Repository}/contents/{markerPath}",
            new
            {
                message = $"test: phase9 live proof {suffix}",
                content = Convert.ToBase64String(Encoding.UTF8.GetBytes(marker)),
                branch
            },
            cancellationToken);
        contentResponse.EnsureSuccessStatusCode();
        using var contentDocument = JsonDocument.Parse(await contentResponse.Content.ReadAsStringAsync(cancellationToken));
        var commitSha = contentDocument.RootElement.GetProperty("commit").GetProperty("sha").GetString()!;

        using var prResponse = await github.PostAsJsonAsync(
            $"repos/{coordinates.Owner}/{coordinates.Repository}/pulls",
            new
            {
                title = $"Phase 9 live proof {suffix}",
                body = $"Synthetic DevOrchestrator live E2E evidence for issue #{issueNumber}. This PR is closed by cleanup and is not intended to merge.",
                head = branch,
                @base = defaultBranch
            },
            cancellationToken);
        prResponse.EnsureSuccessStatusCode();
        using var prDocument = JsonDocument.Parse(await prResponse.Content.ReadAsStringAsync(cancellationToken));
        return new GitEvidence(
            branch,
            markerPath,
            commitSha,
            prDocument.RootElement.GetProperty("number").GetInt32(),
            prDocument.RootElement.GetProperty("html_url").GetString()!);
    }

    private static async Task PostReviewCommentAsync(
        HttpClient github,
        RepositoryCoordinates coordinates,
        int issueNumber,
        string taskCode,
        CancellationToken cancellationToken)
    {
        var contract = new
        {
            schema = "devorchestrator.review.v1",
            taskCode,
            decision = "Pass",
            summary = "Phase 9 live GitHub and remote MCP proof passed",
            findings = Array.Empty<string>(),
            completeOnPass = true
        };
        var body = $"```devorchestrator-review\n{JsonSerializer.Serialize(contract, new JsonSerializerOptions { WriteIndented = true })}\n```";
        using var response = await github.PostAsJsonAsync(
            $"repos/{coordinates.Owner}/{coordinates.Repository}/issues/{issueNumber}/comments",
            new { body },
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task TryClosePullRequestAsync(HttpClient github, RepositoryCoordinates coordinates, int number, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Patch, $"repos/{coordinates.Owner}/{coordinates.Repository}/pulls/{number}")
            {
                Content = JsonContent.Create(new { state = "closed" })
            };
            using var response = await github.SendAsync(request, cancellationToken);
        }
        catch when (!cancellationToken.IsCancellationRequested) { }
    }

    private static async Task TryDeleteBranchAsync(HttpClient github, RepositoryCoordinates coordinates, string branch, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await github.DeleteAsync($"repos/{coordinates.Owner}/{coordinates.Repository}/git/refs/heads/{branch}", cancellationToken);
        }
        catch when (!cancellationToken.IsCancellationRequested) { }
    }

    private static async Task TryCloseIssueAsync(HttpClient github, RepositoryCoordinates coordinates, int issueNumber, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Patch, $"repos/{coordinates.Owner}/{coordinates.Repository}/issues/{issueNumber}")
            {
                Content = JsonContent.Create(new { state = "closed", state_reason = "completed" })
            };
            using var response = await github.SendAsync(request, cancellationToken);
        }
        catch when (!cancellationToken.IsCancellationRequested) { }
    }

    private static RepositoryCoordinates ParseRepository(string repositoryUrl)
    {
        var uri = new Uri(repositoryUrl);
        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (!uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || parts.Length != 2)
            throw new InvalidOperationException("Phase 9 live repository must be a github.com owner/repository URL.");
        return new RepositoryCoordinates(parts[0], parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1]);
    }

    private static string NormalizeRepository(string repositoryUrl)
    {
        var coordinates = ParseRepository(repositoryUrl);
        return $"github.com/{coordinates.Owner}/{coordinates.Repository}".ToLowerInvariant();
    }

    private static string NormalizeKey(string repositoryUrl)
    {
        var coordinates = ParseRepository(repositoryUrl);
        var raw = $"{coordinates.Owner}-{coordinates.Repository}".ToLowerInvariant();
        return new string(raw.Select(ch => char.IsLetterOrDigit(ch) || ch == '-' ? ch : '-').ToArray());
    }

    private static string UniqueSuffix()
        => (Environment.GetEnvironmentVariable("GITHUB_RUN_ID") ?? Guid.NewGuid().ToString("N"))[..8].ToUpperInvariant();

    private sealed record PlanTask(string Code, string Title, string Objective);
    private sealed record GitEvidence(string Branch, string MarkerPath, string CommitSha, int PullRequestNumber, string PullRequestUrl);
    private sealed record RepositoryCoordinates(string Owner, string Repository);

    private sealed record LiveConfig(
        string BaseUrl,
        string RepositoryUrl,
        string GitHubToken,
        string ArchitectKey,
        string ImplementerKey,
        string AuditorKey,
        string? ProjectKey)
    {
        public static LiveConfig? TryLoad()
        {
            var baseUrl = Environment.GetEnvironmentVariable("DEVORCHESTRATOR_LIVE_BASE_URL");
            var repositoryUrl = Environment.GetEnvironmentVariable("DEVORCHESTRATOR_LIVE_REPOSITORY_URL");
            var token = Environment.GetEnvironmentVariable("DEVORCHESTRATOR_LIVE_GITHUB_TOKEN");
            var architectKey = Environment.GetEnvironmentVariable("DEVORCHESTRATOR_LIVE_ARCHITECT_KEY");
            var implementerKey = Environment.GetEnvironmentVariable("DEVORCHESTRATOR_LIVE_IMPLEMENTER_KEY");
            var auditorKey = Environment.GetEnvironmentVariable("DEVORCHESTRATOR_LIVE_AUDITOR_KEY");
            if (new[] { baseUrl, repositoryUrl, token, architectKey, implementerKey, auditorKey }.Any(string.IsNullOrWhiteSpace))
                return null;

            var allowHttp = string.Equals(Environment.GetEnvironmentVariable("DEVORCHESTRATOR_LIVE_ALLOW_HTTP"), "true", StringComparison.OrdinalIgnoreCase);
            if (!allowHttp && !baseUrl!.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Phase 9 live proof requires an HTTPS DEVORCHESTRATOR_LIVE_BASE_URL.");

            return new LiveConfig(
                baseUrl!.TrimEnd('/'),
                repositoryUrl!,
                token!,
                architectKey!,
                implementerKey!,
                auditorKey!,
                Environment.GetEnvironmentVariable("DEVORCHESTRATOR_LIVE_PROJECT_KEY"));
        }
    }
}
