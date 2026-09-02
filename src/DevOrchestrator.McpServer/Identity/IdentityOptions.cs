using System.Security.Claims;

namespace DevOrchestrator.McpServer.Identity;

public sealed class IdentityOptions
{
    public const string SectionName = "Identity";

    public GitHubIdentityOptions GitHub { get; set; } = new();
    public string[] BootstrapGitHubLogins { get; set; } = [];

    public bool GitHubConfigured
        => !string.IsNullOrWhiteSpace(GitHub.ClientId)
           && !string.IsNullOrWhiteSpace(GitHub.ClientSecret);
}

public sealed class GitHubIdentityOptions
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}

public static class HumanIdentityAccess
{
    public const string UserIdClaim = "devorchestrator:user_id";
    public const string ProviderClaim = "devorchestrator:provider";
    public const string LoginClaim = "devorchestrator:login";

    public static bool IsHuman(ClaimsPrincipal user)
        => user.Identity?.IsAuthenticated == true && user.HasClaim(x => x.Type == UserIdClaim);

    public static bool IsAdmin(ClaimsPrincipal user)
        => IsHuman(user) && user.IsInRole(DevOrchestrator.Domain.Identity.HumanRoles.Admin);

    public static bool CanReadControlPlane(ClaimsPrincipal user)
        => IsHuman(user) && user.Claims.Any(x => x.Type == ClaimTypes.Role);

    public static bool CanOperate(ClaimsPrincipal user)
        => IsAdmin(user) || (IsHuman(user) && user.IsInRole(DevOrchestrator.Domain.Identity.HumanRoles.Auditor));

    public static string Actor(ClaimsPrincipal user)
    {
        var provider = user.FindFirstValue(ProviderClaim) ?? "human";
        var login = user.FindFirstValue(LoginClaim) ?? user.Identity?.Name ?? "unknown";
        return $"{provider}:{login}";
    }

    public static Guid? UserId(ClaimsPrincipal user)
        => Guid.TryParse(user.FindFirstValue(UserIdClaim), out var id) ? id : null;
}
