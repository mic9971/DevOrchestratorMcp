using DevOrchestrator.Application;
using DevOrchestrator.Infrastructure;
using DevOrchestrator.Infrastructure.Persistence;
using DevOrchestrator.McpServer.Security;
using DevOrchestrator.McpServer.Webhooks;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection("Security"));
builder.Services.Configure<GitHubWebhookOptions>(builder.Configuration.GetSection("GitHub"));
builder.Services.AddScoped<ToolAuthorizer>();
builder.Services.AddSingleton<GitHubWebhookSignatureVerifier>();

builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithToolsFromAssembly();

var app = builder.Build();

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "data"));
await app.Services.InitializeDatabaseAsync();

app.UseMiddleware<McpApiKeyMiddleware>();

app.MapGet("/healthz", () => Results.Ok(new
{
    status = "ok",
    service = "DevOrchestratorMcp"
}));

app.MapGet("/readyz", async (
    OrchestratorDbContext dbContext,
    CancellationToken cancellationToken) =>
{
    var ready = await dbContext.Database.CanConnectAsync(cancellationToken);
    return ready
        ? Results.Ok(new { status = "ready", database = dbContext.Database.ProviderName })
        : Results.Json(
            new { status = "not-ready" },
            statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapGitHubWebhook();
app.MapMcp("/mcp");

app.Run();
