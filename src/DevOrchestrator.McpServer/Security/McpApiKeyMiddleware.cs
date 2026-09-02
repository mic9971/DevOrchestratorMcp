using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace DevOrchestrator.McpServer.Security;

public sealed class McpApiKeyMiddleware(
    RequestDelegate next,
    IOptions<SecurityOptions> options)
{
    public const string CallerRoleItemKey = "devorchestrator.mcp.role";

    public async Task InvokeAsync(HttpContext context)
    {
        var isMcp = context.Request.Path.StartsWithSegments("/mcp");
        var isOperational = context.Request.Path.StartsWithSegments("/ops")
                            || context.Request.Path.StartsWithSegments("/metrics");
        if (!isMcp && !isOperational)
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

        var suppliedKey = ReadKey(context.Request);
        var role = ResolveRole(suppliedKey, security);
        if (role is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid DevOrchestrator API key." });
            return;
        }

        if (isOperational && role != McpCallerRole.Auditor)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new { error = "Auditor role is required for operational endpoints." });
            return;
        }

        context.Items[CallerRoleItemKey] = role.Value;
        await next(context);
    }

    private static string? ReadKey(HttpRequest request)
    {
        if (request.Headers.TryGetValue("Authorization", out var authorization))
        {
            var value = authorization.ToString();
            const string prefix = "Bearer ";
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return value[prefix.Length..].Trim();
            }
        }

        return request.Headers.TryGetValue("X-DevOrchestrator-Key", out var apiKey)
            ? apiKey.ToString().Trim()
            : null;
    }

    private static McpCallerRole? ResolveRole(string? suppliedKey, SecurityOptions options)
    {
        if (MatchesEither(suppliedKey, options.ArchitectKey, options.ArchitectPreviousKey))
            return McpCallerRole.Architect;
        if (MatchesEither(suppliedKey, options.ImplementerKey, options.ImplementerPreviousKey))
            return McpCallerRole.Implementer;
        if (MatchesEither(suppliedKey, options.AuditorKey, options.AuditorPreviousKey))
            return McpCallerRole.Auditor;
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
