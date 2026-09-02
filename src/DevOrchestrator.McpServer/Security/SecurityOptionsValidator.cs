using Microsoft.Extensions.Options;

namespace DevOrchestrator.McpServer.Security;

public sealed class SecurityOptionsValidator : IValidateOptions<SecurityOptions>
{
    private const int MinimumKeyLength = 24;

    public ValidateOptionsResult Validate(string? name, SecurityOptions options)
    {
        if (!options.RequireAuthentication) return ValidateOptionsResult.Success;

        var currentKeys = new[]
        {
            (Name: "Architect", Key: options.ArchitectKey),
            (Name: "Implementer", Key: options.ImplementerKey),
            (Name: "Auditor", Key: options.AuditorKey)
        };

        var missing = currentKeys.Where(x => string.IsNullOrWhiteSpace(x.Key)).Select(x => x.Name).ToArray();
        if (missing.Length > 0)
        {
            return ValidateOptionsResult.Fail($"MCP authentication requires keys for: {string.Join(", ", missing)}.");
        }

        var allKeys = new[]
        {
            (Name: "Architect", Key: options.ArchitectKey),
            (Name: "ArchitectPrevious", Key: options.ArchitectPreviousKey),
            (Name: "Implementer", Key: options.ImplementerKey),
            (Name: "ImplementerPrevious", Key: options.ImplementerPreviousKey),
            (Name: "Auditor", Key: options.AuditorKey),
            (Name: "AuditorPrevious", Key: options.AuditorPreviousKey)
        }.Where(x => !string.IsNullOrWhiteSpace(x.Key)).ToArray();

        var shortKeys = allKeys.Where(x => x.Key!.Length < MinimumKeyLength).Select(x => x.Name).ToArray();
        if (shortKeys.Length > 0)
        {
            return ValidateOptionsResult.Fail(
                $"MCP role keys must be at least {MinimumKeyLength} characters: {string.Join(", ", shortKeys)}.");
        }

        var distinct = allKeys.Select(x => x.Key!).Distinct(StringComparer.Ordinal).Count();
        return distinct == allKeys.Length
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Current and previous MCP role keys must all be distinct.");
    }
}
