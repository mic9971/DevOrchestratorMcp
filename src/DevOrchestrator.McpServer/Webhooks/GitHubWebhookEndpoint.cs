using System.Text;
using System.Text.Json;
using DevOrchestrator.Application.Abstractions;
using DevOrchestrator.Application.Contracts;

namespace DevOrchestrator.McpServer.Webhooks;

public static class GitHubWebhookEndpoint
{
    public static RouteHandlerBuilder MapGitHubWebhook(this IEndpointRouteBuilder endpoints)
        => endpoints.MapPost(
            "/webhooks/github",
            async (
                HttpRequest request,
                GitHubWebhookSignatureVerifier signatureVerifier,
                IGitHubWebhookInbox inbox,
                CancellationToken cancellationToken) =>
            {
                if (!signatureVerifier.IsConfigured)
                {
                    return Results.Json(new { error = "GitHub webhook secret is not configured." },
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }

                using var reader = new StreamReader(request.Body, Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                var payload = await reader.ReadToEndAsync(cancellationToken);
                var signature = request.Headers["X-Hub-Signature-256"].FirstOrDefault();
                if (!signatureVerifier.IsValid(payload, signature)) return Results.Unauthorized();

                var eventName = request.Headers["X-GitHub-Event"].FirstOrDefault()?.Trim();
                var deliveryId = request.Headers["X-GitHub-Delivery"].FirstOrDefault()?.Trim();
                if (string.IsNullOrWhiteSpace(eventName) || string.IsNullOrWhiteSpace(deliveryId))
                    return Results.BadRequest(new { error = "Missing GitHub event or delivery headers." });

                if (eventName.Equals("ping", StringComparison.OrdinalIgnoreCase))
                    return Results.Ok(new { status = "pong", deliveryId });

                if (!eventName.Equals("issues", StringComparison.OrdinalIgnoreCase)
                    && !eventName.Equals("issue_comment", StringComparison.OrdinalIgnoreCase))
                    return Results.Accepted(value: new { deliveryId, eventName, outcome = "ignored" });

                try
                {
                    using var document = JsonDocument.Parse(payload);
                    var root = document.RootElement;
                    var action = root.TryGetProperty("action", out var actionElement)
                        ? actionElement.GetString() ?? string.Empty
                        : string.Empty;
                    var repositoryUrl = TryReadRepositoryUrl(root);
                    var issueNumber = TryReadIssueNumber(root);
                    if (string.IsNullOrWhiteSpace(repositoryUrl) || issueNumber <= 0)
                        return Results.BadRequest(new { error = "Webhook payload is missing repository or issue data." });

                    var queued = await inbox.EnqueueAsync(
                        new GitHubWebhookNotification(deliveryId, eventName, action, repositoryUrl, issueNumber),
                        cancellationToken);
                    return Results.Accepted(value: new { deliveryId, eventName, outcome = queued ? "queued" : "duplicate" });
                }
                catch (JsonException)
                {
                    return Results.BadRequest(new { error = "Webhook payload is not valid JSON." });
                }
            });

    private static string? TryReadRepositoryUrl(JsonElement root)
    {
        if (!root.TryGetProperty("repository", out var repository)) return null;
        if (repository.TryGetProperty("html_url", out var htmlUrl)) return htmlUrl.GetString();
        return repository.TryGetProperty("clone_url", out var cloneUrl) ? cloneUrl.GetString() : null;
    }

    private static int TryReadIssueNumber(JsonElement root)
    {
        if (!root.TryGetProperty("issue", out var issue)
            || !issue.TryGetProperty("number", out var number)
            || !number.TryGetInt32(out var value)) return 0;
        return value;
    }
}
