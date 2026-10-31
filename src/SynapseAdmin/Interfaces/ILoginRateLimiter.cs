namespace SynapseAdmin.Interfaces;

public interface ILoginRateLimiter
{
    bool IsAllowed(string? clientIp, string? identifier = null);
    void RecordFailedAttempt(string? clientIp, string? identifier = null);
    void ResetAttempts(string? clientIp, string? identifier = null);
}
