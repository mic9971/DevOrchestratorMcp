using System.ComponentModel;
using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Application.Services;
using DevOrchestrator.McpServer.Security;
using ModelContextProtocol.Server;

namespace DevOrchestrator.McpServer.Tools;

[McpServerToolType]
public static class TaskTools
{
    [McpServerTool(
        Name = "task_create",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Create one small implementation task. Intended for the Architect, not the Implementer.")]
    public static async Task<ToolResponse<TaskDto>> CreateAsync(
        [Description("Project key.")] string projectKey,
        [Description("Short unique task code, for example MEDIA-001.")] string code,
        [Description("Small implementation task title.")] string title,
        [Description("Single clear objective.")] string objective,
        [Description("Testable acceptance criteria.")] string[] acceptanceCriteria,
        [Description("Task codes that must be Done first.")] string[]? dependencies,
        [Description("Architecture, behavior, or scope constraints.")] string[]? constraints,
        [Description("low, normal, high, or critical.")] string priority,
        [Description("Actor creating the task, for example chatgpt-architect.")] string actor,
        ITaskService service,
        ToolAuthorizer authorizer,
        CancellationToken cancellationToken)
    {
        authorizer.Require(McpCallerRole.Architect);

        var seed = new CreateTaskSeed(
            code,
            title,
            objective,
            acceptanceCriteria,
            dependencies,
            constraints,
            priority);

        return ToolResponse<TaskDto>.From(
            await service.CreateAsync(projectKey, seed, actor, cancellationToken));
    }

    [McpServerTool(
        Name = "task_create_batch",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Create a dependency-aware graph of small implementation tasks in one operation.")]
    public static async Task<ToolResponse<BatchCreateResult>> CreateBatchAsync(
        [Description("Project key.")] string projectKey,
        [Description("Task seeds. Dependencies reference task codes in this batch or existing tasks.")] CreateTaskSeed[] tasks,
        [Description("Actor creating the task graph, for example chatgpt-architect.")] string actor,
        ITaskService service,
        ToolAuthorizer authorizer,
        CancellationToken cancellationToken)
    {
        authorizer.Require(McpCallerRole.Architect);
        return ToolResponse<BatchCreateResult>.From(
            await service.CreateBatchAsync(projectKey, tasks, actor, cancellationToken));
    }

    [McpServerTool(
        Name = "task_get",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Get a task specification, dependencies, evidence, and review history.")]
    public static async Task<ToolResponse<TaskDto>> GetAsync(
        [Description("Project key.")] string projectKey,
        [Description("Task code.")] string taskCode,
        ITaskService service,
        CancellationToken cancellationToken)
        => ToolResponse<TaskDto>.From(
            await service.GetAsync(projectKey, taskCode, cancellationToken));

    [McpServerTool(
        Name = "task_list",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("List tasks, optionally filtered by workflow status.")]
    public static async Task<ToolResponse<IReadOnlyList<TaskDto>>> ListAsync(
        [Description("Project key.")] string projectKey,
        [Description("Optional status: Draft, Ready, InProgress, ReadyForReview, ChangesRequested, Done, Blocked, Cancelled.")] string? status,
        ITaskService service,
        CancellationToken cancellationToken)
        => ToolResponse<IReadOnlyList<TaskDto>>.From(
            await service.ListAsync(projectKey, status, cancellationToken));

    [McpServerTool(
        Name = "task_get_next",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Get the next task Codex should implement. ChangesRequested tasks are returned before new Ready tasks.")]
    public static async Task<ToolResponse<TaskDto?>> GetNextAsync(
        [Description("Project key.")] string projectKey,
        ITaskService service,
        CancellationToken cancellationToken)
        => ToolResponse<TaskDto?>.From(
            await service.GetNextAsync(projectKey, cancellationToken));

    [McpServerTool(
        Name = "task_start",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Move a Ready or ChangesRequested task to InProgress. Intended for Codex.")]
    public static async Task<ToolResponse<TaskDto>> StartAsync(
        [Description("Project key.")] string projectKey,
        [Description("Task code.")] string taskCode,
        [Description("Implementer actor, for example codex.")] string actor,
        [Description("Working Git branch, if already known.")] string? branch,
        ITaskService service,
        ToolAuthorizer authorizer,
        CancellationToken cancellationToken)
    {
        authorizer.Require(McpCallerRole.Implementer);
        return ToolResponse<TaskDto>.From(
            await service.StartAsync(
                projectKey,
                taskCode,
                actor,
                branch,
                cancellationToken));
    }

    [McpServerTool(
        Name = "task_add_evidence",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Attach implementation evidence after Codex has changed code and run checks.")]
    public static async Task<ToolResponse<TaskDto>> AddEvidenceAsync(
        [Description("Project key.")] string projectKey,
        [Description("Task code.")] string taskCode,
        [Description("Git branch containing the implementation.")] string branch,
        [Description("Git commit SHA proving the implementation.")] string commitSha,
        [Description("Optional pull request URL.")] string? pullRequestUrl,
        [Description("Changed repository file paths.")] string[] filesChanged,
        [Description("Test/build checks and outcomes, for example 'dotnet test: PASS'.")] string[] tests,
        [Description("Commands Codex executed to verify the implementation.")] string[] commands,
        [Description("Optional implementation notes or known limitations.")] string? notes,
        [Description("Implementer actor, for example codex.")] string actor,
        ITaskService service,
        ToolAuthorizer authorizer,
        CancellationToken cancellationToken)
    {
        authorizer.Require(McpCallerRole.Implementer);

        var evidence = new EvidenceInput(
            branch,
            commitSha,
            pullRequestUrl,
            filesChanged,
            tests,
            commands,
            notes);

        return ToolResponse<TaskDto>.From(
            await service.AddEvidenceAsync(
                projectKey,
                taskCode,
                evidence,
                actor,
                cancellationToken));
    }

    [McpServerTool(
        Name = "task_submit_review",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Move an InProgress task with evidence to ReadyForReview. Codex stops after this call.")]
    public static async Task<ToolResponse<TaskDto>> SubmitReviewAsync(
        [Description("Project key.")] string projectKey,
        [Description("Task code.")] string taskCode,
        [Description("Implementer actor, for example codex.")] string actor,
        ITaskService service,
        ToolAuthorizer authorizer,
        CancellationToken cancellationToken)
    {
        authorizer.Require(McpCallerRole.Implementer);
        return ToolResponse<TaskDto>.From(
            await service.SubmitForReviewAsync(
                projectKey,
                taskCode,
                actor,
                cancellationToken));
    }

    [McpServerTool(
        Name = "task_block",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Mark a task Blocked when implementation cannot proceed.")]
    public static async Task<ToolResponse<TaskDto>> BlockAsync(
        string projectKey,
        string taskCode,
        string reason,
        string actor,
        ITaskService service,
        ToolAuthorizer authorizer,
        CancellationToken cancellationToken)
    {
        authorizer.Require(McpCallerRole.Implementer);
        return ToolResponse<TaskDto>.From(
            await service.BlockAsync(
                projectKey,
                taskCode,
                reason,
                actor,
                cancellationToken));
    }

    [McpServerTool(
        Name = "task_resume",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Resume a Blocked task by moving it back to Ready.")]
    public static async Task<ToolResponse<TaskDto>> ResumeAsync(
        string projectKey,
        string taskCode,
        string actor,
        ITaskService service,
        ToolAuthorizer authorizer,
        CancellationToken cancellationToken)
    {
        authorizer.Require(McpCallerRole.Auditor, McpCallerRole.Architect);
        return ToolResponse<TaskDto>.From(
            await service.ResumeAsync(
                projectKey,
                taskCode,
                actor,
                cancellationToken));
    }

    [McpServerTool(
        Name = "task_reopen",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Reopen a Done task as ChangesRequested. Intended for an Auditor or human maintainer.")]
    public static async Task<ToolResponse<TaskDto>> ReopenAsync(
        string projectKey,
        string taskCode,
        string reason,
        string actor,
        ITaskService service,
        ToolAuthorizer authorizer,
        CancellationToken cancellationToken)
    {
        authorizer.Require(McpCallerRole.Auditor);
        return ToolResponse<TaskDto>.From(
            await service.ReopenAsync(
                projectKey,
                taskCode,
                reason,
                actor,
                cancellationToken));
    }
}
