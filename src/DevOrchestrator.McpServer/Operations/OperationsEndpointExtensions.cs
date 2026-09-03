using System.Text.Json;
using DevOrchestrator.Domain.Identity;
using DevOrchestrator.Domain.Tasks;
using DevOrchestrator.Infrastructure.Persistence;
using DevOrchestrator.McpServer.Identity;
using DevOrchestrator.McpServer.Security;
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

    private static async Task<IResult> GetStatusAsync(OrchestratorDbContext db, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var inProgress = await db.DevelopmentTasks.AsNoTracking()
            .Where(x => x.Status == DevelopmentTaskStatus.InProgress)
            .Select(x => new { x.LeaseOwner, x.LeaseExpiresAtUtc }).ToListAsync(cancellationToken);
        var taskCounts = await db.DevelopmentTasks.AsNoTracking().GroupBy(x => x.Status)
            .Select(x => new { Status = x.Key, Count = x.Count() }).ToListAsync(cancellationToken);
        var pendingWebhooks = await db.GitHubWebhookInbox.AsNoTracking()
            .CountAsync(x => x.CompletedAtUtc == null && x.DeadLetteredAtUtc == null, cancellationToken);
        var retryingWebhooks = await db.GitHubWebhookInbox.AsNoTracking()
            .CountAsync(x => x.CompletedAtUtc == null && x.DeadLetteredAtUtc == null && x.AttemptCount > 1, cancellationToken);
        var deadLetteredWebhooks = await db.GitHubWebhookInbox.AsNoTracking()
            .CountAsync(x => x.DeadLetteredAtUtc != null, cancellationToken);

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
                workers = inProgress.Where(x => !string.IsNullOrWhiteSpace(x.LeaseOwner) && x.LeaseExpiresAtUtc > now)
                    .Select(x => x.LeaseOwner!).Distinct(StringComparer.Ordinal).OrderBy(x => x).ToArray()
            },
            webhookInbox = new
            {
                pending = pendingWebhooks,
                retrying = retryingWebhooks,
                deadLettered = deadLetteredWebhooks
            }
        });
    }

    private static async Task<IResult> GetMetricsAsync(OrchestratorDbContext db, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var inProgress = await db.DevelopmentTasks.AsNoTracking()
            .Where(x => x.Status == DevelopmentTaskStatus.InProgress)
            .Select(x => new { x.LeaseOwner, x.LeaseExpiresAtUtc }).ToListAsync(cancellationToken);
        var activeLeases = inProgress.Count(x => x.LeaseExpiresAtUtc.HasValue && x.LeaseExpiresAtUtc.Value > now);
        var expiredLeases = inProgress.Count(x => x.LeaseExpiresAtUtc.HasValue && x.LeaseExpiresAtUtc.Value <= now);
        var activeWorkers = inProgress.Where(x => !string.IsNullOrWhiteSpace(x.LeaseOwner) && x.LeaseExpiresAtUtc > now)
            .Select(x => x.LeaseOwner!).Distinct(StringComparer.Ordinal).Count();
        var webhookAttempts = await db.GitHubWebhookInbox.AsNoTracking()
            .Select(x => x.AttemptCount)
            .ToListAsync(cancellationToken);
        var pendingWebhooks = await db.GitHubWebhookInbox.AsNoTracking()
            .CountAsync(x => x.CompletedAtUtc == null && x.DeadLetteredAtUtc == null, cancellationToken);
        var retryingWebhooks = await db.GitHubWebhookInbox.AsNoTracking()
            .CountAsync(x => x.CompletedAtUtc == null && x.DeadLetteredAtUtc == null && x.AttemptCount > 1, cancellationToken);
        var deadLetteredWebhooks = await db.GitHubWebhookInbox.AsNoTracking()
            .CountAsync(x => x.DeadLetteredAtUtc != null, cancellationToken);
        var taskReclaims = await db.TaskEvents.AsNoTracking()
            .CountAsync(x => x.EventType == "task.reclaimed", cancellationToken);
        var manualLeaseExpiries = await db.TaskEvents.AsNoTracking()
            .CountAsync(x => x.EventType == "task.lease_expired_manually", cancellationToken);
        var webhookRetries = webhookAttempts.Sum(x => Math.Max(0, x - 1));

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
            "# TYPE devorchestrator_webhook_dead_lettered gauge",
            $"devorchestrator_webhook_dead_lettered {deadLetteredWebhooks}",
            "# TYPE devorchestrator_webhook_retry_total counter",
            $"devorchestrator_webhook_retry_total {webhookRetries}",
            "# TYPE devorchestrator_task_reclaim_total counter",
            $"devorchestrator_task_reclaim_total {taskReclaims}",
            "# TYPE devorchestrator_manual_lease_expiry_total counter",
            $"devorchestrator_manual_lease_expiry_total {manualLeaseExpiries}",
            string.Empty
        });
        return Results.Text(text, "text/plain; version=0.0.4; charset=utf-8");
    }

    private static async Task<IResult> ExpireLeaseAsync(string projectKey, string taskCode, HttpContext context, OrchestratorDbContext db, CancellationToken cancellationToken)
    {
        var key = projectKey.Trim().ToLowerInvariant();
        var code = taskCode.Trim().ToUpperInvariant();
        var project = await db.Projects.SingleOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (project is null) return Results.NotFound(new { error = "project.not_found" });
        var task = await db.DevelopmentTasks.SingleOrDefaultAsync(x => x.ProjectId == project.Id && x.Code == code, cancellationToken);
        if (task is null) return Results.NotFound(new { error = "task.not_found" });

        try
        {
            var actor = ResolveActor(context);
            var before = JsonSerializer.Serialize(new { task.LeaseOwner, task.LeaseExpiresAtUtc });
            task.ExpireLease(actor, "manual operational release", DateTimeOffset.UtcNow);
            db.SecurityAuditEvents.Add(Audit(context, actor, "task.lease_expired", "task", $"{key}/{code}", "manual operational release", before,
                JsonSerializer.Serialize(new { task.LeaseOwner, task.LeaseExpiresAtUtc })));
            await db.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { task = code, status = task.Status.ToString(), task.LeaseOwner, task.LeaseExpiresAtUtc });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { error = "task.invalid_state", message = ex.Message });
        }
    }

    private static async Task<IResult> PauseProjectAsync(string projectKey, HttpContext context, OrchestratorDbContext db, CancellationToken cancellationToken)
    {
        var key = projectKey.Trim().ToLowerInvariant();
        var project = await db.Projects.SingleOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (project is null) return Results.NotFound(new { error = "project.not_found" });
        var before = project.IsActive;
        project.Deactivate();
        var actor = ResolveActor(context);
        db.SecurityAuditEvents.Add(Audit(context, actor, "project.paused", "project", key, null,
            JsonSerializer.Serialize(new { active = before }), JsonSerializer.Serialize(new { active = false })));
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { project = key, active = false });
    }

    private static async Task<IResult> ResumeProjectAsync(string projectKey, HttpContext context, OrchestratorDbContext db, CancellationToken cancellationToken)
    {
        var key = projectKey.Trim().ToLowerInvariant();
        var project = await db.Projects.SingleOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (project is null) return Results.NotFound(new { error = "project.not_found" });
        var before = project.IsActive;
        project.Activate();
        var actor = ResolveActor(context);
        db.SecurityAuditEvents.Add(Audit(context, actor, "project.resumed", "project", key, null,
            JsonSerializer.Serialize(new { active = before }), JsonSerializer.Serialize(new { active = true })));
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { project = key, active = true });
    }

    private static async Task<IResult> ReplayWebhookAsync(string deliveryId, HttpContext context, OrchestratorDbContext db, CancellationToken cancellationToken)
    {
        var item = await db.GitHubWebhookInbox.SingleOrDefaultAsync(x => x.DeliveryId == deliveryId, cancellationToken);
        if (item is null) return Results.NotFound(new { error = "webhook.not_found" });
        var before = JsonSerializer.Serialize(new { item.CompletedAtUtc, item.DeadLetteredAtUtc, item.LeaseExpiresAtUtc, item.NextAttemptAtUtc, item.LastError });
        var now = DateTimeOffset.UtcNow;
        item.Requeue(now);
        var actor = ResolveActor(context);
        db.SecurityAuditEvents.Add(Audit(context, actor, "webhook.replayed", "webhook", deliveryId, null, before,
            JsonSerializer.Serialize(new { item.CompletedAtUtc, item.DeadLetteredAtUtc, item.LeaseExpiresAtUtc, item.NextAttemptAtUtc, item.LastError })));
        await db.SaveChangesAsync(cancellationToken);
        return Results.Accepted(value: new { deliveryId, status = "queued" });
    }

    private static string ResolveActor(HttpContext context)
        => context.Items[McpApiKeyMiddleware.CallerIdentityItemKey]?.ToString()
           ?? (HumanIdentityAccess.IsHuman(context.User) ? HumanIdentityAccess.Actor(context.User) : "unknown");

    private static SecurityAuditEvent Audit(HttpContext context, string actor, string action, string resourceType, string resourceId, string? reason, string? beforeJson, string? afterJson)
        => SecurityAuditEvent.Create(actor, HumanIdentityAccess.IsHuman(context.User) ? "human" : "machine", action, resourceType, resourceId, DateTime.UtcNow,
            reason, beforeJson, afterJson, context.Connection.RemoteIpAddress?.ToString());
}
