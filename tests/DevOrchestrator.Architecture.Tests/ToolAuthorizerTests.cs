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
    public void Local_authentication_disabled_mode_preserves_poc_compatibility()
    {
        var authorizer = new ToolAuthorizer(
            new HttpContextAccessor(),
            Options.Create(new SecurityOptions { RequireAuthentication = false }));

        authorizer.Require(McpCallerRole.Auditor);
    }
}
