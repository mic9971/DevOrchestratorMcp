using System.Text.Json;
using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Common.Results;

namespace DevOrchestrator.Application.Services;

internal static class GitHubContractParser
{
    internal const string PlanSchema = "devorchestrator.plan.v1";
    internal const string ReviewSchema = "devorchestrator.review.v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static Result<GitHubPlanContract> ParsePlan(string body)
    {
        var block = ExtractSingleBlock(body, "devorchestrator-plan");
        if (block.IsFailure)
        {
            return Result<GitHubPlanContract>.Failure(block.Error);
        }

        try
        {
            var contract = JsonSerializer.Deserialize<GitHubPlanContract>(block.Value!, JsonOptions);
            if (contract is null)
            {
                return InvalidPlan("Plan contract is empty.");
            }

            if (!string.Equals(contract.Schema, PlanSchema, StringComparison.Ordinal))
            {
                return InvalidPlan($"Unsupported plan schema '{contract.Schema}'. Expected '{PlanSchema}'.");
            }

            if (string.IsNullOrWhiteSpace(contract.ProjectKey))
            {
                return InvalidPlan("projectKey is required.");
            }

            if (contract.Tasks is null || contract.Tasks.Length == 0)
            {
                return InvalidPlan("At least one task is required.");
            }

            var normalizedCodes = contract.Tasks
                .Select(x => x.Code?.Trim().ToUpperInvariant() ?? string.Empty)
                .ToArray();

            if (normalizedCodes.Any(string.IsNullOrWhiteSpace))
            {
                return InvalidPlan("Every task requires a code.");
            }

            var duplicate = normalizedCodes
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(x => x.Count() > 1);

            if (duplicate is not null)
            {
                return InvalidPlan($"Duplicate task code '{duplicate.Key}'.");
            }

            return Result<GitHubPlanContract>.Success(contract);
        }
        catch (JsonException ex)
        {
            return InvalidPlan($"Invalid plan JSON: {ex.Message}");
        }
    }

    public static Result<GitHubReviewContract> ParseReview(string body)
    {
        var block = ExtractSingleBlock(body, "devorchestrator-review");
        if (block.IsFailure)
        {
            return Result<GitHubReviewContract>.Failure(block.Error);
        }

        try
        {
            var contract = JsonSerializer.Deserialize<GitHubReviewContract>(block.Value!, JsonOptions);
            if (contract is null)
            {
                return InvalidReview("Review contract is empty.");
            }

            if (!string.Equals(contract.Schema, ReviewSchema, StringComparison.Ordinal))
            {
                return InvalidReview($"Unsupported review schema '{contract.Schema}'. Expected '{ReviewSchema}'.");
            }

            if (string.IsNullOrWhiteSpace(contract.TaskCode))
            {
                return InvalidReview("taskCode is required.");
            }

            if (!string.Equals(contract.Decision, "Pass", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(contract.Decision, "ChangesRequested", StringComparison.OrdinalIgnoreCase))
            {
                return InvalidReview("decision must be 'Pass' or 'ChangesRequested'.");
            }

            if (string.IsNullOrWhiteSpace(contract.Summary))
            {
                return InvalidReview("summary is required.");
            }

            return Result<GitHubReviewContract>.Success(contract);
        }
        catch (JsonException ex)
        {
            return InvalidReview($"Invalid review JSON: {ex.Message}");
        }
    }

    private static Result<string> ExtractSingleBlock(string body, string language)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return Result<string>.Failure(new Error("bridge.contract.not_found", $"No {language} contract found."));
        }

        var marker = $"```{language}";
        var start = body.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return Result<string>.Failure(new Error("bridge.contract.not_found", $"No {language} contract found."));
        }

        if (body.IndexOf(marker, start + marker.Length, StringComparison.Ordinal) >= 0)
        {
            return Result<string>.Failure(new Error("bridge.contract.invalid", $"Exactly one {language} block is allowed."));
        }

        var contentStart = start + marker.Length;
        while (contentStart < body.Length && (body[contentStart] == '\r' || body[contentStart] == '\n' || char.IsWhiteSpace(body[contentStart])))
        {
            contentStart++;
        }

        var end = body.IndexOf("```", contentStart, StringComparison.Ordinal);
        if (end < 0)
        {
            return Result<string>.Failure(new Error("bridge.contract.invalid", $"The {language} block is not closed."));
        }

        var content = body[contentStart..end].Trim();
        return string.IsNullOrWhiteSpace(content)
            ? Result<string>.Failure(new Error("bridge.contract.invalid", $"The {language} block is empty."))
            : Result<string>.Success(content);
    }

    private static Result<GitHubPlanContract> InvalidPlan(string message)
        => Result<GitHubPlanContract>.Failure(new Error("bridge.plan.invalid", message));

    private static Result<GitHubReviewContract> InvalidReview(string message)
        => Result<GitHubReviewContract>.Failure(new Error("bridge.review.invalid", message));
}
