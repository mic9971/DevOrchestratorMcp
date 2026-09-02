using DevOrchestrator.McpServer.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace DevOrchestrator.Architecture.Tests;

public sealed class ToolAuthorizerTests
{
    [Fact]
    public void Matching_authenticated_role_is_allowed()
    {
        var context = new DefaultHttpContext();
        context.Items[McpApiKeyMiddleware.CallerRoleItemKey] = McpCallerRole.Implementer;
        var accessor = new HttpContextAccessor { HttpContext = context };
        var authorizer = new ToolAuthorizer(
            accessor,
            Options.Create(new SecurityOptions { RequireAuthentication = true }));

        authorizer.Require(McpCallerRole.Implementer);
    }

    [Fact]
    public void Wrong_authenticated_role_is_rejected()
    {
        var context = new DefaultHttpContext();
        context.Items[McpApiKeyMiddleware.CallerRoleItemKey] = McpCallerRole.Implementer;
        var accessor = new HttpContextAccessor { HttpContext = context };
        var authorizer = new ToolAuthorizer(
            accessor,
            Options.Create(new SecurityOptions { RequireAuthentication = true }));

        Assert.Throws<UnauthorizedAccessException>(
            () => authorizer.Require(McpCallerRole.Auditor));
    }

    [Fact]
    public void Authenticated_actor_is_derived_from_role_not_spoofable_input()
    {
        var context = new DefaultHttpContext();
        context.Items[McpApiKeyMiddleware.CallerRoleItemKey] = McpCallerRole.Implementer;
        var accessor = new HttpContextAccessor { HttpContext = context };
        var authorizer = new ToolAuthorizer(
            accessor,
            Options.Create(new SecurityOptions { RequireAuthentication = true }));

        var actor = authorizer.RequireAndResolveActor(
            "chatgpt-auditor",
            McpCallerRole.Implementer);

        Assert.Equal("mcp:implementer", actor);
    }

    [Fact]
    public void Local_authentication_disabled_mode_preserves_poc_actor_compatibility()
    {
        var authorizer = new ToolAuthorizer(
            new HttpContextAccessor(),
            Options.Create(new SecurityOptions { RequireAuthentication = false }));

        var actor = authorizer.RequireAndResolveActor(
            "local-developer",
            McpCallerRole.Auditor);

        Assert.Equal("local-developer", actor);
    }
}
