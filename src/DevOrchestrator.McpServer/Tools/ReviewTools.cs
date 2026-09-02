using System.ComponentModel;
using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Application.Services;
using ModelContextProtocol.Server;

namespace DevOrchestrator.McpServer.Tools;

[McpServerToolType]
public static class ReviewTools
{
    [McpServerTool(
        Name = "review_submit",
        ReadOnly = false,
        Destructive = false,
        Idempotent = false,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description("Audit a ReadyForReview task. PASS can complete the task; ChangesRequested sends it back to Codex.")]
    public static async Task<ToolResponse<TaskDto>> SubmitAsync(
        [Description("Project key.")] string projectKey,
        [Description("Task code.")] string taskCode,
        [Description("Pass or ChangesRequested.")] string decision,
        [Description("Concise audit summary comparing requirement, acceptance criteria, diff, and tests.")] string summary,
        [Description("Concrete audit findings. Empty array is valid for a clean pass.")] string[] findings,
        [Description("Reviewer actor, for example chatgpt-auditor.")] string actor,
        [Description("When true, a Pass transitions the task directly to Done and unlocks dependents.")] bool completeOnPass,
        IReviewService service,
        CancellationToken cancellationToken)
        => ToolResponse<TaskDto>.From(
            await service.SubmitAsync(
                projectKey,
                taskCode,
                decision,
                summary,
                findings,
                actor,
                completeOnPass,
                cancellationToken));
}
