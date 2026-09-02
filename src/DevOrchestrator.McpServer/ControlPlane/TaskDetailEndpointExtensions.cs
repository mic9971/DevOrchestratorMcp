using DevOrchestrator.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevOrchestrator.McpServer.ControlPlane;

public static class TaskDetailEndpointExtensions
{
    public static IEndpointRouteBuilder MapControlPlaneTaskDetailEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/control/api/tasks/{projectKey}/{taskCode}", GetTaskDetailAsync);
        return endpoints;
    }

    private static async Task<IResult> GetTaskDetailAsync(
        string projectKey,
        string taskCode,
        OrchestratorDbContext db,
        CancellationToken cancellationToken)
    {
        var key = projectKey.Trim().ToLowerInvariant();
        var code = taskCode.Trim().ToUpperInvariant();
        var rawTask = await (from item in db.DevelopmentTasks.AsNoTracking()
                             join project in db.Projects.AsNoTracking() on item.ProjectId equals project.Id
                             where project.Key == key && item.Code == code
                             select new
                             {
                                 item.Id,
                                 ProjectKey = project.Key,
                                 ProjectName = project.Name,
                                 RepositoryUrl = project.RepositoryUrl,
                                 item.Code,
                                 item.Title,
                                 item.Objective,
                                 item.Constraints,
                                 item.Status,
                                 item.Priority,
                                 item.ActiveBranch,
                                 item.LastCommitSha,
                                 item.PullRequestUrl,
                                 item.BlockReason,
                                 item.LeaseOwner,
                                 item.LeaseExpiresAtUtc,
                                 item.LastHeartbeatAtUtc,
                                 item.CreatedAtUtc,
                                 item.UpdatedAtUtc,
                                 item.Revision
                             })
            .SingleOrDefaultAsync(cancellationToken);

        if (rawTask is null)
            return Results.NotFound(new { error = "task.not_found" });

        var task = new
        {
            rawTask.Id,
            rawTask.ProjectKey,
            rawTask.ProjectName,
            rawTask.RepositoryUrl,
            rawTask.Code,
            rawTask.Title,
            rawTask.Objective,
            rawTask.Constraints,
            Status = rawTask.Status.ToString(),
            Priority = rawTask.Priority.ToString(),
            rawTask.ActiveBranch,
            rawTask.LastCommitSha,
            rawTask.PullRequestUrl,
            rawTask.BlockReason,
            rawTask.LeaseOwner,
            rawTask.LeaseExpiresAtUtc,
            rawTask.LastHeartbeatAtUtc,
            rawTask.CreatedAtUtc,
            rawTask.UpdatedAtUtc,
            rawTask.Revision
        };

        var criteria = await db.AcceptanceCriteria
            .AsNoTracking()
            .Where(x => x.TaskId == task.Id)
            .OrderBy(x => x.Id)
            .Select(x => new { x.Description, x.IsSatisfied })
            .ToListAsync(cancellationToken);

        var dependencyIds = await db.TaskDependencies
            .AsNoTracking()
            .Where(x => x.TaskId == task.Id)
            .Select(x => x.DependsOnTaskId)
            .ToListAsync(cancellationToken);
        var dependencyRows = await db.DevelopmentTasks
            .AsNoTracking()
            .Where(x => dependencyIds.Contains(x.Id))
            .OrderBy(x => x.Code)
            .Select(x => new { x.Code, x.Title, x.Status })
            .ToListAsync(cancellationToken);
        var dependencies = dependencyRows
            .Select(x => new { x.Code, x.Title, Status = x.Status.ToString() })
            .ToArray();

        var isSqlite = db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;

        var evidenceQuery = db.TaskEvidence
            .AsNoTracking()
            .Where(x => x.TaskId == task.Id)
            .Select(x => new
            {
                x.Actor,
                x.Branch,
                x.CommitSha,
                x.PullRequestUrl,
                x.PayloadJson,
                x.CreatedAtUtc
            });
        var evidence = isSqlite
            ? (await evidenceQuery.ToListAsync(cancellationToken))
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(20)
                .ToList()
            : await evidenceQuery
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(20)
                .ToListAsync(cancellationToken);

        var reviewQuery = db.TaskReviews
            .AsNoTracking()
            .Where(x => x.TaskId == task.Id)
            .Select(x => new
            {
                x.Decision,
                x.Actor,
                x.Summary,
                x.FindingsJson,
                x.CreatedAtUtc
            });
        var reviewRows = isSqlite
            ? (await reviewQuery.ToListAsync(cancellationToken))
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(20)
                .ToList()
            : await reviewQuery
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(20)
                .ToListAsync(cancellationToken);
        var reviews = reviewRows.Select(x => new
        {
            Decision = x.Decision.ToString(),
            x.Actor,
            x.Summary,
            x.FindingsJson,
            x.CreatedAtUtc
        }).ToArray();

        var eventQuery = db.TaskEvents
            .AsNoTracking()
            .Where(x => x.TaskId == task.Id)
            .Select(x => new
            {
                x.EventType,
                x.Actor,
                x.PayloadJson,
                x.CreatedAtUtc
            });
        var events = isSqlite
            ? (await eventQuery.ToListAsync(cancellationToken))
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(30)
                .ToList()
            : await eventQuery
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(30)
                .ToListAsync(cancellationToken);

        return Results.Ok(new { task, criteria, dependencies, evidence, reviews, events });
    }
}
