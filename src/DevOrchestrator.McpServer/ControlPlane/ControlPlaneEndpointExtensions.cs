using DevOrchestrator.Domain.Tasks;
using DevOrchestrator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevOrchestrator.McpServer.ControlPlane;

public static class ControlPlaneEndpointExtensions
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapControlPlaneEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/control", () => Results.Redirect("/control/index.html"));
        endpoints.MapGet("/control/api/dashboard", GetDashboardAsync);
        endpoints.MapGet("/control/api/projects", GetProjectsAsync);
        endpoints.MapGet("/control/api/tasks", GetTasksAsync);
        endpoints.MapGet("/control/api/workers", GetWorkersAsync);
        endpoints.MapGet("/control/api/webhooks", GetWebhooksAsync);
        endpoints.MapGet("/control/api/audit", GetAuditAsync);
        return endpoints;
    }

    private static async Task<IResult> GetDashboardAsync(
        OrchestratorDbContext db,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var taskCounts = await db.DevelopmentTasks
            .AsNoTracking()
            .GroupBy(x => x.Status)
            .Select(x => new { Status = x.Key, Count = x.Count() })
            .ToListAsync(cancellationToken);

        var leases = await db.DevelopmentTasks
            .AsNoTracking()
            .Where(x => x.Status == DevelopmentTaskStatus.InProgress)
            .Select(x => new { x.LeaseOwner, x.LeaseExpiresAtUtc })
            .ToListAsync(cancellationToken);

        var webhookCounts = await db.GitHubWebhookInbox
            .AsNoTracking()
            .GroupBy(x => x.DeadLetteredAtUtc != null
                ? "dead-lettered"
                : x.CompletedAtUtc != null
                    ? "completed"
                    : x.AttemptCount > 1 ? "retrying" : "pending")
            .Select(x => new { State = x.Key, Count = x.Count() })
            .ToListAsync(cancellationToken);

        return Results.Ok(new
        {
            serverTimeUtc = now,
            database = db.Database.ProviderName,
            projects = new
            {
                active = await db.Projects.AsNoTracking().CountAsync(x => x.IsActive, cancellationToken),
                paused = await db.Projects.AsNoTracking().CountAsync(x => !x.IsActive, cancellationToken)
            },
            tasks = taskCounts.ToDictionary(x => x.Status.ToString(), x => x.Count),
            leases = new
            {
                active = leases.Count(x => x.LeaseExpiresAtUtc.HasValue && x.LeaseExpiresAtUtc.Value > now),
                expired = leases.Count(x => x.LeaseExpiresAtUtc.HasValue && x.LeaseExpiresAtUtc.Value <= now),
                workers = leases
                    .Where(x => !string.IsNullOrWhiteSpace(x.LeaseOwner) && x.LeaseExpiresAtUtc > now)
                    .Select(x => x.LeaseOwner!)
                    .Distinct(StringComparer.Ordinal)
                    .Count()
            },
            webhooks = webhookCounts.ToDictionary(x => x.State, x => x.Count)
        });
    }

    private static async Task<IResult> GetProjectsAsync(
        OrchestratorDbContext db,
        CancellationToken cancellationToken)
    {
        var projects = await db.Projects
            .AsNoTracking()
            .OrderBy(x => x.Key)
            .Select(x => new
            {
                x.Id,
                x.Key,
                x.Name,
                x.RepositoryUrl,
                x.DefaultBranch,
                x.IsActive,
                x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var taskCounts = await db.DevelopmentTasks
            .AsNoTracking()
            .GroupBy(x => new { x.ProjectId, x.Status })
            .Select(x => new { x.Key.ProjectId, x.Key.Status, Count = x.Count() })
            .ToListAsync(cancellationToken);

        var countsByProject = taskCounts
            .GroupBy(x => x.ProjectId)
            .ToDictionary(
                x => x.Key,
                x => x.ToDictionary(y => y.Status.ToString(), y => y.Count));

        return Results.Ok(projects.Select(project => new
        {
            project.Id,
            project.Key,
            project.Name,
            project.RepositoryUrl,
            project.DefaultBranch,
            project.IsActive,
            project.CreatedAtUtc,
            Tasks = countsByProject.GetValueOrDefault(project.Id, new Dictionary<string, int>())
        }));
    }

    private static async Task<IResult> GetTasksAsync(
        string? projectKey,
        string? status,
        int? offset,
        int? limit,
        OrchestratorDbContext db,
        CancellationToken cancellationToken)
    {
        var skip = Math.Max(0, offset ?? 0);
        var take = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        DevelopmentTaskStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<DevelopmentTaskStatus>(status, true, out var value))
                return Results.BadRequest(new { error = "task.invalid_status", status });
            parsedStatus = value;
        }

        var query = from task in db.DevelopmentTasks.AsNoTracking()
                    join project in db.Projects.AsNoTracking() on task.ProjectId equals project.Id
                    select new { task, project };

        if (!string.IsNullOrWhiteSpace(projectKey))
        {
            var key = projectKey.Trim().ToLowerInvariant();
            query = query.Where(x => x.project.Key == key);
        }

        if (parsedStatus.HasValue)
            query = query.Where(x => x.task.Status == parsedStatus.Value);

        var rows = await query
            .OrderBy(x => x.project.Key)
            .ThenBy(x => x.task.Code)
            .Skip(skip)
            .Take(take + 1)
            .Select(x => new
            {
                ProjectKey = x.project.Key,
                ProjectName = x.project.Name,
                x.task.Code,
                x.task.Title,
                x.task.Status,
                x.task.Priority,
                x.task.ActiveBranch,
                x.task.LastCommitSha,
                x.task.PullRequestUrl,
                x.task.BlockReason,
                x.task.LeaseOwner,
                x.task.LeaseExpiresAtUtc,
                x.task.LastHeartbeatAtUtc,
                x.task.UpdatedAtUtc,
                x.task.Revision
            })
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > take;
        var items = rows.Take(take).Select(x => new
        {
            x.ProjectKey,
            x.ProjectName,
            x.Code,
            x.Title,
            Status = x.Status.ToString(),
            Priority = x.Priority.ToString(),
            x.ActiveBranch,
            x.LastCommitSha,
            x.PullRequestUrl,
            x.BlockReason,
            x.LeaseOwner,
            x.LeaseExpiresAtUtc,
            x.LastHeartbeatAtUtc,
            x.UpdatedAtUtc,
            x.Revision
        }).ToArray();

        return Results.Ok(new
        {
            offset = skip,
            limit = take,
            nextOffset = hasMore ? skip + take : (int?)null,
            items
        });
    }

    private static async Task<IResult> GetWorkersAsync(
        OrchestratorDbContext db,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var workers = await (from task in db.DevelopmentTasks.AsNoTracking()
                             join project in db.Projects.AsNoTracking() on task.ProjectId equals project.Id
                             where task.Status == DevelopmentTaskStatus.InProgress && task.LeaseOwner != null
                             orderby task.LeaseOwner, project.Key, task.Code
                             select new
                             {
                                 WorkerId = task.LeaseOwner!,
                                 ProjectKey = project.Key,
                                 TaskCode = task.Code,
                                 TaskTitle = task.Title,
                                 task.ActiveBranch,
                                 task.LeaseExpiresAtUtc,
                                 task.LastHeartbeatAtUtc,
                                 task.UpdatedAtUtc
                             })
            .ToListAsync(cancellationToken);

        return Results.Ok(workers.Select(x => new
        {
            x.WorkerId,
            x.ProjectKey,
            x.TaskCode,
            x.TaskTitle,
            x.ActiveBranch,
            x.LeaseExpiresAtUtc,
            x.LastHeartbeatAtUtc,
            x.UpdatedAtUtc,
            LeaseState = !x.LeaseExpiresAtUtc.HasValue
                ? "missing"
                : x.LeaseExpiresAtUtc.Value <= now ? "expired" : "active"
        }));
    }

    private static async Task<IResult> GetWebhooksAsync(
        string? state,
        int? offset,
        int? limit,
        OrchestratorDbContext db,
        CancellationToken cancellationToken)
    {
        var skip = Math.Max(0, offset ?? 0);
        var take = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        var normalizedState = string.IsNullOrWhiteSpace(state) ? "pending" : state.Trim().ToLowerInvariant();
        if (normalizedState is not ("pending" or "retrying" or "dead-lettered" or "completed" or "all"))
            return Results.BadRequest(new { error = "webhook.invalid_state", state });

        var query = db.GitHubWebhookInbox.AsNoTracking().AsQueryable();
        query = normalizedState switch
        {
            "pending" => query.Where(x => x.CompletedAtUtc == null && x.DeadLetteredAtUtc == null && x.AttemptCount <= 1),
            "retrying" => query.Where(x => x.CompletedAtUtc == null && x.DeadLetteredAtUtc == null && x.AttemptCount > 1),
            "dead-lettered" => query.Where(x => x.DeadLetteredAtUtc != null),
            "completed" => query.Where(x => x.CompletedAtUtc != null),
            _ => query
        };

        var rows = await query
            .OrderByDescending(x => x.ReceivedAtUtc)
            .Skip(skip)
            .Take(take + 1)
            .Select(x => new
            {
                x.DeliveryId,
                x.EventName,
                x.Action,
                x.RepositoryUrl,
                x.IssueNumber,
                x.AttemptCount,
                x.ReceivedAtUtc,
                x.NextAttemptAtUtc,
                x.LeaseExpiresAtUtc,
                x.CompletedAtUtc,
                x.DeadLetteredAtUtc,
                x.LastError
            })
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > take;
        return Results.Ok(new
        {
            offset = skip,
            limit = take,
            nextOffset = hasMore ? skip + take : (int?)null,
            items = rows.Take(take).ToArray()
        });
    }

    private static async Task<IResult> GetAuditAsync(
        string? projectKey,
        string? taskCode,
        int? offset,
        int? limit,
        OrchestratorDbContext db,
        CancellationToken cancellationToken)
    {
        var skip = Math.Max(0, offset ?? 0);
        var take = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
        var query = from taskEvent in db.TaskEvents.AsNoTracking()
                    join task in db.DevelopmentTasks.AsNoTracking() on taskEvent.TaskId equals task.Id
                    join project in db.Projects.AsNoTracking() on task.ProjectId equals project.Id
                    select new { taskEvent, task, project };

        if (!string.IsNullOrWhiteSpace(projectKey))
        {
            var key = projectKey.Trim().ToLowerInvariant();
            query = query.Where(x => x.project.Key == key);
        }

        if (!string.IsNullOrWhiteSpace(taskCode))
        {
            var code = taskCode.Trim().ToUpperInvariant();
            query = query.Where(x => x.task.Code == code);
        }

        var projected = query.Select(x => new
        {
            ProjectKey = x.project.Key,
            TaskCode = x.task.Code,
            x.taskEvent.EventType,
            x.taskEvent.Actor,
            x.taskEvent.PayloadJson,
            x.taskEvent.CreatedAtUtc
        });

        var isSqlite = db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
        var rows = isSqlite
            ? (await projected.ToListAsync(cancellationToken))
                .OrderByDescending(x => x.CreatedAtUtc)
                .Skip(skip)
                .Take(take + 1)
                .ToList()
            : await projected
                .OrderByDescending(x => x.CreatedAtUtc)
                .Skip(skip)
                .Take(take + 1)
                .ToListAsync(cancellationToken);

        var hasMore = rows.Count > take;
        return Results.Ok(new
        {
            offset = skip,
            limit = take,
            nextOffset = hasMore ? skip + take : (int?)null,
            items = rows.Take(take).ToArray()
        });
    }
}
