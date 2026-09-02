using DevOrchestrator.Application.Abstractions;
using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Application.Services;
using DevOrchestrator.Common.Results;

namespace DevOrchestrator.Application.Tests;

public sealed class GitHubBridgeServiceTests
{
    private static readonly ProjectDto Project = new(
        "novel-platform",
        "Novel Platform",
        "https://github.com/mic9971/NovelPlatformArchitecture",
        "main",
        true);

    [Fact]
    public async Task ImportPlanIssue_skips_existing_codes_and_creates_only_missing_tasks()
    {
        var tasks = new FakeTaskService([CreateTask("P2-001", "Done", DateTimeOffset.UtcNow)]);
        var github = new FakeGitHubBridgeClient
        {
            Issue = new GitHubIssueSnapshot(
                12,
                "https://github.com/mic9971/NovelPlatformArchitecture/issues/12",
                """
                ```devorchestrator-plan
                {
                  "schema": "devorchestrator.plan.v1",
                  "projectKey": "novel-platform",
                  "tasks": [
                    { "code": "P2-001", "title": "Existing", "objective": "Existing", "acceptanceCriteria": ["Done"] },
                    { "code": "P2-002", "title": "New", "objective": "Create new task", "acceptanceCriteria": ["Build passes"], "dependencies": ["P2-001"] }
                  ]
                }
                ```
                """,
                "architect",
                DateTimeOffset.UtcNow)
        };

        var service = new GitHubBridgeService(
            new FakeProjectService(),
            tasks,
            new FakeReviewService(),
            github);

        var result = await service.ImportPlanIssueAsync("novel-platform", 12, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.Created);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Equal(["P2-001"], result.Value.SkippedTaskCodes);
        Assert.Single(tasks.CreatedSeeds);
        Assert.Equal("P2-002", tasks.CreatedSeeds[0].Code);
        Assert.Equal("github:architect", tasks.LastCreateActor);
    }

    [Fact]
    public async Task SyncReviews_applies_latest_review_after_current_submission_and_ignores_old_or_plain_comments()
    {
        var submittedAt = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);
        var tasks = new FakeTaskService([CreateTask("P2-001", "ReadyForReview", submittedAt)]);
        var reviews = new FakeReviewService();
        var github = new FakeGitHubBridgeClient
        {
            Issue = new GitHubIssueSnapshot(
                12,
                "https://github.com/mic9971/NovelPlatformArchitecture/issues/12",
                string.Empty,
                "architect",
                submittedAt),
            Comments =
            [
                new GitHubIssueCommentSnapshot(1, "comment/1", "auditor", "plain discussion", submittedAt.AddMinutes(1)),
                new GitHubIssueCommentSnapshot(
                    2,
                    "comment/2",
                    "auditor",
                    ReviewContract("Pass", "Old review"),
                    submittedAt.AddMinutes(-1)),
                new GitHubIssueCommentSnapshot(
                    3,
                    "comment/3",
                    "auditor",
                    ReviewContract("ChangesRequested", "Fix one finding"),
                    submittedAt.AddMinutes(2))
            ]
        };

        var service = new GitHubBridgeService(
            new FakeProjectService(),
            tasks,
            reviews,
            github);

        var result = await service.SyncReviewsAsync("novel-platform", 12, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.Applied);
        Assert.Equal(2, result.Value.Ignored);
        Assert.Equal(0, result.Value.Invalid);
        Assert.Single(reviews.Calls);
        Assert.Equal("P2-001", reviews.Calls[0].TaskCode);
        Assert.Equal("ChangesRequested", reviews.Calls[0].Decision);
        Assert.Equal("github:auditor", reviews.Calls[0].Actor);
    }

    private static string ReviewContract(string decision, string summary)
        => $$"""
           ```devorchestrator-review
           {
             "schema": "devorchestrator.review.v1",
             "taskCode": "P2-001",
             "decision": "{{decision}}",
             "summary": "{{summary}}",
             "findings": ["finding"]
           }
           ```
           """;

    private static TaskDto CreateTask(string code, string status, DateTimeOffset updatedAt)
        => new(
            Project.Key,
            code,
            code,
            "Objective",
            [],
            "Normal",
            status,
            null,
            null,
            null,
            null,
            [],
            [],
            [],
            [],
            updatedAt.AddMinutes(-5),
            updatedAt);

    private sealed class FakeProjectService : IProjectService
    {
        public Task<Result<ProjectDto>> GetAsync(string key, CancellationToken cancellationToken)
            => Task.FromResult(Result<ProjectDto>.Success(Project));

        public Task<Result<ProjectDto>> RegisterAsync(string key, string name, string repositoryUrl, string defaultBranch, string actor, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<ProjectDto>>> ListAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class FakeTaskService(IEnumerable<TaskDto> initial) : ITaskService
    {
        private readonly List<TaskDto> items = [.. initial];

        public List<CreateTaskSeed> CreatedSeeds { get; } = [];

        public string? LastCreateActor { get; private set; }

        public Task<Result<IReadOnlyList<TaskDto>>> ListAsync(string projectKey, string? status, CancellationToken cancellationToken)
            => Task.FromResult(Result<IReadOnlyList<TaskDto>>.Success(items));

        public Task<Result<BatchCreateResult>> CreateBatchAsync(string projectKey, IReadOnlyList<CreateTaskSeed> seeds, string actor, CancellationToken cancellationToken)
        {
            LastCreateActor = actor;
            CreatedSeeds.AddRange(seeds);
            var created = seeds.Select(seed => CreateTask(seed.Code.Trim().ToUpperInvariant(), "Ready", DateTimeOffset.UtcNow)).ToArray();
            items.AddRange(created);
            return Task.FromResult(Result<BatchCreateResult>.Success(new BatchCreateResult(created.Length, created)));
        }

        public Task<Result<TaskDto>> CreateAsync(string projectKey, CreateTaskSeed seed, string actor, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<TaskDto>> GetAsync(string projectKey, string code, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<TaskDto?>> GetNextAsync(string projectKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<TaskDto>> StartAsync(string projectKey, string code, string actor, string? branch, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<TaskDto>> AddEvidenceAsync(string projectKey, string code, EvidenceInput evidence, string actor, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<TaskDto>> SubmitForReviewAsync(string projectKey, string code, string actor, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<TaskDto>> BlockAsync(string projectKey, string code, string reason, string actor, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<TaskDto>> ResumeAsync(string projectKey, string code, string actor, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<TaskDto>> ReopenAsync(string projectKey, string code, string reason, string actor, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeReviewService : IReviewService
    {
        public List<ReviewCall> Calls { get; } = [];

        public Task<Result<TaskDto>> SubmitAsync(
            string projectKey,
            string taskCode,
            string decision,
            string summary,
            IReadOnlyList<string> findings,
            string actor,
            bool completeOnPass,
            CancellationToken cancellationToken)
        {
            Calls.Add(new ReviewCall(taskCode, decision, actor));
            var status = string.Equals(decision, "Pass", StringComparison.OrdinalIgnoreCase) && completeOnPass
                ? "Done"
                : "ChangesRequested";
            return Task.FromResult(Result<TaskDto>.Success(CreateTask(taskCode, status, DateTimeOffset.UtcNow)));
        }
    }

    private sealed class FakeGitHubBridgeClient : IGitHubBridgeClient
    {
        public GitHubIssueSnapshot Issue { get; init; } = default!;
        public IReadOnlyList<GitHubIssueCommentSnapshot> Comments { get; init; } = [];

        public Task<GitHubIssueSnapshot> GetIssueAsync(string repositoryUrl, int issueNumber, CancellationToken cancellationToken)
            => Task.FromResult(Issue);

        public Task<IReadOnlyList<GitHubIssueCommentSnapshot>> GetIssueCommentsAsync(string repositoryUrl, int issueNumber, CancellationToken cancellationToken)
            => Task.FromResult(Comments);
    }

    private sealed record ReviewCall(string TaskCode, string Decision, string Actor);
}
