using System.Security.Cryptography;
using System.Text;
using DevOrchestrator.McpServer.Webhooks;
using Microsoft.Extensions.Options;

namespace DevOrchestrator.Architecture.Tests;

public sealed class GitHubWebhookSignatureVerifierTests
{
    [Fact]
    public void Valid_signature_is_accepted_and_modified_payload_is_rejected()
    {
        const string secret = "phase3-test-secret";
        const string payload = "{\"action\":\"edited\"}";
        var verifier = new GitHubWebhookSignatureVerifier(
            Options.Create(new GitHubWebhookOptions { WebhookSecret = secret }));

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = "sha256=" + Convert.ToHexString(
            hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

        Assert.True(verifier.IsValid(payload, signature));
        Assert.False(verifier.IsValid(payload + " ", signature));
    }

    [Fact]
    public void Missing_secret_never_accepts_a_signature()
    {
        var verifier = new GitHubWebhookSignatureVerifier(
            Options.Create(new GitHubWebhookOptions()));

        Assert.False(verifier.IsConfigured);
        Assert.False(verifier.IsValid("{}", "sha256=deadbeef"));
    }
}
