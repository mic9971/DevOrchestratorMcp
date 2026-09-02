using Microsoft.Extensions.Options;

namespace DevOrchestrator.McpServer.Security;

public sealed class ToolAuthorizer(
    IHttpContextAccessor httpContextAccessor,
    IOptions<SecurityOptions> options)
{
    public void Require(params McpCallerRole[] allowedRoles)
    {
        if (!options.Value.RequireAuthentication)
        {
            return;
        }

        var context = httpContextAccessor.HttpContext
            ?? throw new UnauthorizedAccessException("MCP caller context is unavailable.");

        if (!context.Items.TryGetValue(McpApiKeyMiddleware.CallerRoleItemKey, out var value)
            || value is not McpCallerRole role
            || !allowedRoles.Contains(role))
        {
            throw new UnauthorizedAccessException(
                $"MCP caller is not authorized for this tool. Required role: {string.Join(" or ", allowedRoles)}.");
        }
    }
}
