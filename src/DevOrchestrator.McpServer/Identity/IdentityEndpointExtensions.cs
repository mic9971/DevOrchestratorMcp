using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevOrchestrator.Domain.Identity;
using DevOrchestrator.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevOrchestrator.McpServer.Identity;

public static class IdentityEndpointExtensions
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/auth/status", GetStatus);
        endpoints.MapGet("/auth/login", Login);
        endpoints.MapPost("/auth/logout", LogoutAsync);
        endpoints.MapGet("/auth/denied", () => Results.Json(new { error = "identity.access_denied" }, statusCode: StatusCodes.Status403Forbidden));

        endpoints.MapGet("/control/api/users", ListUsersAsync);
        endpoints.MapPost("/control/api/users/{userId:guid}/roles", SetRolesAsync);
        endpoints.MapPost("/control/api/users/{userId:guid}/enabled", SetEnabledAsync);
        endpoints.MapGet("/control/api/machine-credentials", ListMachineCredentialsAsync);
        endpoints.MapPost("/control/api/machine-credentials", CreateMachineCredentialAsync);
        endpoints.MapPost("/control/api/machine-credentials/{credentialId:guid}/revoke", RevokeMachineCredentialAsync);
        endpoints.MapPost("/control/api/machine-credentials/{credentialId:guid}/rotate", RotateMachineCredentialAsync);
        endpoints.MapGet("/control/api/security-audit", ListSecurityAuditAsync);
        return endpoints;
    }

    private static IResult GetStatus(HttpContext context, IOptions<IdentityOptions> options)
        => Results.Ok(new
        {
            githubConfigured = options.Value.GitHubConfigured,
            authenticated = HumanIdentityAccess.IsHuman(context.User),
            user = HumanIdentityAccess.IsHuman(context.User) ? new
            {
                id = HumanIdentityAccess.UserId(context.User),
                login = context.User.FindFirst(HumanIdentityAccess.LoginClaim)?.Value,
                name = context.User.Identity?.Name,
                provider = context.User.FindFirst(HumanIdentityAccess.ProviderClaim)?.Value,
                roles = context.User.Claims.Where(x => x.Type == System.Security.Claims.ClaimTypes.Role).Select(x => x.Value).Distinct().OrderBy(x => x).ToArray()
            } : null
        });

    private static IResult Login(HttpContext context, IOptions<IdentityOptions> options, string? returnUrl)
    {
        if (!options.Value.GitHubConfigured)
            return Results.Json(new { error = "identity.github_not_configured" }, statusCode: StatusCodes.Status503ServiceUnavailable);

        var redirect = IsSafeReturnUrl(returnUrl) ? returnUrl! : "/control/session.html";
        return Results.Challenge(new AuthenticationProperties { RedirectUri = redirect }, [IdentityAuthentication.GitHubScheme]);
    }

    private static async Task<IResult> LogoutAsync(HttpContext context)
    {
        if (HumanIdentityAccess.IsHuman(context.User))
        {
            var db = context.RequestServices.GetRequiredService<OrchestratorDbContext>();
            db.SecurityAuditEvents.Add(SecurityAuditEvent.Create(
                HumanIdentityAccess.Actor(context.User), "human", "identity.logout", "session", HumanIdentityAccess.UserId(context.User)?.ToString() ?? "unknown", DateTime.UtcNow,
                ipAddress: context.Connection.RemoteIpAddress?.ToString()));
            await db.SaveChangesAsync(context.RequestAborted);
        }
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Ok(new { signedOut = true });
    }

    private static async Task<IResult> ListUsersAsync(HttpContext context, OrchestratorDbContext db, CancellationToken cancellationToken)
    {
        if (!HumanIdentityAccess.IsAdmin(context.User)) return AdminRequired();
        var users = await db.HumanIdentityUsers.AsNoTracking().OrderBy(x => x.Login).ToListAsync(cancellationToken);
        var roles = await db.HumanIdentityRoles.AsNoTracking().ToListAsync(cancellationToken);
        return Results.Ok(users.Select(user => new
        {
            user.Id,
            user.Provider,
            user.Subject,
            user.Login,
            user.DisplayName,
            user.Email,
            user.IsEnabled,
            user.CreatedAtUtc,
            user.LastLoginAtUtc,
            roles = roles.Where(x => x.UserId == user.Id).Select(x => x.Role).OrderBy(x => x).ToArray()
        }));
    }

    private static async Task<IResult> SetRolesAsync(
        Guid userId,
        SetRolesRequest request,
        HttpContext context,
        OrchestratorDbContext db,
        CancellationToken cancellationToken)
    {
        if (!HumanIdentityAccess.IsAdmin(context.User)) return AdminRequired();
        var user = await db.HumanIdentityUsers.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null) return Results.NotFound(new { error = "identity.user_not_found" });

        string[] roles;
        try { roles = (request.Roles ?? []).Select(HumanRoles.Normalize).Distinct(StringComparer.Ordinal).OrderBy(x => x).ToArray(); }
        catch (ArgumentException ex) { return Results.BadRequest(new { error = "identity.invalid_role", message = ex.Message }); }

        if (HumanIdentityAccess.UserId(context.User) == userId && !roles.Contains(HumanRoles.Admin, StringComparer.Ordinal))
            return Results.BadRequest(new { error = "identity.self_admin_required", message = "An administrator cannot remove their own Admin role." });

        var before = await db.HumanIdentityRoles.Where(x => x.UserId == userId).Select(x => x.Role).ToArrayAsync(cancellationToken);
        await db.HumanIdentityRoles.Where(x => x.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        var actor = HumanIdentityAccess.Actor(context.User);
        foreach (var role in roles) db.HumanIdentityRoles.Add(HumanIdentityRole.Create(userId, role, actor, DateTime.UtcNow));
        db.SecurityAuditEvents.Add(SecurityAuditEvent.Create(
            actor, "human", "identity.roles_changed", "user", userId.ToString(), DateTime.UtcNow,
            request.Reason, JsonSerializer.Serialize(before), JsonSerializer.Serialize(roles), context.Connection.RemoteIpAddress?.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { userId, roles });
    }

    private static async Task<IResult> SetEnabledAsync(
        Guid userId,
        SetEnabledRequest request,
        HttpContext context,
        OrchestratorDbContext db,
        CancellationToken cancellationToken)
    {
        if (!HumanIdentityAccess.IsAdmin(context.User)) return AdminRequired();
        if (HumanIdentityAccess.UserId(context.User) == userId && !request.Enabled)
            return Results.BadRequest(new { error = "identity.self_disable_forbidden" });
        var user = await db.HumanIdentityUsers.SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (user is null) return Results.NotFound(new { error = "identity.user_not_found" });
        var before = user.IsEnabled;
        if (request.Enabled) user.Enable(); else user.Disable();
        var actor = HumanIdentityAccess.Actor(context.User);
        db.SecurityAuditEvents.Add(SecurityAuditEvent.Create(
            actor, "human", request.Enabled ? "identity.user_enabled" : "identity.user_disabled", "user", userId.ToString(), DateTime.UtcNow,
            request.Reason, JsonSerializer.Serialize(new { enabled = before }), JsonSerializer.Serialize(new { enabled = request.Enabled }), context.Connection.RemoteIpAddress?.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { userId, enabled = request.Enabled });
    }

    private static async Task<IResult> ListMachineCredentialsAsync(HttpContext context, OrchestratorDbContext db, CancellationToken cancellationToken)
    {
        if (!HumanIdentityAccess.IsAdmin(context.User)) return AdminRequired();
        var now = DateTime.UtcNow;
        var credentials = await db.MachineCredentials.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
        return Results.Ok(credentials.Select(x => new
        {
            x.Id,
            x.Name,
            x.KeyPrefix,
            x.Role,
            x.IsActive,
            usable = x.IsUsable(now),
            x.CreatedAtUtc,
            x.ExpiresAtUtc,
            x.LastUsedAtUtc,
            x.RevokedAtUtc,
            x.CreatedBy
        }));
    }

    private static async Task<IResult> CreateMachineCredentialAsync(
        CreateMachineCredentialRequest request,
        HttpContext context,
        OrchestratorDbContext db,
        CancellationToken cancellationToken)
    {
        if (!HumanIdentityAccess.IsAdmin(context.User)) return AdminRequired();
        return await CreateMachineCredentialCoreAsync(request.Name, request.Role, request.ExpiresInDays, context, db, null, cancellationToken);
    }

    private static async Task<IResult> RevokeMachineCredentialAsync(
        Guid credentialId,
        RevokeCredentialRequest request,
        HttpContext context,
        OrchestratorDbContext db,
        CancellationToken cancellationToken)
    {
        if (!HumanIdentityAccess.IsAdmin(context.User)) return AdminRequired();
        var credential = await db.MachineCredentials.SingleOrDefaultAsync(x => x.Id == credentialId, cancellationToken);
        if (credential is null) return Results.NotFound(new { error = "identity.credential_not_found" });
        credential.Revoke(DateTime.UtcNow);
        var actor = HumanIdentityAccess.Actor(context.User);
        db.SecurityAuditEvents.Add(SecurityAuditEvent.Create(
            actor, "human", "credential.revoked", "machine_credential", credentialId.ToString(), DateTime.UtcNow,
            request.Reason, ipAddress: context.Connection.RemoteIpAddress?.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new { credentialId, revoked = true });
    }

    private static async Task<IResult> RotateMachineCredentialAsync(
        Guid credentialId,
        RotateCredentialRequest request,
        HttpContext context,
        OrchestratorDbContext db,
        CancellationToken cancellationToken)
    {
        if (!HumanIdentityAccess.IsAdmin(context.User)) return AdminRequired();
        var existing = await db.MachineCredentials.SingleOrDefaultAsync(x => x.Id == credentialId, cancellationToken);
        if (existing is null) return Results.NotFound(new { error = "identity.credential_not_found" });
        existing.Revoke(DateTime.UtcNow);
        return await CreateMachineCredentialCoreAsync(existing.Name, existing.Role, request.ExpiresInDays, context, db, credentialId, cancellationToken, request.Reason);
    }

    private static async Task<IResult> CreateMachineCredentialCoreAsync(
        string name,
        string role,
        int? expiresInDays,
        HttpContext context,
        OrchestratorDbContext db,
        Guid? rotatedFrom,
        CancellationToken cancellationToken,
        string? reason = null)
    {
        if (expiresInDays is <= 0 or > 365)
            return Results.BadRequest(new { error = "identity.invalid_expiry", message = "expiresInDays must be between 1 and 365." });

        var now = DateTime.UtcNow;
        var secret = GenerateSecret();
        var hash = Hash(secret);
        var prefix = secret[..Math.Min(10, secret.Length)];
        var actor = HumanIdentityAccess.Actor(context.User);
        MachineCredential credential;
        try
        {
            credential = MachineCredential.Create(name, hash, prefix, role, now, expiresInDays.HasValue ? now.AddDays(expiresInDays.Value) : null, actor);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = "identity.invalid_credential", message = ex.Message });
        }

        db.MachineCredentials.Add(credential);
        db.SecurityAuditEvents.Add(SecurityAuditEvent.Create(
            actor, "human", rotatedFrom.HasValue ? "credential.rotated" : "credential.created", "machine_credential", credential.Id.ToString(), now,
            reason, rotatedFrom.HasValue ? JsonSerializer.Serialize(new { rotatedFrom }) : null,
            JsonSerializer.Serialize(new { credential.Name, credential.Role, credential.ExpiresAtUtc, credential.KeyPrefix }), context.Connection.RemoteIpAddress?.ToString()));
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new
        {
            credential.Id,
            credential.Name,
            credential.Role,
            credential.KeyPrefix,
            credential.ExpiresAtUtc,
            secret,
            warning = "This secret is returned once. Store it securely; only its SHA-256 hash is persisted."
        });
    }

    private static async Task<IResult> ListSecurityAuditAsync(int? limit, HttpContext context, OrchestratorDbContext db, CancellationToken cancellationToken)
    {
        if (!HumanIdentityAccess.IsAdmin(context.User)) return AdminRequired();
        var take = Math.Clamp(limit ?? 100, 1, 500);
        var events = await db.SecurityAuditEvents.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(take).ToListAsync(cancellationToken);
        return Results.Ok(events);
    }

    public static string Hash(string secret)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    private static string GenerateSecret()
        => "do_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool IsSafeReturnUrl(string? returnUrl)
        => !string.IsNullOrWhiteSpace(returnUrl)
           && returnUrl.StartsWith("/control", StringComparison.Ordinal)
           && !returnUrl.StartsWith("//", StringComparison.Ordinal);

    private static IResult AdminRequired()
        => Results.Json(new { error = "identity.admin_required" }, statusCode: StatusCodes.Status403Forbidden);

    public sealed record SetRolesRequest(string[]? Roles, string? Reason);
    public sealed record SetEnabledRequest(bool Enabled, string? Reason);
    public sealed record CreateMachineCredentialRequest(string Name, string Role, int? ExpiresInDays);
    public sealed record RevokeCredentialRequest(string? Reason);
    public sealed record RotateCredentialRequest(int? ExpiresInDays, string? Reason);
}
