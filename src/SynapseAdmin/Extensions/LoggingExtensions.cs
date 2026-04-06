namespace SynapseAdmin.Extensions;

public static class LoggingExtensions
{
    /// <summary>
    /// Sanitizes a string for safe logging by removing line breaks and trimming.
    /// This prevents Log Injection/Forging vulnerabilities (CodeQL cs/log-forging).
    /// </summary>
    public static string? SanitizeForLogging(this string? input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        return input
            .Replace(Environment.NewLine, string.Empty)
            .Replace("\n", string.Empty)
            .Replace("\r", string.Empty)
            .Trim();
    }
}
