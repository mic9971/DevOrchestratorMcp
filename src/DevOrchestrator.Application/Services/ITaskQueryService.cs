using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Common.Results;

namespace DevOrchestrator.Application.Services;

public interface ITaskQueryService
{
    Task<Result<TaskPageDto>> ListPageAsync(
        string projectKey,
        string? status,
        int offset,
        int limit,
        CancellationToken cancellationToken);
}
