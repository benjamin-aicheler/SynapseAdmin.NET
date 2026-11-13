using System.Text.Json;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using SynapseAdmin.Services;
using Xunit;

namespace SynapseAdmin.Tests;

public class UserExperimentalFeaturesTests
{
    [Fact]
    public void ExperimentalFeaturesDtos_SerializeAndDeserializeCorrectly()
    {
        const string json = """
        {
            "features": {
                "msc3026": true,
                "msc3881": false,
                "msc3967": true
            }
        }
        """;

        var response = JsonSerializer.Deserialize<SynapseAdminExperimentalFeaturesResponse>(json);
        Assert.NotNull(response);
        Assert.Equal(3, response.Features.Count);
        Assert.True(response.Features["msc3026"]);
        Assert.False(response.Features["msc3881"]);
        Assert.True(response.Features["msc3967"]);

        var request = new SynapseAdminExperimentalFeaturesRequest
        {
            Features = new Dictionary<string, bool>
            {
                ["msc3881"] = true
            }
        };

        var serialized = JsonSerializer.Serialize(request);
        Assert.Contains("\"features\":{\"msc3881\":true}", serialized);
    }

    [Fact]
    public async Task GetUserExperimentalFeaturesAsync_OrdersKeysAndResolvesDescriptions()
    {
        var gateway = new FakeExperimentalFeaturesGateway
        {
            ExperimentalFeaturesResult = new SynapseAdminExperimentalFeaturesResponse
            {
                Features = new Dictionary<string, bool>
                {
                    ["msc9999"] = false,
                    ["msc3026"] = true,
                    ["msc3881"] = false
                }
            }
        };

        var service = CreateUserService(gateway);

        var result = await service.GetUserExperimentalFeaturesAsync("@alice:example.com");

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(3, result.Data.Count);

        Assert.Equal("msc3026", result.Data[0].FeatureKey);
        Assert.True(result.Data[0].Enabled);
        Assert.Equal("Busy presence state (MSC3026)", result.Data[0].Description);

        Assert.Equal("msc3881", result.Data[1].FeatureKey);
        Assert.False(result.Data[1].Enabled);
        Assert.Equal("Remotely toggle push notifications for another client (MSC3881)", result.Data[1].Description);

        Assert.Equal("msc9999", result.Data[2].FeatureKey);
        Assert.False(result.Data[2].Enabled);
        Assert.Equal("Experimental Matrix Specification Change (MSC) feature", result.Data[2].Description);
    }

    [Fact]
    public async Task SetUserExperimentalFeaturesAsync_SendsRequestToGateway()
    {
        var gateway = new FakeExperimentalFeaturesGateway();
        var service = CreateUserService(gateway);

        var updates = new Dictionary<string, bool> { ["msc3881"] = true };
        var result = await service.SetUserExperimentalFeaturesAsync("@alice:example.com", updates);

        Assert.True(result.Success);
        Assert.Equal("@alice:example.com", gateway.LastUpdatedUserId);
        Assert.NotNull(gateway.LastUpdatedFeaturesRequest);
        Assert.True(gateway.LastUpdatedFeaturesRequest.Features["msc3881"]);
    }

    [Fact]
    public async Task GetUserDetailsAsync_ContinuesGracefully_WhenExperimentalFeaturesEndpointThrows()
    {
        var gateway = new FakeExperimentalFeaturesGateway
        {
            ThrowOnGetExperimentalFeatures = true
        };
        var service = CreateUserService(gateway);

        var result = await service.GetUserDetailsAsync("@alice:example.com");

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal("@alice:example.com", result.Data.UserId);
        Assert.Null(result.Data.ExperimentalFeatures);
    }

    [Fact]
    public void AccountValidityDtos_SerializeAndOmitNullFieldsCorrectly()
    {
        var defaultPeriodReq = new SynapseAdminAccountValidityRequest
        {
            UserId = "@alice:example.com",
            ExpirationTs = null,
            EnableRenewalEmails = true
        };

        var defaultJson = JsonSerializer.Serialize(defaultPeriodReq);
        Assert.Contains("\"user_id\":\"@alice:example.com\"", defaultJson);
        Assert.Contains("\"enable_renewal_emails\":true", defaultJson);
        Assert.DoesNotContain("expiration_ts", defaultJson);

        var customTsReq = new SynapseAdminAccountValidityRequest
        {
            UserId = "@alice:example.com",
            ExpirationTs = 1785542400000L,
            EnableRenewalEmails = false
        };

        var customJson = JsonSerializer.Serialize(customTsReq);
        Assert.Contains("\"expiration_ts\":1785542400000", customJson);
        Assert.Contains("\"enable_renewal_emails\":false", customJson);

        var resp = JsonSerializer.Deserialize<SynapseAdminAccountValidityResponse>("{\"expiration_ts\":1785542400000}");
        Assert.NotNull(resp);
        Assert.Equal(1785542400000L, resp.ExpirationTs);
    }

    [Fact]
    public async Task RenewAccountValidityAsync_SendsRequestAndReturnsUpdatedExpirationTimestamp()
    {
        var gateway = new FakeExperimentalFeaturesGateway
        {
            AccountValidityResponse = new SynapseAdminAccountValidityResponse
            {
                ExpirationTs = 1785542400000L
            }
        };
        var service = CreateUserService(gateway);

        var model = new AccountValidityRenewViewModel
        {
            UserId = "@alice:example.com",
            ExpirationTs = 1785542400000L,
            EnableRenewalEmails = true
        };

        var result = await service.RenewAccountValidityAsync(model);

        Assert.True(result.Success);
        Assert.Equal(1785542400000L, result.Data);
        Assert.NotNull(gateway.LastAccountValidityRequest);
        Assert.Equal("@alice:example.com", gateway.LastAccountValidityRequest.UserId);
        Assert.Equal(1785542400000L, gateway.LastAccountValidityRequest.ExpirationTs);
        Assert.True(gateway.LastAccountValidityRequest.EnableRenewalEmails);
    }

    private static UserService CreateUserService(IMatrixGateway gateway)
    {
        var session = new FakeMatrixSessionService(gateway);
        var localizer = new FakeStringLocalizer();
        return new UserService(session, NullLogger<UserService>.Instance, localizer);
    }

    private sealed class FakeMatrixSessionService(IMatrixGateway gateway) : IMatrixSessionService
    {
        public IMatrixGateway? Gateway { get; } = gateway;
        public bool IsLoggedIn => true;
        public Task<OperationResult> LoginAsync(string homeserver, string username, string password) => throw new NotImplementedException();
        public Task<OperationResult> LoginWithTokenAsync(string homeserver, string accessToken) => throw new NotImplementedException();
        public Task<OperationResult> RestoreSessionAsync(string homeserver, string accessToken, bool force = false) => throw new NotImplementedException();
        public Task<string?> GetServerVersionAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>("1.120.0");
        public void Logout() { }
    }

    private sealed class FakeStringLocalizer : IStringLocalizer<SharedResources>
    {
        private static readonly Dictionary<string, string> Translations = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ExperimentalFeature_msc3026"] = "Busy presence state (MSC3026)",
            ["ExperimentalFeature_msc3881"] = "Remotely toggle push notifications for another client (MSC3881)",
            ["ExperimentalFeature_Unknown"] = "Experimental Matrix Specification Change (MSC) feature",
            ["ExperimentalFeaturesUpdatedSuccessfully"] = "Experimental features updated successfully.",
            ["AccountValidityRenewedSuccessfully"] = "Account validity renewed successfully."
        };

        public LocalizedString this[string name] =>
            Translations.TryGetValue(name, out var value)
                ? new LocalizedString(name, value, resourceNotFound: false)
                : new LocalizedString(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(this[name].Value, arguments), this[name].ResourceNotFound);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private sealed class FakeExperimentalFeaturesGateway : IMatrixGateway
    {
        public SynapseAdminExperimentalFeaturesResponse? ExperimentalFeaturesResult { get; set; }
        public bool ThrowOnGetExperimentalFeatures { get; set; }
        public string? LastUpdatedUserId { get; private set; }
        public SynapseAdminExperimentalFeaturesRequest? LastUpdatedFeaturesRequest { get; private set; }
        public SynapseAdminAccountValidityResponse? AccountValidityResponse { get; set; }
        public SynapseAdminAccountValidityRequest? LastAccountValidityRequest { get; private set; }

        public string UserId => "@admin:example.com";
        public string Username => "admin";
        public string HomeserverUrl => "https://example.com";
        public string ServerName => "example.com";
        public string AccessToken => "token";
        public bool SupportsAdminApi => true;
        public string ServerBrand => "Synapse";
        public string ServerVersion => "1.120.0";

        public Task<SynapseAdminExperimentalFeaturesResponse?> GetUserExperimentalFeaturesAsync(string userId, CancellationToken cancellationToken = default)
        {
            if (ThrowOnGetExperimentalFeatures)
                throw new HttpRequestException("Endpoint not supported");
            return Task.FromResult(ExperimentalFeaturesResult);
        }

        public Task SetUserExperimentalFeaturesAsync(string userId, SynapseAdminExperimentalFeaturesRequest request, CancellationToken cancellationToken = default)
        {
            LastUpdatedUserId = userId;
            LastUpdatedFeaturesRequest = request;
            return Task.CompletedTask;
        }

        public Task<SynapseAdminAccountValidityResponse?> RenewAccountValidityAsync(SynapseAdminAccountValidityRequest request, CancellationToken cancellationToken = default)
        {
            LastAccountValidityRequest = request;
            return Task.FromResult(AccountValidityResponse);
        }

        public Task<SynapseAdminUserListResult.SynapseAdminUserListResultUser?> GetUserDetailsAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SynapseAdminUserListResult.SynapseAdminUserListResultUser?>(new()
            {
                Name = userId,
                DisplayName = "Alice",
                CreationTs = 1700000000L
            });

        public Task<SynapseAdminUserMediaResult?> GetUserMediaAsync(string userId, int from, int limit, string orderBy = "created_ts", string dir = "b", CancellationToken cancellationToken = default) =>
            Task.FromResult<SynapseAdminUserMediaResult?>(new() { Total = 0, Media = [] });

        public Task<SynapseAdminUserMembershipsResponse?> GetUserMembershipsAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SynapseAdminUserMembershipsResponse?>(new() { Memberships = [] });

        public Task<SynapseAdminUserListResult?> GetUserListAsync(int offset, int limit, string orderBy, string direction, string? searchTerm = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeactivateUserAsync(string userId, bool erase, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task QuarantineMediaByUserIdAsync(string userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<LoginResponse> LoginAsUserAsync(string userId, TimeSpan expireIn, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SendServerNoticeResponse?> SendServerNoticeAsync(string userId, object content, string? type = null, string? stateKey = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<UserMediaStatisticsResponse?> GetUserMediaStatisticsAsync(int limit = 10, string orderBy = "media_length", string dir = "b", string? searchTerm = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminUserListResult.SynapseAdminUserListResultUser?> UpdateUserAsync(string userId, object request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminRoomListResult?> GetRoomListAsync(int offset, int limit, string orderBy, string direction, string? searchTerm = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom?> GetRoomDetailsAsync(string roomId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminRoomMemberListResult?> GetRoomMembersAsync(string roomId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminRoomStateResult?> GetRoomStateAsync(string roomId, string? type = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminRoomMediaListResult?> GetRoomMediaListAsync(string roomId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteRoomAsync(string roomId, SynapseAdminRoomDeleteRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task QuarantineMediaByRoomIdAsync(string roomId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task BlockRoomAsync(string roomId, bool block, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<RoomStatisticsResponse?> GetLargestRoomsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminRoomMessagesResponse?> GetRoomMessagesAsync(string roomId, int? limit = null, string? from = null, string? dir = null, string? filter = null, string? to = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminPurgeHistoryResponse?> PurgeRoomHistoryAsync(string roomId, SynapseAdminPurgeHistoryRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminPurgeHistoryStatusResponse?> GetPurgeHistoryStatusAsync(string purgeId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<MatrixPublicRoomDirectoryResult?> GetPublicRoomsAsync(int limit, string? since = null, string? searchTerm = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<string?> GetRoomDirectoryVisibilityAsync(string roomId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task SetRoomDirectoryVisibilityAsync(string roomId, string visibility, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task PutRoomAliasAsync(string roomAlias, string roomId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteRoomAliasAsync(string roomAlias, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<string>?> GetLocalRoomAliasesAsync(string roomId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminDestinationListResult?> GetFederationDestinationListAsync(int offset, int limit, string direction, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ResetFederationConnectionTimeoutAsync(string destination, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminEventReportListResult?> GetEventReportListAsync(int offset, int limit, string direction, string? searchTerm = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteEventReportAsync(string reportId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminFetchEventResponse?> FetchEventAsync(string eventId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken>> GetRegistrationTokensAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken?> CreateRegistrationTokenAsync(SynapseAdminRegistrationTokenCreateRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateRegistrationTokenAsync(string token, SynapseAdminRegistrationTokenUpdateRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteRegistrationTokenAsync(string token, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseVersionResponse?> GetSynapseVersionAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<byte[]?> DownloadMediaAsync(string mxcUrl, long maxBytes = 5242880, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Stream> GetMediaStreamAsync(string mxcUri, string? filename = null, int? timeout = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminMediaMetadataResponse.MediaInfo?> GetMediaMetadataAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminMediaMetadataResponse.MediaInfo?> GetMediaMetadataAsync(string mxcUri, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task QuarantineMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task QuarantineMediaAsync(string mxcUri, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UnquarantineMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UnquarantineMediaAsync(string mxcUri, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteMediaAsync(string mxcUri, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ProtectMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task ProtectMediaAsync(string mxcUri, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UnprotectMediaAsync(string serverName, string mediaId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UnprotectMediaAsync(string mxcUri, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminPurgeMediaCacheResponse?> PurgeRemoteMediaCacheAsync(long beforeTs, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminDeleteMediaResponse?> DeleteLocalMediaAsync(long beforeTs, long sizeGt, bool keepProfiles, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminBackgroundUpdatesStatusResponse?> GetBackgroundUpdatesStatusAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<SynapseAdminBackgroundUpdatesEnabledResponse?> SetBackgroundUpdatesEnabledAsync(bool enabled, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task StartBackgroundUpdatesJobAsync(string jobName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}

