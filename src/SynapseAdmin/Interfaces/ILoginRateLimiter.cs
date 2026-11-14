namespace SynapseAdmin.Interfaces;

public interface ILoginRateLimiter
{
    bool TryAcquire(string? clientIp, string? identifier = null);
    void ResetAttempts(string? clientIp, string? identifier = null);
}
