using DevOrchestrator.Application.Services;

namespace DevOrchestrator.Application.Tests;

public sealed class GitHubContractParserTests
{
    [Fact]
    public void ParsePlan_accepts_valid_contract_with_surrounding_markdown()
    {
        const string body = """
            Planning notes before the contract.

            ```devorchestrator-plan
            {
              "schema": "devorchestrator.plan.v1",
              "projectKey": "novel-platform",
              "tasks": [
                {
                  "code": "P2-001",
                  "title": "Bridge task",
                  "objective": "Implement the bridge.",
                  "acceptanceCriteria": ["Build passes"]
                }
              ]
            }
            ```

            Notes after the contract.
            """;

        var result = GitHubContractParser.ParsePlan(body);

        Assert.True(result.IsSuccess);
        Assert.Equal("novel-platform", result.Value!.ProjectKey);
        Assert.Single(result.Value.Tasks);
        Assert.Equal("P2-001", result.Value.Tasks[0].Code);
    }

    [Fact]
    public void ParsePlan_rejects_duplicate_task_codes_case_insensitively()
    {
        const string body = """
            ```devorchestrator-plan
            {
              "schema": "devorchestrator.plan.v1",
              "projectKey": "novel-platform",
              "tasks": [
                { "code": "P2-001", "title": "A", "objective": "A", "acceptanceCriteria": ["A"] },
                { "code": "p2-001", "title": "B", "objective": "B", "acceptanceCriteria": ["B"] }
              ]
            }
            ```
            """;

        var result = GitHubContractParser.ParsePlan(body);

        Assert.True(result.IsFailure);
        Assert.Equal("bridge.plan.invalid", result.Error.Code);
    }

    [Fact]
    public void ParseReview_rejects_unknown_decision()
    {
        const string body = """
            ```devorchestrator-review
            {
              "schema": "devorchestrator.review.v1",
              "taskCode": "P2-001",
              "decision": "Maybe",
              "summary": "Not sure"
            }
            ```
            """;

        var result = GitHubContractParser.ParseReview(body);

        Assert.True(result.IsFailure);
        Assert.Equal("bridge.review.invalid", result.Error.Code);
    }

    [Fact]
    public void ParseReview_treats_plain_comment_as_not_a_contract()
    {
        var result = GitHubContractParser.ParseReview("Looks good to me.");

        Assert.True(result.IsFailure);
        Assert.Equal("bridge.contract.not_found", result.Error.Code);
    }
}
