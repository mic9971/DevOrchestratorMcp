using DevOrchestrator.Application.Contracts;
using DevOrchestrator.Common.Results;

namespace DevOrchestrator.Application.Services;

public interface ITaskService
{
    Task<Result<TaskDto>> CreateAsync(string projectKey, CreateTaskSeed seed, string actor, CancellationToken cancellationToken);
    Task<Result<BatchCreateResult>> CreateBatchAsync(string projectKey, IReadOnlyList<CreateTaskSeed> seeds, string actor, CancellationToken cancellationToken);
    Task<Result<TaskDto>> GetAsync(string projectKey, string code, CancellationToken cancellationToken);
    Task<Result<IReadOnlyList<TaskDto>>> ListAsync(string projectKey, string? status, CancellationToken cancellationToken);
    Task<Result<TaskDto?>> GetNextAsync(string projectKey, CancellationToken cancellationToken);
    Task<Result<TaskDto?>> ClaimNextAsync(string projectKey, string workerId, string actor, string? branch, CancellationToken cancellationToken);
    Task<Result<TaskDto>> HeartbeatAsync(string projectKey, string code, string workerId, string actor, CancellationToken cancellationToken);
    Task<Result<TaskDto>> StartAsync(string projectKey, string code, string actor, string? branch, CancellationToken cancellationToken);
    Task<Result<TaskDto>> AddEvidenceAsync(string projectKey, string code, EvidenceInput evidence, string actor, CancellationToken cancellationToken);
    Task<Result<TaskDto>> SubmitForReviewAsync(string projectKey, string code, string actor, CancellationToken cancellationToken);
    Task<Result<TaskDto>> BlockAsync(string projectKey, string code, string reason, string actor, CancellationToken cancellationToken);
    Task<Result<TaskDto>> ResumeAsync(string projectKey, string code, string actor, CancellationToken cancellationToken);
    Task<Result<TaskDto>> ReopenAsync(string projectKey, string code, string reason, string actor, CancellationToken cancellationToken);
}
