using DevOrchestrator.Domain.Identity;

namespace DevOrchestrator.Domain.Tests;

public sealed class IdentityGovernanceTests
{
    [Fact]
    public void Human_roles_are_normalized_and_unknown_roles_are_rejected()
    {
        Assert.Equal(HumanRoles.Auditor, HumanRoles.Normalize("auditor"));
        Assert.Throws<ArgumentException>(() => HumanRoles.Normalize("superuser"));
    }

    [Fact]
    public void Machine_credentials_support_expiry_use_and_revocation_without_plaintext_secret()
    {
        var now = new DateTime(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc);
        var credential = MachineCredential.Create(
            "worker-1",
            new string('a', 64),
            "do_abcd123",
            HumanRoles.Implementer,
            now,
            now.AddDays(30),
            "github:mic9971");

        Assert.True(credential.IsUsable(now));
        Assert.Equal(new string('a', 64), credential.KeyHash);
        credential.MarkUsed(now.AddMinutes(1));
        Assert.Equal(now.AddMinutes(1), credential.LastUsedAtUtc);
        credential.Revoke(now.AddMinutes(2));
        Assert.False(credential.IsUsable(now.AddMinutes(3)));
        Assert.False(credential.IsActive);
        Assert.NotNull(credential.RevokedAtUtc);
    }

    [Fact]
    public void Machine_credentials_cannot_be_admin_credentials()
    {
        var now = DateTime.UtcNow;
        Assert.Throws<ArgumentException>(() => MachineCredential.Create(
            "bad-admin-key",
            new string('b', 64),
            "do_badadmin",
            HumanRoles.Admin,
            now,
            now.AddDays(1),
            "github:admin"));
    }

    [Fact]
    public void Human_user_can_be_disabled_without_destroying_identity_history()
    {
        var now = DateTime.UtcNow;
        var user = HumanIdentityUser.Create("github", "123", "alice", "Alice", "alice@example.com", now);
        Assert.True(user.IsEnabled);
        user.Disable();
        Assert.False(user.IsEnabled);
        user.Enable();
        Assert.True(user.IsEnabled);
        Assert.Equal("github", user.Provider);
        Assert.Equal("123", user.Subject);
    }
}
