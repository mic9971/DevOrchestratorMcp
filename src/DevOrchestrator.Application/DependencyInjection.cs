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
        services.AddScoped<IReviewService, ReviewService>();

        return services;
    }
}
