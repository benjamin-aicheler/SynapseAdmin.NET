using System.Net;
using LibMatrix;
using Microsoft.Extensions.Caching.Memory;
using SynapseAdmin.Infrastructure.Gateways;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// <see cref="MatrixAuthGateway.GetAuthenticatedAsync"/> reports the homeserver's rate limit as
/// <see cref="HomeserverRateLimitedException"/>, whichever way the SDK or the HTTP stack reports it, so that callers
/// don't depend on <c>LibMatrix</c>. Every other failure passes through unchanged.
/// </summary>
public sealed class MatrixAuthGatewayRateLimitTests : IDisposable
{
    private const string Homeserver = "https://matrix.example.com";
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public void Dispose() => _cache.Dispose();

    public static TheoryData<string> RateLimitResponses => ["http-429", "matrix-limit-exceeded"];

    public static TheoryData<string> OtherFailures => ["http-401", "matrix-unknown-token", "invalid-operation"];

    [Theory]
    [MemberData(nameof(RateLimitResponses))]
    public async Task GetAuthenticated_RateLimitResponse_ThrowsRateLimitedException(string kind)
    {
        var cause = CreateFailure(kind);
        var gateway = new FailingAuthGateway(_cache, cause);

        var ex = await Assert.ThrowsAsync<HomeserverRateLimitedException>(() => gateway.GetAuthenticatedAsync(Homeserver, "syt_token"));

        Assert.Same(cause, ex.InnerException);
    }

    [Theory]
    [MemberData(nameof(OtherFailures))]
    public async Task GetAuthenticated_OtherFailure_PassesThroughUnchanged(string kind)
    {
        var cause = CreateFailure(kind);
        var gateway = new FailingAuthGateway(_cache, cause);

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => gateway.GetAuthenticatedAsync(Homeserver, "syt_token"));

        Assert.Same(cause, ex);
    }

    [Fact]
    public async Task GetAuthenticated_RateLimitResponse_IsNotCached()
    {
        var gateway = new FailingAuthGateway(_cache, CreateFailure("http-429"));

        await Assert.ThrowsAsync<HomeserverRateLimitedException>(() => gateway.GetAuthenticatedAsync(Homeserver, "syt_token"));
        await Assert.ThrowsAsync<HomeserverRateLimitedException>(() => gateway.GetAuthenticatedAsync(Homeserver, "syt_token"));

        Assert.Equal(2, gateway.CreationCount);
    }

    private static Exception CreateFailure(string kind) => kind switch
    {
        "http-429" => new HttpRequestException("Too Many Requests", null, HttpStatusCode.TooManyRequests),
        "matrix-limit-exceeded" => new MatrixException { ErrorCode = MatrixException.ErrorCodes.M_LIMIT_EXCEEDED, Error = "Too many requests" },
        "http-401" => new HttpRequestException("Unauthorized", null, HttpStatusCode.Unauthorized),
        "matrix-unknown-token" => new MatrixException { ErrorCode = "M_UNKNOWN_TOKEN", Error = "Unknown token" },
        "invalid-operation" => new InvalidOperationException("Could not resolve a client URL for the homeserver."),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    /// <summary>Replaces the gateway creation (discovery, SDK initialisation and probes) with a failure.</summary>
    private sealed class FailingAuthGateway(IMemoryCache cache, Exception failure) : MatrixAuthGateway(null!, cache)
    {
        private int _creationCount;

        public int CreationCount => Volatile.Read(ref _creationCount);

        protected override Task<IMatrixGateway> CreateAuthenticatedGatewayAsync(string homeserverUrl, string accessToken, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _creationCount);
            return Task.FromException<IMatrixGateway>(failure);
        }
    }
}
