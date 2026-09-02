using System.Threading.RateLimiting;
using DevOrchestrator.Application;
using DevOrchestrator.Infrastructure;
using DevOrchestrator.Infrastructure.Persistence;
using DevOrchestrator.McpServer.ControlPlane;
using DevOrchestrator.McpServer.Operations;
using DevOrchestrator.McpServer.Security;
using DevOrchestrator.McpServer.Webhooks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var migrateOnly = args.Any(x => string.Equals(x, "migrate", StringComparison.OrdinalIgnoreCase));
var hostArgs = args.Where(x => !string.Equals(x, "migrate", StringComparison.OrdinalIgnoreCase)).ToArray();
var builder = WebApplication.CreateBuilder(hostArgs);

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment.ContentRootPath);

if (migrateOnly)
{
    var migrationHost = builder.Build();
    Directory.CreateDirectory(Path.Combine(migrationHost.Environment.ContentRootPath, "data"));
    await migrationHost.Services.MigrateDatabaseAsync();
    return;
}

builder.Services.AddApplication();
builder.Services.AddHttpContextAccessor();
builder.Services.AddOptions<SecurityOptions>().Bind(builder.Configuration.GetSection("Security")).ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<SecurityOptions>, SecurityOptionsValidator>();
builder.Services.Configure<GitHubWebhookOptions>(builder.Configuration.GetSection("GitHub"));
builder.Services.AddScoped<ToolAuthorizer>();
builder.Services.AddSingleton<GitHubWebhookSignatureVerifier>();
builder.Services.AddHostedService<GitHubWebhookBackgroundService>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("mcp", limiter =>
    {
        limiter.PermitLimit = 120;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
    options.AddFixedWindowLimiter("webhook", limiter =>
    {
        limiter.PermitLimit = 300;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
        limiter.AutoReplenishment = true;
    });
});

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("DevOrchestratorMcp"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation(options =>
            {
                options.Filter = context => !context.Request.Path.StartsWithSegments("/healthz");
            })
            .AddHttpClientInstrumentation();

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            tracing.AddOtlpExporter();
    });

builder.Services.AddMcpServer().WithHttpTransport(options => options.Stateless = true).WithToolsFromAssembly();

var app = builder.Build();
Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "data"));
app.UseRateLimiter();
app.UseMiddleware<McpApiKeyMiddleware>();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/control"))
    {
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
    }

    await next();
});
app.UseStaticFiles();

app.MapGet("/", () => Results.Redirect("/control"));
app.MapGet("/healthz", () => Results.Ok(new { status = "ok", service = "DevOrchestratorMcp" }));
app.MapGet("/readyz", async (OrchestratorDbContext dbContext, CancellationToken cancellationToken) =>
{
    var readiness = await dbContext.GetDatabaseReadinessAsync(cancellationToken);
    return readiness.Ready
        ? Results.Ok(new { status = "ready", database = dbContext.Database.ProviderName })
        : Results.Json(new
        {
            status = "not-ready",
            reason = readiness.Reason,
            pendingMigrations = readiness.PendingMigrations
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapControlPlaneEndpoints();
app.MapControlPlaneTaskDetailEndpoints();
app.MapOperationsEndpoints();
app.MapGitHubWebhook().RequireRateLimiting("webhook");
app.MapMcp("/mcp").RequireRateLimiting("mcp");

app.Run();

public partial class Program;
