using System.ComponentModel;
using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Application.Services;
using ModelContextProtocol.Server;

namespace DevOrchestrator.McpServer.Tools;

[McpServerToolType]
public static class GitHubBridgeTools
{
    [McpServerTool(
        Name = "bridge_import_plan_issue",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Import missing orchestrator tasks from a devorchestrator-plan JSON contract in a GitHub issue. Re-importing the same issue skips existing task codes.")]
    public static async Task<ToolResponse<GitHubBridgeImportResult>> ImportPlanIssueAsync(
        [Description("Registered project key.")] string projectKey,
        [Description("GitHub issue number containing the devorchestrator-plan contract.")] int issueNumber,
        IGitHubBridgeService service,
        CancellationToken cancellationToken)
        => ToolResponse<GitHubBridgeImportResult>.From(
            await service.ImportPlanIssueAsync(projectKey, issueNumber, cancellationToken));

    [McpServerTool(
        Name = "bridge_sync_reviews",
        ReadOnly = false,
        Destructive = false,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true)]
    [Description("Apply the latest valid devorchestrator-review GitHub issue comments to tasks currently ready for review. Old comments are ignored.")]
    public static async Task<ToolResponse<GitHubBridgeReviewSyncResult>> SyncReviewsAsync(
        [Description("Registered project key.")] string projectKey,
        [Description("GitHub plan issue number whose comments contain review contracts.")] int issueNumber,
        IGitHubBridgeService service,
        CancellationToken cancellationToken)
        => ToolResponse<GitHubBridgeReviewSyncResult>.From(
            await service.SyncReviewsAsync(projectKey, issueNumber, cancellationToken));
}
