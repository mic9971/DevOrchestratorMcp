using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using DevOrchestrator.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace DevOrchestrator.Infrastructure.GitHub;

internal sealed class GitHubBridgeClient(
    HttpClient httpClient,
    IConfiguration configuration) : IGitHubBridgeClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<GitHubIssueSnapshot> GetIssueAsync(
        string repositoryUrl,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        var repository = ParseRepository(repositoryUrl);
        using var request = CreateRequest(
            HttpMethod.Get,
            $"https://api.github.com/repos/{repository.Owner}/{repository.Name}/issues/{issueNumber}");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var issue = await JsonSerializer.DeserializeAsync<IssueResponse>(stream, JsonOptions, cancellationToken)
                    ?? throw new InvalidOperationException("GitHub returned an empty issue response.");

        return new GitHubIssueSnapshot(
            issue.Number,
            issue.HtmlUrl,
            issue.Body ?? string.Empty,
            issue.User?.Login ?? "unknown",
            issue.UpdatedAt);
    }

    public async Task<IReadOnlyList<GitHubIssueCommentSnapshot>> GetIssueCommentsAsync(
        string repositoryUrl,
        int issueNumber,
        CancellationToken cancellationToken)
    {
        var repository = ParseRepository(repositoryUrl);
        var results = new List<GitHubIssueCommentSnapshot>();

        for (var page = 1; ; page++)
        {
            using var request = CreateRequest(
                HttpMethod.Get,
                $"https://api.github.com/repos/{repository.Owner}/{repository.Name}/issues/{issueNumber}/comments?per_page=100&page={page}");
            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var comments = await JsonSerializer.DeserializeAsync<CommentResponse[]>(stream, JsonOptions, cancellationToken)
                           ?? [];

            results.AddRange(comments.Select(comment => new GitHubIssueCommentSnapshot(
                comment.Id,
                comment.HtmlUrl,
                comment.User?.Login ?? "unknown",
                comment.Body ?? string.Empty,
                comment.CreatedAt)));

            if (comments.Length < 100)
            {
                break;
            }
        }

        return results;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("DevOrchestratorMcp/1.0");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        var token = configuration["GitHub:Token"];
        if (string.IsNullOrWhiteSpace(token))
        {
            token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        }

        return request;
    }

    private static RepositoryCoordinates ParseRepository(string repositoryUrl)
    {
        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Phase 2 GitHub Bridge currently supports github.com repository URLs only.");
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2)
        {
            throw new InvalidOperationException("Repository URL must have the form https://github.com/{owner}/{repo}.");
        }

        var repo = segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? segments[1][..^4]
            : segments[1];

        return new RepositoryCoordinates(segments[0], repo);
    }

    private sealed record RepositoryCoordinates(string Owner, string Name);

    private sealed record IssueResponse(
        int Number,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        string? Body,
        UserResponse? User,
        [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

    private sealed record CommentResponse(
        long Id,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        string? Body,
        UserResponse? User,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

    private sealed record UserResponse(string Login);
}
