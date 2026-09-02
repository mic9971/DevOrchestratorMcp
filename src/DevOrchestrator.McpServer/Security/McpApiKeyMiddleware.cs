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
        if (!context.Request.Path.StartsWithSegments("/mcp"))
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

        if (string.IsNullOrWhiteSpace(security.ArchitectKey)
            && string.IsNullOrWhiteSpace(security.ImplementerKey)
            && string.IsNullOrWhiteSpace(security.AuditorKey))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new
            {
                error = "MCP authentication is required but no role keys are configured."
            });
            return;
        }

        var suppliedKey = ReadKey(context.Request);
        var role = ResolveRole(suppliedKey, security);
        if (role is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid MCP API key." });
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
        if (Matches(suppliedKey, options.ArchitectKey))
        {
            return McpCallerRole.Architect;
        }

        if (Matches(suppliedKey, options.ImplementerKey))
        {
            return McpCallerRole.Implementer;
        }

        if (Matches(suppliedKey, options.AuditorKey))
        {
            return McpCallerRole.Auditor;
        }

        return null;
    }

    private static bool Matches(string? supplied, string? configured)
    {
        if (string.IsNullOrWhiteSpace(supplied) || string.IsNullOrWhiteSpace(configured))
        {
            return false;
        }

        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        var configuredBytes = Encoding.UTF8.GetBytes(configured);
        return suppliedBytes.Length == configuredBytes.Length
               && CryptographicOperations.FixedTimeEquals(suppliedBytes, configuredBytes);
    }
}
