using System.Security.Cryptography;
using System.Text;
using DevOrchestrator.Domain.Identity;
using DevOrchestrator.Infrastructure.Persistence;
using DevOrchestrator.McpServer.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevOrchestrator.McpServer.Security;

public sealed class McpApiKeyMiddleware(
    RequestDelegate next,
    IOptions<SecurityOptions> options)
{
    public const string CallerRoleItemKey = "devorchestrator.mcp.role";
    public const string CallerIdentityItemKey = "devorchestrator.caller.identity";

    public async Task InvokeAsync(HttpContext context, OrchestratorDbContext db)
    {
        var isMcp = context.Request.Path.StartsWithSegments("/mcp");
        var isControlApi = context.Request.Path.StartsWithSegments("/control/api");
        var isOperational = context.Request.Path.StartsWithSegments("/ops")
                            || context.Request.Path.StartsWithSegments("/metrics");
        if (!isMcp && !isOperational && !isControlApi)
        {
            await next(context);
            return;
        }

        var security = options.Value;
        if (!security.RequireAuthentication)
        {
            await next(context);
            return;
        }

        // Human browser sessions and machine credentials are deliberately separate.
        // Humans can never use the MCP transport; MCP always requires a machine credential.
        if (!isMcp && HumanIdentityAccess.IsHuman(context.User))
        {
            var allowed = isControlApi
                ? HumanIdentityAccess.CanReadControlPlane(context.User)
                : HumanIdentityAccess.CanOperate(context.User);
            if (!allowed)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = isControlApi ? "identity.role_required" : "identity.auditor_required" });
                return;
            }

            context.Items[CallerIdentityItemKey] = HumanIdentityAccess.Actor(context.User);
            await next(context);
            return;
        }

        var suppliedKey = ReadKey(context.Request);
        var role = ResolveConfiguredRole(suppliedKey, security);
        string callerIdentity;

        if (role.HasValue)
        {
            callerIdentity = $"config:{role.Value.ToString().ToLowerInvariant()}";
        }
        else
        {
            var dynamic = await ResolveDynamicCredentialAsync(suppliedKey, db, context.RequestAborted);
            role = dynamic.Role;
            callerIdentity = dynamic.Identity ?? string.Empty;
        }

        if (role is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid DevOrchestrator machine credential." });
            return;
        }

        if ((isOperational || isControlApi) && role != McpCallerRole.Auditor)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Auditor machine role is required for operational endpoints." });
            return;
        }

        context.Items[CallerRoleItemKey] = role.Value;
        context.Items[CallerIdentityItemKey] = callerIdentity;
        await next(context);
    }

    private static async Task<(McpCallerRole? Role, string? Identity)> ResolveDynamicCredentialAsync(
        string? suppliedKey,
        OrchestratorDbContext db,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(suppliedKey)) return (null, null);
        var hash = IdentityEndpointExtensions.Hash(suppliedKey);
        var credential = await db.MachineCredentials.SingleOrDefaultAsync(x => x.KeyHash == hash, cancellationToken);
        if (credential is null || !credential.IsUsable(DateTime.UtcNow)) return (null, null);

        var role = credential.Role switch
        {
            HumanRoles.Architect => McpCallerRole.Architect,
            HumanRoles.Implementer => McpCallerRole.Implementer,
            HumanRoles.Auditor => McpCallerRole.Auditor,
            _ => (McpCallerRole?)null
        };
        if (!role.HasValue) return (null, null);

        var now = DateTime.UtcNow;
        if (!credential.LastUsedAtUtc.HasValue || credential.LastUsedAtUtc.Value < now.AddMinutes(-5))
        {
            credential.MarkUsed(now);
            await db.SaveChangesAsync(cancellationToken);
        }
        return (role, $"credential:{credential.Id}");
    }

    private static string? ReadKey(HttpRequest request)
    {
        if (request.Headers.TryGetValue("Authorization", out var authorization))
        {
            var value = authorization.ToString();
            const string prefix = "Bearer ";
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return value[prefix.Length..].Trim();
        }

        return request.Headers.TryGetValue("X-DevOrchestrator-Key", out var apiKey)
            ? apiKey.ToString().Trim()
            : null;
    }

    private static McpCallerRole? ResolveConfiguredRole(string? suppliedKey, SecurityOptions options)
    {
        if (MatchesEither(suppliedKey, options.ArchitectKey, options.ArchitectPreviousKey)) return McpCallerRole.Architect;
        if (MatchesEither(suppliedKey, options.ImplementerKey, options.ImplementerPreviousKey)) return McpCallerRole.Implementer;
        if (MatchesEither(suppliedKey, options.AuditorKey, options.AuditorPreviousKey)) return McpCallerRole.Auditor;
        return null;
    }

    private static bool MatchesEither(string? supplied, string? current, string? previous)
        => Matches(supplied, current) || Matches(supplied, previous);

    private static bool Matches(string? supplied, string? configured)
    {
        if (string.IsNullOrWhiteSpace(supplied) || string.IsNullOrWhiteSpace(configured)) return false;
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        var configuredBytes = Encoding.UTF8.GetBytes(configured);
        return suppliedBytes.Length == configuredBytes.Length
               && CryptographicOperations.FixedTimeEquals(suppliedBytes, configuredBytes);
    }
}
