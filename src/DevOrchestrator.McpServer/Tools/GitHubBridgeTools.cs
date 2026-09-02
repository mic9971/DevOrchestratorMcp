using System.ComponentModel;
using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Application.Services;
using DevOrchestrator.McpServer.Security;
using ModelContextProtocol.Server;

namespace DevOrchestrator.McpServer.Tools;

[McpServerToolType]
public static class GitHubBridgeTools
{
    [McpServerTool(Name = "bridge_import_plan_issue", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Import missing orchestration tasks from one GitHub Plan Issue using the devorchestrator.plan.v1 contract.")]
    public static async Task<ToolResponse<GitHubBridgeImportResult>> ImportPlanIssueAsync(
        [Description("Registered project key.")] string projectKey,
        [Description("GitHub Plan Issue number.")] int issueNumber,
        IGitHubBridgeService service,
        ToolAuthorizer authorizer,
        CancellationToken cancellationToken)
    {
        authorizer.Require(McpCallerRole.Architect, McpCallerRole.Implementer);
        return ToolResponse<GitHubBridgeImportResult>.From(
            await service.ImportPlanIssueAsync(projectKey, issueNumber, cancellationToken));
    }

    [McpServerTool(Name = "bridge_sync_reviews", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Apply the latest eligible GitHub review-contract comments to ReadyForReview tasks from one Plan Issue.")]
    public static async Task<ToolResponse<GitHubBridgeReviewSyncResult>> SyncReviewsAsync(
        [Description("Registered project key.")] string projectKey,
        [Description("GitHub Plan Issue number.")] int issueNumber,
        IGitHubBridgeService service,
        ToolAuthorizer authorizer,
        CancellationToken cancellationToken)
    {
        authorizer.Require(McpCallerRole.Implementer, McpCallerRole.Auditor);
        return ToolResponse<GitHubBridgeReviewSyncResult>.From(
            await service.SyncReviewsAsync(projectKey, issueNumber, cancellationToken));
    }
}
