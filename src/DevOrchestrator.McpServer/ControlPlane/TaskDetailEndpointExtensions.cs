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
        var task = await (from item in db.DevelopmentTasks.AsNoTracking()
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
                              Status = item.Status.ToString(),
                              Priority = item.Priority.ToString(),
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

        if (task is null)
            return Results.NotFound(new { error = "task.not_found" });

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
        var dependencies = await db.DevelopmentTasks
            .AsNoTracking()
            .Where(x => dependencyIds.Contains(x.Id))
            .OrderBy(x => x.Code)
            .Select(x => new { x.Code, x.Title, Status = x.Status.ToString() })
            .ToListAsync(cancellationToken);

        var evidence = await db.TaskEvidence
            .AsNoTracking()
            .Where(x => x.TaskId == task.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(20)
            .Select(x => new
            {
                x.Actor,
                x.Branch,
                x.CommitSha,
                x.PullRequestUrl,
                x.PayloadJson,
                x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var reviews = await db.TaskReviews
            .AsNoTracking()
            .Where(x => x.TaskId == task.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(20)
            .Select(x => new
            {
                Decision = x.Decision.ToString(),
                x.Actor,
                x.Summary,
                x.FindingsJson,
                x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var events = await db.TaskEvents
            .AsNoTracking()
            .Where(x => x.TaskId == task.Id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(30)
            .Select(x => new
            {
                x.EventType,
                x.Actor,
                x.PayloadJson,
                x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(new { task, criteria, dependencies, evidence, reviews, events });
    }
}
