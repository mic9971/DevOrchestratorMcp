using DevOrchestrator.Application;
using DevOrchestrator.Infrastructure;
using DevOrchestrator.Infrastructure.Persistence;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithToolsFromAssembly();

var app = builder.Build();

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "data"));
await app.Services.InitializeDatabaseAsync();

app.MapGet("/healthz", () => Results.Ok(new
{
    status = "ok",
    service = "DevOrchestratorMcp"
}));

app.MapMcp("/mcp");

app.Run();
