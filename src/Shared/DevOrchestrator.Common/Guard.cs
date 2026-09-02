namespace DevOrchestrator.Common;

public static class Guard
{
    public static string NotBlank(string? value, string paramName, int maxLength = 500)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{paramName} is required.", paramName);
        }

        var normalized = value.Trim();

        if (normalized.Length > maxLength)
        {
            throw new ArgumentException($"{paramName} cannot exceed {maxLength} characters.", paramName);
        }

        return normalized;
    }
}
