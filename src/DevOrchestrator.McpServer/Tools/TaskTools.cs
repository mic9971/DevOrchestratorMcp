using System.ComponentModel;
using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Application.Services;
using DevOrchestrator.McpServer.Security;
using ModelContextProtocol.Server;

namespace DevOrchestrator.McpServer.Tools;

[McpServerToolType]
public static class TaskTools
{
    [McpServerTool(Name = "task_create", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Create one small implementation task. Intended for the Architect, not the Implementer.")]
    public static async Task<ToolResponse<TaskDto>> CreateAsync(
        string projectKey, string code, string title, string objective, string[] acceptanceCriteria,
        string[]? dependencies, string[]? constraints, string priority, string actor,
        ITaskService service, ToolAuthorizer authorizer, CancellationToken cancellationToken)
    {
        actor = authorizer.RequireAndResolveActor(actor, McpCallerRole.Architect);
        var seed = new CreateTaskSeed(code, title, objective, acceptanceCriteria, dependencies, constraints, priority);
        return ToolResponse<TaskDto>.From(await service.CreateAsync(projectKey, seed, actor, cancellationToken));
    }

    [McpServerTool(Name = "task_create_batch", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Create a dependency-aware graph of small implementation tasks in one operation.")]
    public static async Task<ToolResponse<BatchCreateResult>> CreateBatchAsync(
        string projectKey, CreateTaskSeed[] tasks, string actor,
        ITaskService service, ToolAuthorizer authorizer, CancellationToken cancellationToken)
    {
        actor = authorizer.RequireAndResolveActor(actor, McpCallerRole.Architect);
        return ToolResponse<BatchCreateResult>.From(await service.CreateBatchAsync(projectKey, tasks, actor, cancellationToken));
    }

    [McpServerTool(Name = "task_get", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Get a task specification, dependencies, evidence, review history, and current worker lease.")]
    public static async Task<ToolResponse<TaskDto>> GetAsync(
        string projectKey, string taskCode, ITaskService service, CancellationToken cancellationToken)
        => ToolResponse<TaskDto>.From(await service.GetAsync(projectKey, taskCode, cancellationToken));

    [McpServerTool(Name = "task_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Compatibility list returning full task details. Prefer task_list_page for large projects.")]
    public static async Task<ToolResponse<IReadOnlyList<TaskDto>>> ListAsync(
        string projectKey, string? status, ITaskService service, CancellationToken cancellationToken)
        => ToolResponse<IReadOnlyList<TaskDto>>.From(await service.ListAsync(projectKey, status, cancellationToken));

    [McpServerTool(Name = "task_list_page", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("List compact task summaries with bounded pagination. Use this for normal project browsing.")]
    public static async Task<ToolResponse<TaskPageDto>> ListPageAsync(
        string projectKey, string? status,
        [Description("Zero-based offset.")] int offset,
        [Description("Page size from 1 to 100.")] int limit,
        ITaskQueryService service, CancellationToken cancellationToken)
        => ToolResponse<TaskPageDto>.From(await service.ListPageAsync(projectKey, status, offset, limit, cancellationToken));

    [McpServerTool(Name = "task_get_next", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Preview the next unclaimed task. Multi-worker Codex should use task_claim_next instead.")]
    public static async Task<ToolResponse<TaskDto?>> GetNextAsync(
        string projectKey, ITaskService service, CancellationToken cancellationToken)
        => ToolResponse<TaskDto?>.From(await service.GetNextAsync(projectKey, cancellationToken));

    [McpServerTool(Name = "task_claim_next", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Claim the next task for one Codex worker. Expired InProgress leases may be reclaimed.")]
    public static async Task<ToolResponse<TaskDto?>> ClaimNextAsync(
        string projectKey,
        [Description("Stable unique id for this Codex worker/process.")] string workerId,
        string? branch,
        string actor,
        ITaskLeaseService service,
        ToolAuthorizer authorizer,
        CancellationToken cancellationToken)
    {
        actor = authorizer.RequireAndResolveActor(actor, McpCallerRole.Implementer);
        return ToolResponse<TaskDto?>.From(await service.ClaimNextAsync(projectKey, workerId, actor, branch, cancellationToken));
    }

    [McpServerTool(Name = "task_heartbeat", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Renew an active task lease. Send periodically while a Codex worker is implementing.")]
    public static async Task<ToolResponse<TaskDto>> HeartbeatAsync(
        string projectKey, string taskCode, string workerId, string actor,
        ITaskLeaseService service, ToolAuthorizer authorizer, CancellationToken cancellationToken)
    {
        actor = authorizer.RequireAndResolveActor(actor, McpCallerRole.Implementer);
        return ToolResponse<TaskDto>.From(await service.HeartbeatAsync(projectKey, taskCode, workerId, actor, cancellationToken));
    }

    [McpServerTool(Name = "task_start", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Compatibility start operation. Multi-worker Codex should prefer task_claim_next.")]
    public static async Task<ToolResponse<TaskDto>> StartAsync(
        string projectKey, string taskCode, string actor, string? branch,
        ITaskService service, ToolAuthorizer authorizer, CancellationToken cancellationToken)
    {
        actor = authorizer.RequireAndResolveActor(actor, McpCallerRole.Implementer);
        return ToolResponse<TaskDto>.From(await service.StartAsync(projectKey, taskCode, actor, branch, cancellationToken));
    }

    [McpServerTool(Name = "task_add_evidence", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Attach implementation evidence after Codex has changed code and run checks.")]
    public static async Task<ToolResponse<TaskDto>> AddEvidenceAsync(
        string projectKey, string taskCode, string branch, string commitSha, string? pullRequestUrl,
        string[] filesChanged, string[] tests, string[] commands, string? notes, string actor,
        ITaskService service, ToolAuthorizer authorizer, CancellationToken cancellationToken)
    {
        actor = authorizer.RequireAndResolveActor(actor, McpCallerRole.Implementer);
        var evidence = new EvidenceInput(branch, commitSha, pullRequestUrl, filesChanged, tests, commands, notes);
        return ToolResponse<TaskDto>.From(await service.AddEvidenceAsync(projectKey, taskCode, evidence, actor, cancellationToken));
    }

    [McpServerTool(Name = "task_submit_review", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Move an InProgress task with evidence to ReadyForReview. Codex stops after this call.")]
    public static async Task<ToolResponse<TaskDto>> SubmitReviewAsync(
        string projectKey, string taskCode, string actor,
        ITaskService service, ToolAuthorizer authorizer, CancellationToken cancellationToken)
    {
        actor = authorizer.RequireAndResolveActor(actor, McpCallerRole.Implementer);
        return ToolResponse<TaskDto>.From(await service.SubmitForReviewAsync(projectKey, taskCode, actor, cancellationToken));
    }

    [McpServerTool(Name = "task_block", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Mark a task Blocked when implementation cannot proceed.")]
    public static async Task<ToolResponse<TaskDto>> BlockAsync(
        string projectKey, string taskCode, string reason, string actor,
        ITaskService service, ToolAuthorizer authorizer, CancellationToken cancellationToken)
    {
        actor = authorizer.RequireAndResolveActor(actor, McpCallerRole.Implementer);
        return ToolResponse<TaskDto>.From(await service.BlockAsync(projectKey, taskCode, reason, actor, cancellationToken));
    }

    [McpServerTool(Name = "task_resume", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Resume a Blocked task by moving it back to Ready.")]
    public static async Task<ToolResponse<TaskDto>> ResumeAsync(
        string projectKey, string taskCode, string actor,
        ITaskService service, ToolAuthorizer authorizer, CancellationToken cancellationToken)
    {
        actor = authorizer.RequireAndResolveActor(actor, McpCallerRole.Auditor, McpCallerRole.Architect);
        return ToolResponse<TaskDto>.From(await service.ResumeAsync(projectKey, taskCode, actor, cancellationToken));
    }

    [McpServerTool(Name = "task_reopen", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Reopen a Done task as ChangesRequested. Intended for an Auditor or human maintainer.")]
    public static async Task<ToolResponse<TaskDto>> ReopenAsync(
        string projectKey, string taskCode, string reason, string actor,
        ITaskService service, ToolAuthorizer authorizer, CancellationToken cancellationToken)
    {
        actor = authorizer.RequireAndResolveActor(actor, McpCallerRole.Auditor);
        return ToolResponse<TaskDto>.From(await service.ReopenAsync(projectKey, taskCode, reason, actor, cancellationToken));
    }
}
