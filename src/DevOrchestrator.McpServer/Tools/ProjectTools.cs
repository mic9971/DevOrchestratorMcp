using System.ComponentModel;
using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Application.Services;
using DevOrchestrator.McpServer.Security;
using ModelContextProtocol.Server;

namespace DevOrchestrator.McpServer.Tools;

[McpServerToolType]
public static class ProjectTools
{
    [McpServerTool(
        Name = "project_register",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Register a target Git repository that ChatGPT plans and Codex implements against.")]
    public static async Task<ToolResponse<ProjectDto>> RegisterAsync(
        [Description("Stable project key, for example novel-platform.")] string projectKey,
        [Description("Human-readable project name.")] string name,
        [Description("Absolute HTTP(S) Git repository URL.")] string repositoryUrl,
        [Description("Default branch, usually main.")] string defaultBranch,
        [Description("Actor hint. Authenticated callers are bound to their server-side role identity.")] string actor,
        IProjectService service,
        ToolAuthorizer authorizer,
        CancellationToken cancellationToken)
    {
        actor = authorizer.RequireAndResolveActor(actor, McpCallerRole.Architect);

        return ToolResponse<ProjectDto>.From(
            await service.RegisterAsync(
                projectKey,
                name,
                repositoryUrl,
                defaultBranch,
                actor,
                cancellationToken));
    }

    [McpServerTool(Name = "project_get", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Get one registered target project.")]
    public static async Task<ToolResponse<ProjectDto>> GetAsync(
        [Description("Project key.")] string projectKey,
        IProjectService service,
        CancellationToken cancellationToken)
        => ToolResponse<ProjectDto>.From(await service.GetAsync(projectKey, cancellationToken));

    [McpServerTool(Name = "project_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("List registered target projects.")]
    public static async Task<ToolResponse<IReadOnlyList<ProjectDto>>> ListAsync(
        IProjectService service,
        CancellationToken cancellationToken)
        => ToolResponse<IReadOnlyList<ProjectDto>>.From(await service.ListAsync(cancellationToken));
}
