using DevOrchestrator.Application.Abstractions;
using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Application.Services;
using DevOrchestrator.Common.Results;

namespace DevOrchestrator.Application.Tests;

public sealed class GitHubWebhookProcessorTests
{
    [Fact]
    public async Task Issues_event_imports_registered_project_plan()
    {
        var projects = CreateProjects();
        var bridge = new StubBridgeService();
        var deliveries = new InMemoryDeliveryStore();
        var processor = new GitHubWebhookProcessor(projects, bridge, deliveries);

        var result = await processor.ProcessAsync(
            Notification("delivery-1", "issues", "edited"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("processed", result.Value!.Outcome);
        Assert.Equal("novel-platform", result.Value.ProjectKey);
        Assert.Equal(1, bridge.ImportCalls);
        Assert.Equal(0, bridge.SyncCalls);
    }

    [Fact]
    public async Task Duplicate_delivery_does_not_replay_bridge_operation()
    {
        var projects = CreateProjects();
        var bridge = new StubBridgeService();
        var deliveries = new InMemoryDeliveryStore();
        var processor = new GitHubWebhookProcessor(projects, bridge, deliveries);
        var notification = Notification("delivery-2", "issues", "opened");

        var first = await processor.ProcessAsync(notification, CancellationToken.None);
        var second = await processor.ProcessAsync(notification, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal("duplicate", second.Value!.Outcome);
        Assert.Equal(1, bridge.ImportCalls);
    }

    [Fact]
    public async Task Issue_comment_event_synchronizes_reviews()
    {
        var projects = CreateProjects();
        var bridge = new StubBridgeService();
        var deliveries = new InMemoryDeliveryStore();
        var processor = new GitHubWebhookProcessor(projects, bridge, deliveries);

        var result = await processor.ProcessAsync(
            Notification("delivery-3", "issue_comment", "created"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, bridge.ImportCalls);
        Assert.Equal(1, bridge.SyncCalls);
    }

    [Fact]
    public async Task Ordinary_issue_without_plan_contract_is_accepted_and_not_retried()
    {
        var projects = CreateProjects();
        var bridge = new StubBridgeService(
            new Error("bridge.contract.not_found", "No devorchestrator-plan contract found."));
        var deliveries = new InMemoryDeliveryStore();
        var processor = new GitHubWebhookProcessor(projects, bridge, deliveries);
        var notification = Notification("delivery-ordinary", "issues", "opened");

        var first = await processor.ProcessAsync(notification, CancellationToken.None);
        var second = await processor.ProcessAsync(notification, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.Equal("ignored", first.Value!.Outcome);
        Assert.Contains("bridge.contract.not_found", first.Value.Detail, StringComparison.Ordinal);
        Assert.True(second.IsSuccess);
        Assert.Equal("duplicate", second.Value!.Outcome);
        Assert.Equal(1, bridge.ImportCalls);
    }

    [Fact]
    public async Task Transient_bridge_failure_abandons_delivery_for_retry()
    {
        var projects = CreateProjects();
        var bridge = new StubBridgeService(
            new Error("bridge.github.unavailable", "GitHub is temporarily unavailable."));
        var deliveries = new InMemoryDeliveryStore();
        var processor = new GitHubWebhookProcessor(projects, bridge, deliveries);
        var notification = Notification("delivery-retry", "issues", "edited");

        var first = await processor.ProcessAsync(notification, CancellationToken.None);
        var second = await processor.ProcessAsync(notification, CancellationToken.None);

        Assert.True(first.IsFailure);
        Assert.True(second.IsFailure);
        Assert.Equal(2, bridge.ImportCalls);
    }

    private static GitHubWebhookNotification Notification(
        string deliveryId,
        string eventName,
        string action)
        => new(
            deliveryId,
            eventName,
            action,
            "https://github.com/mic9971/NovelPlatformArchitecture",
            144);

    private static StubProjectService CreateProjects()
        => new(
            new ProjectDto(
                "novel-platform",
                "NovelPlatformArchitecture",
                "https://github.com/mic9971/NovelPlatformArchitecture.git",
                "main",
                true));

    private sealed class StubProjectService(ProjectDto project) : IProjectService
    {
        public Task<Result<ProjectDto>> RegisterAsync(
            string key,
            string name,
            string repositoryUrl,
            string defaultBranch,
            string actor,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<ProjectDto>.Success(project));

        public Task<Result<ProjectDto>> GetAsync(
            string key,
            CancellationToken cancellationToken)
            => Task.FromResult(Result<ProjectDto>.Success(project));

        public Task<Result<IReadOnlyList<ProjectDto>>> ListAsync(
            CancellationToken cancellationToken)
            => Task.FromResult(Result<IReadOnlyList<ProjectDto>>.Success([project]));
    }

    private sealed class StubBridgeService(Error? importError = null) : IGitHubBridgeService
    {
        public int ImportCalls { get; private set; }

        public int SyncCalls { get; private set; }

        public Task<Result<GitHubBridgeImportResult>> ImportPlanIssueAsync(
            string projectKey,
            int issueNumber,
            CancellationToken cancellationToken)
        {
            ImportCalls++;
            if (importError is not null)
            {
                return Task.FromResult(Result<GitHubBridgeImportResult>.Failure(importError));
            }

            return Task.FromResult(
                Result<GitHubBridgeImportResult>.Success(
                    new GitHubBridgeImportResult(
                        0,
                        0,
                        [],
                        $"https://github.com/example/repo/issues/{issueNumber}",
                        [])));
        }

        public Task<Result<GitHubBridgeReviewSyncResult>> SyncReviewsAsync(
            string projectKey,
            int issueNumber,
            CancellationToken cancellationToken)
        {
            SyncCalls++;
            return Task.FromResult(
                Result<GitHubBridgeReviewSyncResult>.Success(
                    new GitHubBridgeReviewSyncResult(
                        0,
                        0,
                        0,
                        $"https://github.com/example/repo/issues/{issueNumber}",
                        [])));
        }
    }

    private sealed class InMemoryDeliveryStore : IGitHubWebhookDeliveryStore
    {
        private readonly HashSet<string> deliveries = new(StringComparer.Ordinal);

        public Task<bool> TryBeginAsync(
            string deliveryId,
            string eventName,
            CancellationToken cancellationToken)
            => Task.FromResult(deliveries.Add(deliveryId));

        public Task CompleteAsync(
            string deliveryId,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AbandonAsync(
            string deliveryId,
            CancellationToken cancellationToken)
        {
            deliveries.Remove(deliveryId);
            return Task.CompletedTask;
        }
    }
}
