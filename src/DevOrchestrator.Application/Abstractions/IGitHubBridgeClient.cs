namespace DevOrchestrator.Application.Abstractions;

public sealed record GitHubIssueSnapshot(
    int Number,
    string Url,
    string Body,
    string Author,
    DateTimeOffset UpdatedAtUtc);

public sealed record GitHubIssueCommentSnapshot(
    long Id,
    string Url,
    string Author,
    string Body,
    DateTimeOffset CreatedAtUtc);

public sealed class GitHubBridgeClientException : Exception
{
    public GitHubBridgeClientException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public interface IGitHubBridgeClient
{
    Task<GitHubIssueSnapshot> GetIssueAsync(
        string repositoryUrl,
        int issueNumber,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<GitHubIssueCommentSnapshot>> GetIssueCommentsAsync(
        string repositoryUrl,
        int issueNumber,
        CancellationToken cancellationToken);
}
