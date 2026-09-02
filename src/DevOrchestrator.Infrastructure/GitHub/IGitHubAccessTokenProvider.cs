namespace DevOrchestrator.Infrastructure.GitHub;

internal interface IGitHubAccessTokenProvider
{
    ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken);
}
