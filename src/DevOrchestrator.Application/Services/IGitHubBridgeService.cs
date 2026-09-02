using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Common.Results;

namespace DevOrchestrator.Application.Services;

public interface IGitHubBridgeService
{
    Task<Result<GitHubBridgeImportResult>> ImportPlanIssueAsync(
        string projectKey,
        int issueNumber,
        CancellationToken cancellationToken);

    Task<Result<GitHubBridgeReviewSyncResult>> SyncReviewsAsync(
        string projectKey,
        int issueNumber,
        CancellationToken cancellationToken);
}
