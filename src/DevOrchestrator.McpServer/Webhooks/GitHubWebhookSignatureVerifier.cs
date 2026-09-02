using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace DevOrchestrator.McpServer.Webhooks;

public sealed class GitHubWebhookSignatureVerifier(IOptions<GitHubWebhookOptions> options)
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.WebhookSecret);

    public bool IsValid(string payload, string? signature)
    {
        var secret = options.Value.WebhookSecret;
        if (string.IsNullOrWhiteSpace(secret)
            || string.IsNullOrWhiteSpace(signature)
            || !signature.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var digest = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var expected = "sha256=" + Convert.ToHexString(digest).ToLowerInvariant();

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var suppliedBytes = Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant());

        return expectedBytes.Length == suppliedBytes.Length
               && CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes);
    }
}
