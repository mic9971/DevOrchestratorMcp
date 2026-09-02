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
        var projects = new StubProjectService(
            new ProjectDto(
                "novel-platform",
                "NovelPlatformArchitecture",
                "https://github.com/mic9971/NovelPlatformArchitecture.git",
                "main",
                true));
        var bridge = new StubBridgeService();
        var deliveries = new InMemoryDeliveryStore();
        var processor = new GitHubWebhookProcessor(projects, bridge, deliveries);

        var result = await processor.ProcessAsync(
            new GitHubWebhookNotification(
                "delivery-1",
                "issues",
                "edited",
                "https://github.com/mic9971/NovelPlatformArchitecture",
                144),
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
        var projects = new StubProjectService(
            new ProjectDto(
                "novel-platform",
                "NovelPlatformArchitecture",
                "https://github.com/mic9971/NovelPlatformArchitecture",
                "main",
                true));
        var bridge = new StubBridgeService();
        var deliveries = new InMemoryDeliveryStore();
        var processor = new GitHubWebhookProcessor(projects, bridge, deliveries);
        var notification = new GitHubWebhookNotification(
            "delivery-2",
            "issues",
            "opened",
            "https://github.com/mic9971/NovelPlatformArchitecture",
            144);

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
        var projects = new StubProjectService(
            new ProjectDto(
                "novel-platform",
                "NovelPlatformArchitecture",
                "https://github.com/mic9971/NovelPlatformArchitecture",
                "main",
                true));
        var bridge = new StubBridgeService();
        var deliveries = new InMemoryDeliveryStore();
        var processor = new GitHubWebhookProcessor(projects, bridge, deliveries);

        var result = await processor.ProcessAsync(
            new GitHubWebhookNotification(
                "delivery-3",
                "issue_comment",
                "created",
                "https://github.com/mic9971/NovelPlatformArchitecture",
                144),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, bridge.ImportCalls);
        Assert.Equal(1, bridge.SyncCalls);
    }

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

    private sealed class StubBridgeService : IGitHubBridgeService
    {
        public int ImportCalls { get; private set; }

        public int SyncCalls { get; private set; }

        public Task<Result<GitHubBridgeImportResult>> ImportPlanIssueAsync(
            string projectKey,
            int issueNumber,
            CancellationToken cancellationToken)
        {
            ImportCalls++;
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
