namespace DevOrchestrator.Application.Abstractions;

public sealed class ConcurrencyConflictException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
