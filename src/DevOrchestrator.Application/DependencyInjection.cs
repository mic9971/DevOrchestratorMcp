using DevOrchestrator.Application.Services;
using DevOrchestrator.Common.Time;
using Microsoft.Extensions.DependencyInjection;

namespace DevOrchestrator.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<ITaskQueryService, TaskQueryService>();
        services.AddScoped<IReviewService, ReviewService>();
        services.AddScoped<IGitHubBridgeService, GitHubBridgeService>();
        services.AddScoped<IGitHubWebhookProcessor, GitHubWebhookProcessor>();

        return services;
    }
}
