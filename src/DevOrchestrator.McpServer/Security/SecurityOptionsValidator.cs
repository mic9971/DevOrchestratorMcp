using Microsoft.Extensions.Options;

namespace DevOrchestrator.McpServer.Security;

public sealed class SecurityOptionsValidator : IValidateOptions<SecurityOptions>
{
    private const int MinimumKeyLength = 24;

    public ValidateOptionsResult Validate(string? name, SecurityOptions options)
    {
        if (!options.RequireAuthentication)
        {
            return ValidateOptionsResult.Success;
        }

        var keys = new[]
        {
            (Role: "Architect", Key: options.ArchitectKey),
            (Role: "Implementer", Key: options.ImplementerKey),
            (Role: "Auditor", Key: options.AuditorKey)
        };

        var missing = keys
            .Where(x => string.IsNullOrWhiteSpace(x.Key))
            .Select(x => x.Role)
            .ToArray();

        if (missing.Length > 0)
        {
            return ValidateOptionsResult.Fail(
                $"MCP authentication requires keys for: {string.Join(", ", missing)}.");
        }

        var shortKeys = keys
            .Where(x => x.Key!.Length < MinimumKeyLength)
            .Select(x => x.Role)
            .ToArray();

        if (shortKeys.Length > 0)
        {
            return ValidateOptionsResult.Fail(
                $"MCP role keys must be at least {MinimumKeyLength} characters: {string.Join(", ", shortKeys)}.");
        }

        var distinct = keys
            .Select(x => x.Key!)
            .Distinct(StringComparer.Ordinal)
            .Count();

        return distinct == keys.Length
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Architect, Implementer, and Auditor keys must be distinct.");
    }
}
