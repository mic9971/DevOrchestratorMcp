using System.Text.Json;
using DevOrchestrator.Application.Abstractions;
using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Application.Errors;
using DevOrchestrator.Common.Results;
using DevOrchestrator.Common.Time;
using DevOrchestrator.Domain.Projects;
using DevOrchestrator.Domain.Tasks;

namespace DevOrchestrator.Application.Services;

internal sealed class TaskService(
    ITargetProjectRepository projects,
    IDevelopmentTaskRepository tasks,
    IUnitOfWork unitOfWork,
    IClock clock) : ITaskService
{
    private static readonly TimeSpan CompatibilityLeaseDuration = TimeSpan.FromMinutes(10);

    public async Task<Result<TaskDto>> CreateAsync(
        string projectKey,
        CreateTaskSeed seed,
        string actor,
        CancellationToken cancellationToken)
    {
        var projectResult = await GetProjectAsync(projectKey, cancellationToken);
        if (projectResult.IsFailure) return Result<TaskDto>.Failure(projectResult.Error);

        var project = projectResult.Value!;
        var code = NormalizeCode(seed.Code);
        if (await tasks.GetByCodeAsync(project.Id, code, cancellationToken) is not null)
            return Result<TaskDto>.Failure(OrchestratorErrors.TaskAlreadyExists(code));

        var dependencyTasks = new List<DevelopmentTask>();
        foreach (var dependencyCode in NormalizeDependencies(seed.Dependencies))
        {
            var dependency = await tasks.GetByCodeAsync(project.Id, dependencyCode, cancellationToken);
            if (dependency is null)
                return Result<TaskDto>.Failure(OrchestratorErrors.DependencyNotFound(dependencyCode));
            dependencyTasks.Add(dependency);
        }

        try
        {
            var task = DevelopmentTask.Create(
                project.Id, code, seed.Title, seed.Objective, seed.AcceptanceCriteria,
                seed.Constraints, ParsePriority(seed.Priority), actor, clock.UtcNow);

            foreach (var dependency in dependencyTasks) task.AddDependency(dependency.Id);
            if (dependencyTasks.All(x => x.Status == DevelopmentTaskStatus.Done))
                task.MarkReady(actor, clock.UtcNow);

            tasks.Add(task);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            var codes = dependencyTasks.ToDictionary(x => x.Id, x => x.Code);
            return Result<TaskDto>.Success(TaskMapping.Map(task, project.Key, codes));
        }
        catch (DuplicateKeyException)
        {
            return Result<TaskDto>.Failure(OrchestratorErrors.TaskAlreadyExists(code));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Result<TaskDto>.Failure(OrchestratorErrors.InvalidInput(ex.Message));
        }
    }

    public async Task<Result<BatchCreateResult>> CreateBatchAsync(
        string projectKey,
        IReadOnlyList<CreateTaskSeed> seeds,
        string actor,
        CancellationToken cancellationToken)
    {
        if (seeds.Count == 0)
            return Result<BatchCreateResult>.Failure(OrchestratorErrors.InvalidInput("At least one task is required."));

        var projectResult = await GetProjectAsync(projectKey, cancellationToken);
        if (projectResult.IsFailure) return Result<BatchCreateResult>.Failure(projectResult.Error);
        var project = projectResult.Value!;
        var normalizedSeeds = seeds.Select(x => x with { Code = NormalizeCode(x.Code) }).ToArray();
        var duplicate = normalizedSeeds.GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase).FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            return Result<BatchCreateResult>.Failure(OrchestratorErrors.InvalidInput($"Duplicate task code '{duplicate.Key}' in batch."));

        foreach (var seed in normalizedSeeds)
        {
            if (await tasks.GetByCodeAsync(project.Id, seed.Code, cancellationToken) is not null)
                return Result<BatchCreateResult>.Failure(OrchestratorErrors.TaskAlreadyExists(seed.Code));
        }

        var graphError = ValidateBatchGraph(normalizedSeeds);
        if (graphError is not null) return Result<BatchCreateResult>.Failure(OrchestratorErrors.InvalidInput(graphError));

        var existingDependencies = new Dictionary<string, DevelopmentTask>(StringComparer.OrdinalIgnoreCase);
        var incomingCodes = normalizedSeeds.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var dependencyCode in normalizedSeeds
                     .SelectMany(x => NormalizeDependencies(x.Dependencies))
                     .Where(x => !incomingCodes.Contains(x))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var dependency = await tasks.GetByCodeAsync(project.Id, dependencyCode, cancellationToken);
            if (dependency is null)
                return Result<BatchCreateResult>.Failure(OrchestratorErrors.DependencyNotFound(dependencyCode));
            existingDependencies[dependencyCode] = dependency;
        }

        try
        {
            var now = clock.UtcNow;
            var created = normalizedSeeds.ToDictionary(
                seed => seed.Code,
                seed => DevelopmentTask.Create(
                    project.Id, seed.Code, seed.Title, seed.Objective, seed.AcceptanceCriteria,
                    seed.Constraints, ParsePriority(seed.Priority), actor, now),
                StringComparer.OrdinalIgnoreCase);

            foreach (var seed in normalizedSeeds)
            {
                var task = created[seed.Code];
                foreach (var dependencyCode in NormalizeDependencies(seed.Dependencies))
                {
                    var dependency = created.TryGetValue(dependencyCode, out var incoming)
                        ? incoming
                        : existingDependencies[dependencyCode];
                    task.AddDependency(dependency.Id);
                }
            }

            foreach (var seed in normalizedSeeds)
            {
                var task = created[seed.Code];
                var dependencies = NormalizeDependencies(seed.Dependencies);
                var allDependenciesDone = dependencies.All(code =>
                    existingDependencies.TryGetValue(code, out var existing) && existing.Status == DevelopmentTaskStatus.Done);
                if (dependencies.Length == 0 || allDependenciesDone) task.MarkReady(actor, now);
            }

            tasks.AddRange(created.Values);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            var allCodes = created.Values.Concat(existingDependencies.Values).ToDictionary(x => x.Id, x => x.Code);
            var mapped = normalizedSeeds.Select(x => TaskMapping.Map(created[x.Code], project.Key, allCodes)).ToArray();
            return Result<BatchCreateResult>.Success(new BatchCreateResult(mapped.Length, mapped));
        }
        catch (DuplicateKeyException)
        {
            return Result<BatchCreateResult>.Failure(OrchestratorErrors.TaskAlreadyExists());
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return Result<BatchCreateResult>.Failure(OrchestratorErrors.InvalidInput(ex.Message));
        }
    }

    public async Task<Result<TaskDto>> GetAsync(string projectKey, string code, CancellationToken cancellationToken)
    {
        var found = await FindAsync(projectKey, code, cancellationToken);
        if (found.IsFailure) return Result<TaskDto>.Failure(found.Error);
        var (project, task) = found.Value!;
        return Result<TaskDto>.Success(await MapWithDependencyCodesAsync(project, task, cancellationToken));
    }

    public async Task<Result<IReadOnlyList<TaskDto>>> ListAsync(string projectKey, string? status, CancellationToken cancellationToken)
    {
        var projectResult = await GetProjectAsync(projectKey, cancellationToken);
        if (projectResult.IsFailure) return Result<IReadOnlyList<TaskDto>>.Failure(projectResult.Error);

        DevelopmentTaskStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<DevelopmentTaskStatus>(status, true, out var value))
                return Result<IReadOnlyList<TaskDto>>.Failure(OrchestratorErrors.InvalidInput($"Unknown task status '{status}'."));
            parsedStatus = value;
        }

        var project = projectResult.Value!;
        var items = await tasks.ListAsync(project.Id, parsedStatus, cancellationToken);
        var mapped = new List<TaskDto>(items.Count);
        foreach (var task in items) mapped.Add(await MapWithDependencyCodesAsync(project, task, cancellationToken));
        return Result<IReadOnlyList<TaskDto>>.Success(mapped);
    }

    public async Task<Result<TaskDto?>> GetNextAsync(string projectKey, CancellationToken cancellationToken)
    {
        var projectResult = await GetProjectAsync(projectKey, cancellationToken);
        if (projectResult.IsFailure) return Result<TaskDto?>.Failure(projectResult.Error);
        var project = projectResult.Value!;
        var task = await tasks.GetNextAsync(project.Id, cancellationToken);
        return task is null
            ? Result<TaskDto?>.Success(null)
            : Result<TaskDto?>.Success(await MapWithDependencyCodesAsync(project, task, cancellationToken));
    }

    public Task<Result<TaskDto>> StartAsync(
        string projectKey, string code, string actor, string? branch, CancellationToken cancellationToken)
        => MutateAsync(
            projectKey,
            code,
            (task, now) => task.Claim(actor, actor, branch, now, CompatibilityLeaseDuration),
            cancellationToken);

    public async Task<Result<TaskDto>> AddEvidenceAsync(
        string projectKey, string code, EvidenceInput evidence, string actor, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            filesChanged = evidence.FilesChanged ?? [],
            tests = evidence.Tests ?? [],
            commands = evidence.Commands ?? [],
            notes = evidence.Notes
        });
        return await MutateAsync(projectKey, code, (task, now) => task.AddEvidence(
            actor, evidence.Branch, evidence.CommitSha, evidence.PullRequestUrl, payload, now), cancellationToken);
    }

    public Task<Result<TaskDto>> SubmitForReviewAsync(string projectKey, string code, string actor, CancellationToken cancellationToken)
        => MutateAsync(projectKey, code, (task, now) => task.SubmitForReview(actor, now), cancellationToken);

    public Task<Result<TaskDto>> BlockAsync(string projectKey, string code, string reason, string actor, CancellationToken cancellationToken)
        => MutateAsync(projectKey, code, (task, now) => task.Block(actor, reason, now), cancellationToken);

    public Task<Result<TaskDto>> ResumeAsync(string projectKey, string code, string actor, CancellationToken cancellationToken)
        => MutateAsync(projectKey, code, (task, now) => task.ResumeFromBlocked(actor, now), cancellationToken);

    public Task<Result<TaskDto>> ReopenAsync(string projectKey, string code, string reason, string actor, CancellationToken cancellationToken)
        => MutateAsync(projectKey, code, (task, now) => task.Reopen(actor, reason, now), cancellationToken);

    private async Task<Result<TaskDto>> MutateAsync(
        string projectKey, string code, Action<DevelopmentTask, DateTimeOffset> mutation, CancellationToken cancellationToken)
    {
        var found = await FindAsync(projectKey, code, cancellationToken);
        if (found.IsFailure) return Result<TaskDto>.Failure(found.Error);
        var (project, task) = found.Value!;
        try
        {
            mutation(task, clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result<TaskDto>.Success(await MapWithDependencyCodesAsync(project, task, cancellationToken));
        }
        catch (ConcurrencyConflictException ex)
        {
            return Result<TaskDto>.Failure(OrchestratorErrors.ConcurrencyConflict(ex.Message));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or ArgumentOutOfRangeException)
        {
            return Result<TaskDto>.Failure(OrchestratorErrors.InvalidState(ex.Message));
        }
    }

    private async Task<Result<(TargetProject Project, DevelopmentTask Task)>> FindAsync(
        string projectKey, string code, CancellationToken cancellationToken)
    {
        var projectResult = await GetProjectAsync(projectKey, cancellationToken);
        if (projectResult.IsFailure) return Result<(TargetProject, DevelopmentTask)>.Failure(projectResult.Error);
        var project = projectResult.Value!;
        var normalizedCode = NormalizeCode(code);
        var task = await tasks.GetByCodeAsync(project.Id, normalizedCode, cancellationToken);
        return task is null
            ? Result<(TargetProject, DevelopmentTask)>.Failure(OrchestratorErrors.TaskNotFound(normalizedCode))
            : Result<(TargetProject, DevelopmentTask)>.Success((project, task));
    }

    private async Task<Result<TargetProject>> GetProjectAsync(string projectKey, CancellationToken cancellationToken)
    {
        var key = projectKey.Trim().ToLowerInvariant();
        var project = await projects.GetByKeyAsync(key, cancellationToken);
        return project is null
            ? Result<TargetProject>.Failure(OrchestratorErrors.ProjectNotFound(key))
            : Result<TargetProject>.Success(project);
    }

    private async Task<TaskDto> MapWithDependencyCodesAsync(
        TargetProject project, DevelopmentTask task, CancellationToken cancellationToken)
    {
        if (task.Dependencies.Count == 0) return TaskMapping.Map(task, project.Key);
        var ids = task.Dependencies.Select(x => x.DependsOnTaskId).ToArray();
        var allTasks = await tasks.ListAsync(project.Id, null, cancellationToken);
        var codeById = allTasks.Where(x => ids.Contains(x.Id)).ToDictionary(x => x.Id, x => x.Code);
        return TaskMapping.Map(task, project.Key, codeById);
    }

    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();
    private static string[] NormalizeDependencies(string[]? dependencies)
        => dependencies?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(NormalizeCode)
               .Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
    private static TaskPriority ParsePriority(string priority)
        => Enum.TryParse<TaskPriority>(priority, true, out var value) ? value : TaskPriority.Normal;

    private static string? ValidateBatchGraph(IReadOnlyList<CreateTaskSeed> seeds)
    {
        var incoming = seeds.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var graph = seeds.ToDictionary(
            x => x.Code,
            x => NormalizeDependencies(x.Dependencies).Where(incoming.Contains).ToArray(),
            StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        bool HasCycle(string node)
        {
            if (visiting.Contains(node)) return true;
            if (!visited.Add(node)) return false;
            visiting.Add(node);
            foreach (var dependency in graph[node]) if (HasCycle(dependency)) return true;
            visiting.Remove(node);
            return false;
        }

        foreach (var node in graph.Keys) if (HasCycle(node)) return $"Dependency cycle detected involving task '{node}'.";
        return null;
    }
}
