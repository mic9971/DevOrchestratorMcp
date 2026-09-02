namespace DevOrchestrator.Domain.Tasks;

public sealed class TaskDependency
{
    private TaskDependency()
    {
    }

    internal TaskDependency(Guid taskId, Guid dependsOnTaskId)
    {
        Id = Guid.NewGuid();
        TaskId = taskId;
        DependsOnTaskId = dependsOnTaskId;
    }

    public Guid Id { get; private set; }

    public Guid TaskId { get; private set; }

    public Guid DependsOnTaskId { get; private set; }
}
