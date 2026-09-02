namespace DevOrchestrator.Common.Time;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
