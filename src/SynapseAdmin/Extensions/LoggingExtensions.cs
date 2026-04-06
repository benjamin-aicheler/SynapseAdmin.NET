namespace SynapseAdmin.Extensions;

public static class LoggingExtensions
{
    private const int DefaultMaxLogLength = 1024;

    /// <summary>
    /// Sanitizes a string for safe logging by removing line breaks, control characters, and limiting length.
    /// This prevents Log Injection/Forging vulnerabilities (CodeQL cs/log-forging) and log-based DoS.
    /// </summary>
    /// <param name="input">The untrusted input string.</param>
    /// <param name="maxLength">Optional maximum length for the log entry. Defaults to 1024.</param>
    /// <returns>A sanitized and truncated string safe for logging.</returns>
    public static string? SanitizeForLogging(this string? input, int maxLength = DefaultMaxLogLength)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // Work on a local copy so we never return the original (tainted) reference.
        var sanitized = input;

        // Truncate first to prevent processing excessively large strings (DoS protection)
        if (sanitized.Length > maxLength)
        {
            sanitized = sanitized[..maxLength] + "...(truncated)";
        }

        // Normalize and replace all newline variants with spaces to preserve context
        sanitized = sanitized
            .Replace(Environment.NewLine, " ")
            .Replace("\r\n", " ")
            .Replace("\n\r", " ")
            .Replace("\n", " ")
            .Replace("\r", " ");

        // Allow only a conservative subset of printable characters in logs.
        // Replace any disallowed character with '?' so that the log structure remains intact.
        var result = new string(sanitized.Select(c =>
        {
            // Allow basic printable ASCII characters (0x20-0x7E).
            // This excludes control characters and potential terminal escape sequences.
            if (c >= 0x20 && c <= 0x7E)
            {
                return c;
            }

            // For anything else (including non-ASCII), replace with a visible placeholder.
            return '?';
        }).ToArray());

        return result.Trim();
    }
}
