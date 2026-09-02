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

    private static readonly HashSet<string> NonRetryablePlanErrors =
        new(StringComparer.Ordinal)
        {
            "bridge.contract.not_found",
            "bridge.contract.invalid",
            "bridge.plan.invalid",
            "bridge.plan.project_mismatch"
        };

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

        if (!IsSupported(notification.EventName, notification.Action))
        {
            return Result<GitHubWebhookProcessResult>.Success(
                CreateOutcome(notification, "ignored"));
        }

        if (!await deliveries.TryBeginAsync(
                notification.DeliveryId,
                notification.EventName,
                cancellationToken))
        {
            return Result<GitHubWebhookProcessResult>.Success(
                CreateOutcome(notification, "duplicate"));
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
                    CreateOutcome(notification, "unregistered_repository"));
            }

            if (notification.EventName.Equals("issues", StringComparison.OrdinalIgnoreCase))
            {
                var import = await bridge.ImportPlanIssueAsync(
                    project.Key,
                    notification.IssueNumber,
                    cancellationToken);

                if (import.IsFailure)
                {
                    if (NonRetryablePlanErrors.Contains(import.Error.Code))
                    {
                        await deliveries.CompleteAsync(notification.DeliveryId, cancellationToken);
                        var outcome = import.Error.Code == "bridge.contract.not_found"
                            ? "ignored"
                            : "rejected";

                        return Result<GitHubWebhookProcessResult>.Success(
                            CreateOutcome(
                                notification,
                                outcome,
                                project.Key,
                                $"{import.Error.Code}: {import.Error.Message}"));
                    }

                    await deliveries.AbandonAsync(notification.DeliveryId, cancellationToken);
                    return Result<GitHubWebhookProcessResult>.Failure(import.Error);
                }
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
            }

            await deliveries.CompleteAsync(notification.DeliveryId, cancellationToken);
            return Result<GitHubWebhookProcessResult>.Success(
                CreateOutcome(notification, "processed", project.Key));
        }
        catch
        {
            await deliveries.AbandonAsync(notification.DeliveryId, cancellationToken);
            throw;
        }
    }

    private static GitHubWebhookProcessResult CreateOutcome(
        GitHubWebhookNotification notification,
        string outcome,
        string? projectKey = null,
        string? detail = null)
        => new(
            notification.DeliveryId,
            notification.EventName,
            notification.Action,
            outcome,
            projectKey,
            notification.IssueNumber,
            detail);

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
