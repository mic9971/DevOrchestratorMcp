using DevOrchestrator.Common.Results;

namespace DevOrchestrator.McpServer.Tools;

public sealed record ToolResponse<T>(
    bool Success,
    T? Data,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static ToolResponse<T> From(Result<T> result)
        => result.IsSuccess
            ? new ToolResponse<T>(true, result.Value, null, null)
            : new ToolResponse<T>(
                false,
                default,
                result.Error.Code,
                result.Error.Message);
}
