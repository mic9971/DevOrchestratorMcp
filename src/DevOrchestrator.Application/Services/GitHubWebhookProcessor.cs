using DevOrchestrator.Application.Abstractions;
using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Common.Results;

namespace DevOrchestrator.Application.Services;

internal sealed class GitHubWebhookProcessor(
    IProjectService projects,
    IGitHubBridgeService bridge,
    IGitHubWebhookDeliveryStore deliveries) : IGitHubWebhookProcessor
{
    private static readonly HashSet<string> PlanActions =
        new(StringComparer.OrdinalIgnoreCase) { "opened", "edited", "reopened" };

    private static readonly HashSet<string> ReviewActions =
        new(StringComparer.OrdinalIgnoreCase) { "created", "edited" };

    public async Task<Result<GitHubWebhookProcessResult>> ProcessAsync(
        GitHubWebhookNotification notification,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(notification.DeliveryId))
        {
            return Result<GitHubWebhookProcessResult>.Failure(
                new Error("webhook.delivery_id_required", "GitHub delivery id is required."));
        }

        if (notification.IssueNumber <= 0)
        {
            return Result<GitHubWebhookProcessResult>.Failure(
                new Error("webhook.issue_number_invalid", "GitHub issue number must be positive."));
        }

        var supported = IsSupported(notification.EventName, notification.Action);
        if (!supported)
        {
            return Result<GitHubWebhookProcessResult>.Success(
                new GitHubWebhookProcessResult(
                    notification.DeliveryId,
                    notification.EventName,
                    notification.Action,
                    "ignored",
                    null,
                    notification.IssueNumber));
        }

        if (!await deliveries.TryBeginAsync(
                notification.DeliveryId,
                notification.EventName,
                cancellationToken))
        {
            return Result<GitHubWebhookProcessResult>.Success(
                new GitHubWebhookProcessResult(
                    notification.DeliveryId,
                    notification.EventName,
                    notification.Action,
                    "duplicate",
                    null,
                    notification.IssueNumber));
        }

        try
        {
            var projectList = await projects.ListAsync(cancellationToken);
            if (projectList.IsFailure)
            {
                await deliveries.AbandonAsync(notification.DeliveryId, cancellationToken);
                return Result<GitHubWebhookProcessResult>.Failure(projectList.Error);
            }

            var repository = NormalizeRepository(notification.RepositoryUrl);
            var project = projectList.Value!
                .FirstOrDefault(x =>
                    x.IsActive &&
                    string.Equals(
                        NormalizeRepository(x.RepositoryUrl),
                        repository,
                        StringComparison.OrdinalIgnoreCase));

            if (project is null)
            {
                await deliveries.CompleteAsync(notification.DeliveryId, cancellationToken);
                return Result<GitHubWebhookProcessResult>.Success(
                    new GitHubWebhookProcessResult(
                        notification.DeliveryId,
                        notification.EventName,
                        notification.Action,
                        "unregistered_repository",
                        null,
                        notification.IssueNumber));
            }

            Result operation;
            if (notification.EventName.Equals("issues", StringComparison.OrdinalIgnoreCase))
            {
                var import = await bridge.ImportPlanIssueAsync(
                    project.Key,
                    notification.IssueNumber,
                    cancellationToken);

                if (import.IsFailure)
                {
                    await deliveries.AbandonAsync(notification.DeliveryId, cancellationToken);
                    return Result<GitHubWebhookProcessResult>.Failure(import.Error);
                }

                operation = Result.Success();
            }
            else
            {
                var sync = await bridge.SyncReviewsAsync(
                    project.Key,
                    notification.IssueNumber,
                    cancellationToken);

                if (sync.IsFailure)
                {
                    await deliveries.AbandonAsync(notification.DeliveryId, cancellationToken);
                    return Result<GitHubWebhookProcessResult>.Failure(sync.Error);
                }

                operation = Result.Success();
            }

            if (operation.IsSuccess)
            {
                await deliveries.CompleteAsync(notification.DeliveryId, cancellationToken);
            }

            return Result<GitHubWebhookProcessResult>.Success(
                new GitHubWebhookProcessResult(
                    notification.DeliveryId,
                    notification.EventName,
                    notification.Action,
                    "processed",
                    project.Key,
                    notification.IssueNumber));
        }
        catch
        {
            await deliveries.AbandonAsync(notification.DeliveryId, cancellationToken);
            throw;
        }
    }

    private static bool IsSupported(string eventName, string action)
        => (eventName.Equals("issues", StringComparison.OrdinalIgnoreCase) && PlanActions.Contains(action))
           || (eventName.Equals("issue_comment", StringComparison.OrdinalIgnoreCase) && ReviewActions.Contains(action));

    private static string NormalizeRepository(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return value.Trim().TrimEnd('/').ToLowerInvariant();
        }

        var path = uri.AbsolutePath.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^4];
        }

        return $"{uri.Host}/{path}".ToLowerInvariant();
    }
}
