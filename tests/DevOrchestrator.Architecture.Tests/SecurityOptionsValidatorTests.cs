using DevOrchestrator.McpServer.Security;

namespace DevOrchestrator.Architecture.Tests;

public sealed class SecurityOptionsValidatorTests
{
    private static readonly string ArchitectKey = new('a', 32);
    private static readonly string ImplementerKey = new('i', 32);
    private static readonly string AuditorKey = new('u', 32);

    [Fact]
    public void Authentication_disabled_allows_empty_local_keys()
    {
        var validator = new SecurityOptionsValidator();
        var result = validator.Validate(null, new SecurityOptions { RequireAuthentication = false });
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Authentication_enabled_requires_all_distinct_strong_keys()
    {
        var validator = new SecurityOptionsValidator();
        var valid = validator.Validate(null, new SecurityOptions
        {
            RequireAuthentication = true,
            ArchitectKey = ArchitectKey,
            ImplementerKey = ImplementerKey,
            AuditorKey = AuditorKey
        });
        var duplicate = validator.Validate(null, new SecurityOptions
        {
            RequireAuthentication = true,
            ArchitectKey = ArchitectKey,
            ImplementerKey = ArchitectKey,
            AuditorKey = AuditorKey
        });
        var missing = validator.Validate(null, new SecurityOptions
        {
            RequireAuthentication = true,
            ArchitectKey = ArchitectKey,
            ImplementerKey = ImplementerKey
        });
        Assert.True(valid.Succeeded);
        Assert.True(duplicate.Failed);
        Assert.True(missing.Failed);
    }

    [Fact]
    public void Rotation_allows_distinct_previous_keys_and_rejects_cross_role_reuse()
    {
        var validator = new SecurityOptionsValidator();
        var valid = validator.Validate(null, new SecurityOptions
        {
            RequireAuthentication = true,
            ArchitectKey = ArchitectKey,
            ArchitectPreviousKey = new('b', 32),
            ImplementerKey = ImplementerKey,
            ImplementerPreviousKey = new('j', 32),
            AuditorKey = AuditorKey,
            AuditorPreviousKey = new('v', 32)
        });
        var reused = validator.Validate(null, new SecurityOptions
        {
            RequireAuthentication = true,
            ArchitectKey = ArchitectKey,
            ArchitectPreviousKey = ImplementerKey,
            ImplementerKey = ImplementerKey,
            AuditorKey = AuditorKey
        });
        Assert.True(valid.Succeeded);
        Assert.True(reused.Failed);
    }
}
