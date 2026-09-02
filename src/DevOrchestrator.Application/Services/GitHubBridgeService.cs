using DevOrchestrator.Application.Abstractions;
using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Common.Results;

namespace DevOrchestrator.Application.Services;

internal sealed class GitHubBridgeService(
    IProjectService projects,
    ITaskService tasks,
    IReviewService reviews,
    IGitHubBridgeClient github) : IGitHubBridgeService
{
    public async Task<Result<GitHubBridgeImportResult>> ImportPlanIssueAsync(
        string projectKey,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        if (issueNumber <= 0)
        {
            return Result<GitHubBridgeImportResult>.Failure(
                new Error("bridge.issue.invalid", "issueNumber must be greater than zero."));
        }

        var projectResult = await projects.GetAsync(projectKey, cancellationToken);
        if (projectResult.IsFailure)
        {
            return Result<GitHubBridgeImportResult>.Failure(projectResult.Error);
        }

        var project = projectResult.Value!;
        GitHubIssueSnapshot issue;
        try
        {
            issue = await github.GetIssueAsync(project.RepositoryUrl, issueNumber, cancellationToken);
        }
        catch (GitHubBridgeClientException ex)
        {
            return Result<GitHubBridgeImportResult>.Failure(GitHubFailure(ex.Message));
        }

        var planResult = GitHubContractParser.ParsePlan(issue.Body);
        if (planResult.IsFailure)
        {
            return Result<GitHubBridgeImportResult>.Failure(planResult.Error);
        }

        var plan = planResult.Value!;
        if (!string.Equals(plan.ProjectKey.Trim(), project.Key, StringComparison.OrdinalIgnoreCase))
        {
            return Result<GitHubBridgeImportResult>.Failure(
                new Error(
                    "bridge.plan.project_mismatch",
                    $"Plan projectKey '{plan.ProjectKey}' does not match registered project '{project.Key}'."));
        }

        var existingResult = await tasks.ListAsync(project.Key, null, cancellationToken);
        if (existingResult.IsFailure)
        {
            return Result<GitHubBridgeImportResult>.Failure(existingResult.Error);
        }

        var existingCodes = existingResult.Value!
            .Select(x => x.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var skippedCodes = plan.Tasks
            .Select(x => x.Code.Trim().ToUpperInvariant())
            .Where(existingCodes.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var missing = plan.Tasks
            .Where(x => !existingCodes.Contains(x.Code.Trim()))
            .Select(x => new CreateTaskSeed(
                x.Code,
                x.Title,
                x.Objective,
                x.AcceptanceCriteria ?? [],
                x.Dependencies,
                x.Constraints,
                x.Priority))
            .ToArray();

        if (missing.Length == 0)
        {
            return Result<GitHubBridgeImportResult>.Success(
                new GitHubBridgeImportResult(0, skippedCodes.Length, skippedCodes, issue.Url, []));
        }

        var createResult = await tasks.CreateBatchAsync(
            project.Key,
            missing,
            $"github:{issue.Author}",
            cancellationToken);

        if (createResult.IsFailure)
        {
            return Result<GitHubBridgeImportResult>.Failure(createResult.Error);
        }

        return Result<GitHubBridgeImportResult>.Success(
            new GitHubBridgeImportResult(
                createResult.Value!.Created,
                skippedCodes.Length,
                skippedCodes,
                issue.Url,
                createResult.Value.Tasks));
    }

    public async Task<Result<GitHubBridgeReviewSyncResult>> SyncReviewsAsync(
        string projectKey,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        if (issueNumber <= 0)
        {
            return Result<GitHubBridgeReviewSyncResult>.Failure(
                new Error("bridge.issue.invalid", "issueNumber must be greater than zero."));
        }

        var projectResult = await projects.GetAsync(projectKey, cancellationToken);
        if (projectResult.IsFailure)
        {
            return Result<GitHubBridgeReviewSyncResult>.Failure(projectResult.Error);
        }

        var project = projectResult.Value!;
        GitHubIssueSnapshot issue;
        IReadOnlyList<GitHubIssueCommentSnapshot> comments;
        try
        {
            issue = await github.GetIssueAsync(project.RepositoryUrl, issueNumber, cancellationToken);
            comments = await github.GetIssueCommentsAsync(project.RepositoryUrl, issueNumber, cancellationToken);
        }
        catch (GitHubBridgeClientException ex)
        {
            return Result<GitHubBridgeReviewSyncResult>.Failure(GitHubFailure(ex.Message));
        }

        var taskListResult = await tasks.ListAsync(project.Key, null, cancellationToken);
        if (taskListResult.IsFailure)
        {
            return Result<GitHubBridgeReviewSyncResult>.Failure(taskListResult.Error);
        }

        var taskByCode = taskListResult.Value!
            .ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);

        var ignored = 0;
        var invalid = 0;
        var candidates = new List<(GitHubIssueCommentSnapshot Comment, GitHubReviewContract Contract)>();

        foreach (var comment in comments)
        {
            var parsed = GitHubContractParser.ParseReview(comment.Body);
            if (parsed.IsFailure)
            {
                if (parsed.Error.Code == "bridge.contract.not_found")
                {
                    ignored++;
                }
                else
                {
                    invalid++;
                }

                continue;
            }

            var contract = parsed.Value!;
            var code = contract.TaskCode.Trim().ToUpperInvariant();
            if (!string.Equals(comment.Author, issue.Author, StringComparison.OrdinalIgnoreCase) ||
                !taskByCode.TryGetValue(code, out var task) ||
                !string.Equals(task.Status, "ReadyForReview", StringComparison.OrdinalIgnoreCase) ||
                comment.CreatedAtUtc < TruncateToSecond(task.UpdatedAtUtc))
            {
                ignored++;
                continue;
            }

            candidates.Add((comment, contract));
        }

        var latestByTask = candidates
            .GroupBy(x => x.Contract.TaskCode.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(x => x.Comment.CreatedAtUtc).ThenByDescending(x => x.Comment.Id).First())
            .ToArray();

        ignored += candidates.Count - latestByTask.Length;

        var applied = new List<GitHubBridgeAppliedReview>();
        foreach (var candidate in latestByTask)
        {
            var contract = candidate.Contract;
            var result = await reviews.SubmitAsync(
                project.Key,
                contract.TaskCode,
                contract.Decision,
                contract.Summary,
                contract.Findings ?? [],
                $"github:{candidate.Comment.Author}",
                contract.CompleteOnPass,
                cancellationToken);

            if (result.IsFailure)
            {
                invalid++;
                continue;
            }

            applied.Add(new GitHubBridgeAppliedReview(
                result.Value!.Code,
                contract.Decision,
                candidate.Comment.Id,
                $"github:{candidate.Comment.Author}",
                result.Value.Status));
        }

        return Result<GitHubBridgeReviewSyncResult>.Success(
            new GitHubBridgeReviewSyncResult(
                applied.Count,
                ignored,
                invalid,
                issue.Url,
                applied));
    }

    private static DateTimeOffset TruncateToSecond(DateTimeOffset value)
        => value.AddTicks(-(value.Ticks % TimeSpan.TicksPerSecond));

    private static Error GitHubFailure(string message)
        => new("bridge.github.unavailable", $"GitHub bridge request failed: {message}");
}
