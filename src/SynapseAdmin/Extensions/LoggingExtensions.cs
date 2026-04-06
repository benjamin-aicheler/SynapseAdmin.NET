namespace SynapseAdmin.Extensions;

public static class LoggingExtensions
{
    /// <summary>
    /// Sanitizes a string for safe logging by removing line breaks, control characters and trimming.
    /// This prevents Log Injection/Forging vulnerabilities (CodeQL cs/log-forging).
    /// </summary>
    public static string? SanitizeForLogging(this string? input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // Work on a copy and remove all newline variants and control characters.
        var sanitized = input
            .Replace("\r\n", string.Empty)
            .Replace("\n\r", string.Empty)
            .Replace("\n", string.Empty)
            .Replace("\r", string.Empty);

        // Strip remaining control characters (except tab) to avoid hidden log manipulation.
        sanitized = new string(sanitized.Where(c => !char.IsControl(c) || c == '\t').ToArray());

        return sanitized.Trim();
    }
}
