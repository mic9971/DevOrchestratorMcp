using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace DevOrchestrator.Infrastructure.GitHub;

internal sealed class GitHubAccessTokenProvider(
    HttpClient httpClient,
    IConfiguration configuration) : IGitHubAccessTokenProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim gate = new(1, 1);
    private string? cachedInstallationToken;
    private DateTimeOffset cachedInstallationTokenExpiresAtUtc;

    public async ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        var appId = configuration["GitHub:AppId"]?.Trim();
        var installationId = configuration["GitHub:InstallationId"]?.Trim();
        var privateKey = configuration["GitHub:PrivateKeyPem"];

        if (string.IsNullOrWhiteSpace(appId)
            || string.IsNullOrWhiteSpace(installationId)
            || string.IsNullOrWhiteSpace(privateKey))
        {
            return ReadPersonalAccessToken();
        }

        if (!string.IsNullOrWhiteSpace(cachedInstallationToken)
            && cachedInstallationTokenExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            return cachedInstallationToken;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(cachedInstallationToken)
                && cachedInstallationTokenExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(2))
            {
                return cachedInstallationToken;
            }

            var jwt = CreateAppJwt(appId, privateKey);
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://api.github.com/app/installations/{installationId}/access_tokens");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd("DevOrchestratorMcp/1.0");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

            using var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var tokenResponse = await JsonSerializer.DeserializeAsync<InstallationTokenResponse>(
                stream,
                JsonOptions,
                cancellationToken)
                ?? throw new InvalidOperationException("GitHub App returned an empty installation-token response.");

            cachedInstallationToken = tokenResponse.Token;
            cachedInstallationTokenExpiresAtUtc = tokenResponse.ExpiresAt;
            return cachedInstallationToken;
        }
        finally
        {
            gate.Release();
        }
    }

    private string? ReadPersonalAccessToken()
    {
        var token = configuration["GitHub:Token"];
        if (string.IsNullOrWhiteSpace(token))
        {
            token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        }

        return string.IsNullOrWhiteSpace(token) ? null : token.Trim();
    }

    private static string CreateAppJwt(string appId, string privateKeyPem)
    {
        var now = DateTimeOffset.UtcNow;
        var header = Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"));
        var payloadJson = JsonSerializer.Serialize(new
        {
            iat = now.AddSeconds(-60).ToUnixTimeSeconds(),
            exp = now.AddMinutes(9).ToUnixTimeSeconds(),
            iss = appId
        });
        var payload = Base64Url(Encoding.UTF8.GetBytes(payloadJson));
        var unsignedToken = $"{header}.{payload}";

        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem.Replace("\\n", "\n", StringComparison.Ordinal));
        var signature = rsa.SignData(
            Encoding.ASCII.GetBytes(unsignedToken),
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        return $"{unsignedToken}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] value)
        => Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed record InstallationTokenResponse(
        string Token,
        [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);
}
