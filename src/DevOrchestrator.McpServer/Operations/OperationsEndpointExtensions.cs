using DevOrchestrator.Domain.Tasks;
using DevOrchestrator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevOrchestrator.McpServer.Operations;

public static class OperationsEndpointExtensions
{
    public static IEndpointRouteBuilder MapOperationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/ops/status", GetStatusAsync);
        endpoints.MapGet("/metrics", GetMetricsAsync);
        endpoints.MapPost("/ops/tasks/{projectKey}/{taskCode}/expire-lease", ExpireLeaseAsync);
        endpoints.MapPost("/ops/projects/{projectKey}/pause", PauseProjectAsync);
        endpoints.MapPost("/ops/projects/{projectKey}/resume", ResumeProjectAsync);
        endpoints.MapPost("/ops/webhooks/{deliveryId}/replay", ReplayWebhookAsync);
        return endpoints;
    }

    private static async Task<IResult> GetStatusAsync(
        OrchestratorDbContext db,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var inProgress = await db.DevelopmentTasks
            .AsNoTracking()
            .Where(x => x.Status == DevelopmentTaskStatus.InProgress)
            .Select(x => new { x.LeaseOwner, x.LeaseExpiresAtUtc })
            .ToListAsync(cancellationToken);

        var taskCounts = await db.DevelopmentTasks
            .AsNoTracking()
            .GroupBy(x => x.Status)
            .Select(x => new { Status = x.Key, Count = x.Count() })
            .ToListAsync(cancellationToken);

        var pendingWebhooks = await db.GitHubWebhookInbox
            .AsNoTracking()
            .CountAsync(x => x.CompletedAtUtc == null, cancellationToken);
        var retryingWebhooks = await db.GitHubWebhookInbox
            .AsNoTracking()
            .CountAsync(x => x.CompletedAtUtc == null && x.AttemptCount > 1, cancellationToken);

        return Results.Ok(new
        {
            status = "ok",
            serverTimeUtc = now,
            database = db.Database.ProviderName,
            activeProjects = await db.Projects.AsNoTracking().CountAsync(x => x.IsActive, cancellationToken),
            tasks = taskCounts.ToDictionary(x => x.Status.ToString(), x => x.Count),
            leases = new
            {
                active = inProgress.Count(x => x.LeaseExpiresAtUtc.HasValue && x.LeaseExpiresAtUtc.Value > now),
                expired = inProgress.Count(x => x.LeaseExpiresAtUtc.HasValue && x.LeaseExpiresAtUtc.Value <= now),
                workers = inProgress
                    .Where(x => !string.IsNullOrWhiteSpace(x.LeaseOwner) && x.LeaseExpiresAtUtc > now)
                    .Select(x => x.LeaseOwner!)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(x => x)
                    .ToArray()
            },
            webhookInbox = new { pending = pendingWebhooks, retrying = retryingWebhooks }
        });
    }

    private static async Task<IResult> GetMetricsAsync(
        OrchestratorDbContext db,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var inProgress = await db.DevelopmentTasks
            .AsNoTracking()
            .Where(x => x.Status == DevelopmentTaskStatus.InProgress)
            .Select(x => new { x.LeaseOwner, x.LeaseExpiresAtUtc })
            .ToListAsync(cancellationToken);

        var activeLeases = inProgress.Count(x => x.LeaseExpiresAtUtc.HasValue && x.LeaseExpiresAtUtc.Value > now);
        var expiredLeases = inProgress.Count(x => x.LeaseExpiresAtUtc.HasValue && x.LeaseExpiresAtUtc.Value <= now);
        var activeWorkers = inProgress
            .Where(x => !string.IsNullOrWhiteSpace(x.LeaseOwner) && x.LeaseExpiresAtUtc > now)
            .Select(x => x.LeaseOwner!)
            .Distinct(StringComparer.Ordinal)
            .Count();
        var pendingWebhooks = await db.GitHubWebhookInbox.AsNoTracking()
            .CountAsync(x => x.CompletedAtUtc == null, cancellationToken);
        var retryingWebhooks = await db.GitHubWebhookInbox.AsNoTracking()
            .CountAsync(x => x.CompletedAtUtc == null && x.AttemptCount > 1, cancellationToken);

        var text = string.Join('\n', new[]
        {
            "# TYPE devorchestrator_active_workers gauge",
            $"devorchestrator_active_workers {activeWorkers}",
            "# TYPE devorchestrator_active_task_leases gauge",
            $"devorchestrator_active_task_leases {activeLeases}",
            "# TYPE devorchestrator_expired_task_leases gauge",
            $"devorchestrator_expired_task_leases {expiredLeases}",
            "# TYPE devorchestrator_webhook_inbox_pending gauge",
            $"devorchestrator_webhook_inbox_pending {pendingWebhooks}",
            "# TYPE devorchestrator_webhook_inbox_retrying gauge",
            $"devorchestrator_webhook_inbox_retrying {retryingWebhooks}",
            string.Empty
        });

        return Results.Text(text, "text/plain; version=0.0.4; charset=utf-8");
    }

    private static async Task<IResult> ExpireLeaseAsync(
        string projectKey,
        string taskCode,
        OrchestratorDbContext db,
        CancellationToken cancellationToken)
    {
        var key = projectKey.Trim().ToLowerInvariant();
        var code = taskCode.Trim().ToUpperInvariant();
        var project = await db.Projects.SingleOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (project is null) return Results.NotFound(new { error = "project.not_found" });

        var task = await db.DevelopmentTasks.SingleOrDefaultAsync(
            x => x.ProjectId == project.Id && x.Code == code,
            cancellationToken);
        if (task is null) return Results.NotFound(new { error = "task.not_found" });

        try
        {
            task.ExpireLease("mcp:auditor", "manual operational release", DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { task = code, status = task.Status.ToString(), task.LeaseOwner, task.LeaseExpiresAtUtc });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { error = "task.invalid_state", message = ex.Message });
        }
    }

    private static async Task<IResult> PauseProjectAsync(
        string projectKey,
        OrchestratorDbContext db,
        CancellationToken cancellationToken)
    {
        var key = projectKey.Trim().ToLowerInvariant();
        var project = await db.Projects.SingleOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (project is null) return Results.NotFound(new { error = "project.not_found" });
        project.Deactivate();
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { project = key, active = false });
    }

    private static async Task<IResult> ResumeProjectAsync(
        string projectKey,
        OrchestratorDbContext db,
        CancellationToken cancellationToken)
    {
        var key = projectKey.Trim().ToLowerInvariant();
        var project = await db.Projects.SingleOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (project is null) return Results.NotFound(new { error = "project.not_found" });
        project.Activate();
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { project = key, active = true });
    }

    private static async Task<IResult> ReplayWebhookAsync(
        string deliveryId,
        OrchestratorDbContext db,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var updated = await db.GitHubWebhookInbox
            .Where(x => x.DeliveryId == deliveryId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.CompletedAtUtc, (DateTimeOffset?)null)
                .SetProperty(x => x.LeaseExpiresAtUtc, (DateTimeOffset?)null)
                .SetProperty(x => x.NextAttemptAtUtc, now)
                .SetProperty(x => x.LastError, (string?)null), cancellationToken);

        return updated == 0
            ? Results.NotFound(new { error = "webhook.not_found" })
            : Results.Accepted(value: new { deliveryId, status = "queued" });
    }
}
