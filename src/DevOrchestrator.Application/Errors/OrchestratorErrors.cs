using DevOrchestrator.Common.Results;

namespace DevOrchestrator.Application.Errors;

public static class OrchestratorErrors
{
    public static Error ProjectNotFound(string key)
        => new("project.not_found", $"Project '{key}' was not found.");

    public static Error ProjectAlreadyExists(string key)
        => new("project.already_exists", $"Project '{key}' is already registered.");

    public static Error TaskNotFound(string code)
        => new("task.not_found", $"Task '{code}' was not found.");

    public static Error TaskAlreadyExists(string code)
        => new("task.already_exists", $"Task '{code}' already exists.");

    public static Error TaskAlreadyExists()
        => new("task.already_exists", "One or more task codes were created concurrently and already exist.");

    public static Error DependencyNotFound(string code)
        => new("task.dependency_not_found", $"Dependency task '{code}' was not found.");

    public static Error InvalidState(string message)
        => new("task.invalid_state", message);

    public static Error ConcurrencyConflict(string message)
        => new("task.concurrency_conflict", message);

    public static Error InvalidInput(string message)
        => new("input.invalid", message);
}
