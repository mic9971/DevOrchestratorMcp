namespace DevOrchestrator.Application.Abstractions;

public sealed class DuplicateKeyException : Exception
{
    public DuplicateKeyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
