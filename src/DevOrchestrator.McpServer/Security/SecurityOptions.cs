namespace DevOrchestrator.McpServer.Security;

public sealed class SecurityOptions
{
    public bool RequireAuthentication { get; init; }
    public string? ArchitectKey { get; init; }
    public string? ArchitectPreviousKey { get; init; }
    public string? ImplementerKey { get; init; }
    public string? ImplementerPreviousKey { get; init; }
    public string? AuditorKey { get; init; }
    public string? AuditorPreviousKey { get; init; }
}
