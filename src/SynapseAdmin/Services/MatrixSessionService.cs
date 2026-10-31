using System.Net;
using LibMatrix;
using Microsoft.AspNetCore.Http;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Resources;
using Microsoft.Extensions.Localization;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces.Gateways;

namespace SynapseAdmin.Services;

public class MatrixSessionService(
    IMatrixAuthGateway authGateway,
    ILoginRateLimiter loginRateLimiter,
    IHttpContextAccessor httpContextAccessor,
    ILogger<MatrixSessionService> logger,
    IStringLocalizer<SharedResources> L) : IMatrixSessionService
{
    public IMatrixGateway? Gateway { get; private set; }

    public bool IsLoggedIn => Gateway != null;

    private string? GetClientIp() =>
        httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString();

    private static bool IsUpstreamRateLimit(Exception ex) =>
        ex is LibMatrixException { ErrorCode: "M_LIMIT_EXCEEDED" } ||
        ex is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests };

    public async Task<OperationResult> LoginAsync(string homeserver, string username, string password)
    {
        var clientIp = GetClientIp();
        var rateLimitId = $"{homeserver?.Trim().ToLowerInvariant()}|{username?.Trim().ToLowerInvariant()}";

        if (!loginRateLimiter.IsAllowed(clientIp, rateLimitId))
        {
            logger.LogWarning(
                "Login rate limit exceeded for user {Username} on {Homeserver} from IP {ClientIp}",
                username.SanitizeForLogging(),
                homeserver.SanitizeForLogging(),
                clientIp.SanitizeForLogging());
            return OperationResult.Failure(L["TooManyLoginAttempts"]);
        }

        try
        {
            // Resolve homeserver via gateway
            await authGateway.ResolveHomeserverAsync(homeserver!);

            var loginResponse = await authGateway.LoginAsync(homeserver!, username!, password);
            Gateway = await authGateway.GetAuthenticatedAsync(homeserver!, loginResponse.AccessToken);
            loginRateLimiter.ResetAttempts(clientIp, rateLimitId);
            
            logger.LogInformation("User {Username} successfully logged into {Homeserver}", username.SanitizeForLogging(), homeserver.SanitizeForLogging());
            return OperationResult.Ok(L["LoginSuccessful"]);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Cancelled();
        }
        catch (Exception ex)
        {
            loginRateLimiter.RecordFailedAttempt(clientIp, rateLimitId);
            logger.LogError(ex, "Login failed for user {Username} on {Homeserver}", username.SanitizeForLogging(), homeserver.SanitizeForLogging());

            if (IsUpstreamRateLimit(ex))
            {
                return OperationResult.Failure(L["TooManyLoginAttempts"]);
            }

            return OperationResult.Failure(L["LoginFailed"]);
        }
    }

    public async Task<OperationResult> LoginWithTokenAsync(string homeserver, string accessToken)
    {
        var clientIp = GetClientIp();
        var rateLimitId = homeserver?.Trim().ToLowerInvariant();

        if (!loginRateLimiter.IsAllowed(clientIp, rateLimitId))
        {
            logger.LogWarning(
                "Token login rate limit exceeded on {Homeserver} from IP {ClientIp}",
                homeserver.SanitizeForLogging(),
                clientIp.SanitizeForLogging());
            return OperationResult.Failure(L["TooManyLoginAttempts"]);
        }

        try
        {
            // Resolve homeserver via gateway
            await authGateway.ResolveHomeserverAsync(homeserver!);

            Gateway = await authGateway.GetAuthenticatedAsync(homeserver!, accessToken);
            loginRateLimiter.ResetAttempts(clientIp, rateLimitId);
            
            logger.LogInformation("Session successfully logged in via token for user {UserId} on {Homeserver}", Gateway.UserId.SanitizeForLogging(), homeserver.SanitizeForLogging());
            return OperationResult.Ok(L["LoginSuccessful"]);
        }
        catch (OperationCanceledException)
        {
            Gateway = null;
            return OperationResult.Cancelled();
        }
        catch (Exception ex)
        {
            loginRateLimiter.RecordFailedAttempt(clientIp, rateLimitId);
            logger.LogError(ex, "Login with token failed for {Homeserver}", homeserver.SanitizeForLogging());
            Gateway = null;

            if (IsUpstreamRateLimit(ex))
            {
                return OperationResult.Failure(L["TooManyLoginAttempts"]);
            }

            return OperationResult.Failure(L["LoginFailed"]);
        }
    }

    public async Task<OperationResult> RestoreSessionAsync(string homeserver, string accessToken, bool force = false)
    {
        if (!force && Gateway != null && 
            Gateway.HomeserverUrl == homeserver && 
            Gateway.AccessToken == accessToken)
        {
            return OperationResult.Ok();
        }

        try
        {
            // Resolve homeserver via gateway
            await authGateway.ResolveHomeserverAsync(homeserver);

            Gateway = await authGateway.GetAuthenticatedAsync(homeserver, accessToken);
            
            logger.LogDebug("Session successfully restored for user {UserId} on {Homeserver}", Gateway.UserId.SanitizeForLogging(), homeserver.SanitizeForLogging());
            return OperationResult.Ok();
        }
        catch (OperationCanceledException)
        {
            Gateway = null;
            return OperationResult.Cancelled();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to restore session for {Homeserver}", homeserver.SanitizeForLogging());
            Gateway = null;
            return OperationResult.Failure(L["LoginFailed"]);
        }
    }

    public async Task<string?> GetServerVersionAsync(CancellationToken cancellationToken = default)
    {
        if (Gateway == null) return null;

        if (Gateway.SupportsAdminApi)
        {
            try
            {
                var synapseVer = await Gateway.GetSynapseVersionAsync(cancellationToken);
                if (!string.IsNullOrEmpty(synapseVer?.ServerVersion))
                {
                    return synapseVer.ServerVersion;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to retrieve Synapse server version via admin API");
            }
        }

        return Gateway.ServerVersion != "Unknown" ? Gateway.ServerVersion : null;
    }

    public void Logout()
    {
        if (Gateway != null)
        {
            logger.LogInformation("User {UserId} logged out from {Homeserver}", Gateway.UserId.SanitizeForLogging(), Gateway.ServerName.SanitizeForLogging());
        }
        Gateway = null;
    }
}
