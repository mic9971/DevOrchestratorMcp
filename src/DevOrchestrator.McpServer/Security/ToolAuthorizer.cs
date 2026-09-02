using Microsoft.Extensions.Options;

namespace DevOrchestrator.McpServer.Security;

public sealed class ToolAuthorizer(
    IHttpContextAccessor httpContextAccessor,
    IOptions<SecurityOptions> options)
{
    public void Require(params McpCallerRole[] allowedRoles)
    {
        _ = ResolveRole(allowedRoles);
    }

    public string RequireAndResolveActor(
        string requestedActor,
        params McpCallerRole[] allowedRoles)
    {
        if (!options.Value.RequireAuthentication)
        {
            return requestedActor;
        }

        var role = ResolveRole(allowedRoles)
            ?? throw new UnauthorizedAccessException("MCP caller role is unavailable.");

        return role switch
        {
            McpCallerRole.Architect => "mcp:architect",
            McpCallerRole.Implementer => "mcp:implementer",
            McpCallerRole.Auditor => "mcp:auditor",
            _ => throw new UnauthorizedAccessException("Unknown MCP caller role.")
        };
    }

    private McpCallerRole? ResolveRole(IReadOnlyCollection<McpCallerRole> allowedRoles)
    {
        if (!options.Value.RequireAuthentication)
        {
            return null;
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

        return role;
    }
}
