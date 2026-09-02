using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Common.Results;

namespace DevOrchestrator.Application.Services;

public interface IReviewService
{
    Task<Result<TaskDto>> SubmitAsync(
        string projectKey,
        string taskCode,
        string decision,
        string summary,
        IReadOnlyList<string> findings,
        string actor,
        bool completeOnPass,
        CancellationToken cancellationToken);
}
