using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Common.Results;

namespace DevOrchestrator.Application.Services;

public interface IGitHubWebhookProcessor
{
    Task<Result<GitHubWebhookProcessResult>> ProcessAsync(
        GitHubWebhookNotification notification,
        CancellationToken cancellationToken);
}
