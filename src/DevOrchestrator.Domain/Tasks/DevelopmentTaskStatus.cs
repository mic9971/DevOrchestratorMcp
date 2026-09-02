namespace DevOrchestrator.Domain.Tasks;

public enum DevelopmentTaskStatus
{
    Draft = 0,
    Ready = 1,
    InProgress = 2,
    ReadyForReview = 3,
    ChangesRequested = 4,
    Done = 5,
    Blocked = 6,
    Cancelled = 7
}
