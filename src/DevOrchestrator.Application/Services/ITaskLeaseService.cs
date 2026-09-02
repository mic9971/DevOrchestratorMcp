using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Common.Results;

namespace DevOrchestrator.Application.Services;

public interface ITaskLeaseService
{
    Task<Result<TaskDto?>> ClaimNextAsync(
        string projectKey,
        string workerId,
        string actor,
        string? branch,
        CancellationToken cancellationToken);

    Task<Result<TaskDto>> HeartbeatAsync(
        string projectKey,
        string taskCode,
        string workerId,
        string actor,
        CancellationToken cancellationToken);
}
