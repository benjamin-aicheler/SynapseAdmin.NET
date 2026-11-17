using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using SynapseAdmin.Extensions.Mapping;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Services;
using SynapseAdmin.Tests.Components;
using SynapseAdmin.Tests.TestSupport;
using Xunit;

namespace SynapseAdmin.Tests;

public class MappingExtensionsTests
{
    [Fact]
    public void UserToViewModel_PreservesCreationTsInMilliseconds_AndAppliesDefaults()
    {
        // /_synapse/admin/v3/users returns creation_ts in milliseconds
        const long creationTsMs = 1700000000123L;
        var dto = new SynapseAdminUserListResult.SynapseAdminUserListResultUser
        {
            Name = "@alice:example.com",
            DisplayName = "Alice",
            AvatarUrl = "mxc://example.com/avatar1",
            Deactivated = false,
            Admin = null,
            CreationTs = creationTsMs,
            UserType = null,
            Locked = true,
            IsGuest = null
        };

        var vm = dto.ToViewModel();

        Assert.Equal("@alice:example.com", vm.UserId);
        Assert.Equal("Alice", vm.DisplayName);
        Assert.Equal("mxc://example.com/avatar1", vm.AvatarUrl);
        Assert.False(vm.Deactivated);
        Assert.False(vm.Admin);
        Assert.Equal(creationTsMs, vm.CreationTs);
        Assert.Equal("user", vm.UserType);
        Assert.True(vm.Locked);
        Assert.False(vm.IsGuest);
    }

    [Fact]
    public void UserToDetailViewModel_ConvertsCreationTsFromSecondsToMilliseconds()
    {
        // /_synapse/admin/v2/users/{userId} returns creation_ts in seconds
        const long creationTsSeconds = 1700000000L;
        var dto = new SynapseAdminUserListResult.SynapseAdminUserListResultUser
        {
            Name = "@bob:example.com",
            DisplayName = "Bob",
            AvatarUrl = "mxc://example.com/avatar2",
            Deactivated = true,
            Admin = true,
            CreationTs = creationTsSeconds,
            UserType = "support",
            Locked = false,
            ShadowBanned = true,
            ConsentVersion = null,
            ConsentServerNoticeSent = null,
            AppserviceId = null,
            ExpirationTs = 1785542400000L
        };

        var detailVm = dto.ToDetailViewModel();

        Assert.Equal("@bob:example.com", detailVm.UserId);
        Assert.Equal(creationTsSeconds * 1000L, detailVm.CreationTs);
        Assert.True(detailVm.Admin);
        Assert.True(detailVm.Deactivated);
        Assert.True(detailVm.ShadowBanned);
        Assert.Equal("support", detailVm.UserType);
        Assert.Equal(string.Empty, detailVm.ConsentVersion);
        Assert.Equal(string.Empty, detailVm.ConsentServerNoticeSent);
        Assert.Equal(string.Empty, detailVm.AppserviceId);
        Assert.Equal(1785542400000L, detailVm.ExpirationTs);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1785542400000L), detailVm.ExpirationTsDateTime);
    }

    [Fact]
    public void RoomMapping_MapsAvatarJsonKey_AndAppliesDefaults()
    {
        // Synapse Room Admin API uses "avatar" instead of "avatar_url"
        const string json = """
        {
            "room_id": "!room1:example.com",
            "name": "General",
            "canonical_alias": "#general:example.com",
            "joined_members": 42,
            "joined_local_members": 10,
            "federatable": true,
            "public": true,
            "avatar": "mxc://example.com/roompic",
            "forgotten": true,
            "tombstoned": false
        }
        """;

        var roomDto = JsonSerializer.Deserialize<SynapseAdminRoomListResult.SynapseAdminRoomListResultRoom>(json);
        Assert.NotNull(roomDto);

        var listVm = roomDto.ToViewModel();
        Assert.Equal("!room1:example.com", listVm.RoomId);
        Assert.Equal("mxc://example.com/roompic", listVm.AvatarUrl);
        Assert.Equal("1", listVm.Version);
        Assert.Equal(string.Empty, listVm.Creator);
        Assert.Equal(string.Empty, listVm.RoomType);
        Assert.True(listVm.Federated);
        Assert.True(listVm.Public);

        var detailVm = roomDto.ToDetailViewModel();
        Assert.Equal("!room1:example.com", detailVm.RoomId);
        Assert.Equal("mxc://example.com/roompic", detailVm.AvatarUrl);
        Assert.True(detailVm.Forgotten);
        Assert.False(detailVm.IsTombstoned);
    }

    [Fact]
    public void EventReportFederationAndRegistrationTokenMappings_MapAllProperties()
    {
        var report = new SynapseAdminEventReportListResult.SynapseAdminEventReportListResultReport
        {
            Id = "99",
            EventId = "$ev1",
            RoomId = "!r1:example.com",
            Sender = "@bad:example.com",
            UserId = "@reporter:example.com",
            ReceivedTs = 1700000000000L,
            Reason = null,
            Score = -100,
            CanonicalAlias = "#room:example.com"
        };

        var reportVm = report.ToViewModel();
        Assert.Equal("99", reportVm.Id);
        Assert.Equal(string.Empty, reportVm.Reason);
        Assert.Equal(-100, reportVm.Score);

        var dest = new SynapseAdminDestinationListResult.SynapseAdminDestinationListResultDestination
        {
            Destination = "matrix.org",
            RetryLastTs = 1000L,
            RetryInterval = 60000L,
            FailureTs = 500L,
            LastSuccessfulStreamOrdering = 42L
        };

        var destVm = dest.ToViewModel();
        Assert.Equal("matrix.org", destVm.Destination);
        Assert.Equal(60000L, destVm.RetryInterval);
        Assert.Equal(42L, destVm.LastSuccessfulStreamOrdering);

        var regToken = new SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken
        {
            Token = "invite-2026",
            UsesAllowed = 5,
            Pending = 1,
            Completed = 2,
            ExpiryTime = 1800000000000L
        };

        var regTokenVm = regToken.ToViewModel();
        Assert.Equal("invite-2026", regTokenVm.Token);
        Assert.Equal(5, regTokenVm.UsesAllowed);
        Assert.Equal(1, regTokenVm.Pending);
        Assert.Equal(2, regTokenVm.Completed);
        Assert.Equal(1800000000000L, regTokenVm.ExpiryTime);
    }

    // Every property of the first room has a distinct non-default value, so a missing or swapped assignment fails.
    private const string PublicRoomDirectoryJson = """
        {
          "chunk": [
            { "avatar_url": "mxc://example.com/avatar", "canonical_alias": "#lobby:example.com", "guest_can_join": true,
              "join_rule": "public", "name": "Lobby", "num_joined_members": 42, "room_id": "!lobby:example.com",
              "topic": "Welcome", "world_readable": true },
            { "room_id": "!bare:example.com" }
          ],
          "next_batch": "next-token", "prev_batch": "prev-token", "total_room_count_estimate": 7
        }
        """;

    [Theory]
    [InlineData(PublicRoomDirectoryJson)]
    [InlineData("{}")]
    public async Task PublicRoomDirectory_RoomServiceMapsAllProperties(string json)
    {
        var dto = JsonSerializer.Deserialize<MatrixPublicRoomDirectoryResult>(json)!;
        var gateway = Stub.Create<IMatrixGateway>(s =>
            s.On(nameof(IMatrixGateway.GetPublicRoomsAsync), _ => Task.FromResult<MatrixPublicRoomDirectoryResult?>(dto)));
        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));
        var localizer = new KeyLocalizer();
        using var cache = new MemoryCache(new MemoryCacheOptions());

        var result = await new RoomService(session, NullLogger<RoomService>.Instance, localizer, cache).GetPublicRoomsAsync(10);

        Assert.True(result.Success);
        AssertMapsPublicRoomDirectory(dto, result.Data!);
    }

    [Theory]
    [InlineData(PublicRoomDirectoryJson)]
    [InlineData("{}")]
    public void PublicRoomDirectoryMapping_MapsAllProperties(string json)
    {
        var dto = JsonSerializer.Deserialize<MatrixPublicRoomDirectoryResult>(json)!;

        AssertMapsPublicRoomDirectory(dto, dto.ToViewModel());
    }

    private static void AssertMapsPublicRoomDirectory(MatrixPublicRoomDirectoryResult dto, PublicRoomDirectoryViewModel vm)
    {
        Assert.Equal((dto.NextBatch, dto.PrevBatch, dto.TotalRoomCountEstimate), (vm.NextBatch, vm.PrevBatch, vm.TotalRoomCountEstimate));
        Assert.Equal(
            dto.Chunk.Select(c => (c.AvatarUrl, c.CanonicalAlias, c.GuestCanJoin, c.JoinRule, c.Name, c.NumJoinedMembers, c.RoomId, c.Topic, c.WorldReadable)),
            vm.Chunk.Select(c => (c.AvatarUrl, c.CanonicalAlias, c.GuestCanJoin, c.JoinRule, c.Name, c.NumJoinedMembers, c.RoomId, c.Topic, c.WorldReadable)));
    }

    // As above: the first entry of each fixture has distinct non-default values, so a missing or swapped assignment fails.
    private const string UserMediaJson = """
        {
          "media": [
            { "created_ts": 1700000000001, "last_access_ts": 1700000000002, "media_id": "media-a", "media_length": 4096,
              "media_type": "image/png", "quarantined_by": "@admin:example.com", "safe_from_quarantine": true, "upload_name": "cat.png" },
            { "media_id": "media-bare" }
          ],
          "total": 5
        }
        """;

    private const string UserMediaStatisticsJson = """
        {
          "users": [
            { "displayname": "Alice", "media_count": 3, "media_length": 4096, "user_id": "@alice:example.com" },
            { "user_id": "@bare:example.com" }
          ],
          "total": 2
        }
        """;

    private const string UserMembershipsJson = """{ "memberships": { "!a:example.com": "join", "!b:example.com": "leave" } }""";

    [Theory]
    [InlineData(UserMediaJson)]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task UserMediaPage_UserServiceMapsAllProperties(string json)
    {
        var dto = JsonSerializer.Deserialize<SynapseAdminUserMediaResult>(json);

        var result = await CreateUserService(nameof(IMatrixGateway.GetUserMediaAsync), dto).GetUserMediaPageAsync("@alice:example.com", 3, 10);

        Assert.True(result.Success);
        Assert.Equal((3, dto?.Total ?? 0L), (result.Data!.Offset, result.Data.TotalCount));
        AssertMapsUserMedia(dto?.Media ?? [], result.Data.Items);
    }

    [Theory]
    [InlineData(UserMediaStatisticsJson)]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task TopMediaUsers_UserServiceMapsAllProperties(string json)
    {
        var dto = JsonSerializer.Deserialize<UserMediaStatisticsResponse>(json);

        var result = await CreateUserService(nameof(IMatrixGateway.GetUserMediaStatisticsAsync), dto).GetTopMediaUsersAsync();

        Assert.True(result.Success);
        AssertMapsMediaStatistics(dto?.Users ?? [], result.Data!);
    }

    [Theory]
    [InlineData(UserMembershipsJson)]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task UserMemberships_UserServiceMapsAllEntries(string json)
    {
        var dto = JsonSerializer.Deserialize<SynapseAdminUserMembershipsResponse>(json);

        var result = await CreateUserService(nameof(IMatrixGateway.GetUserMembershipsAsync), dto).GetUserMembershipsAsync("@alice:example.com");

        Assert.True(result.Success);
        AssertMapsMemberships(dto?.Memberships ?? [], result.Data!);
    }

    [Fact]
    public void UserMediaItemMapping_MapsAllProperties()
    {
        var dto = JsonSerializer.Deserialize<SynapseAdminUserMediaResult>(UserMediaJson)!;

        AssertMapsUserMedia(dto.Media, dto.Media.Select(m => m.ToViewModel()));
    }

    [Fact]
    public void UserMediaStatisticMapping_MapsAllProperties()
    {
        var dto = JsonSerializer.Deserialize<UserMediaStatisticsResponse>(UserMediaStatisticsJson)!;

        AssertMapsMediaStatistics(dto.Users, dto.Users.Select(u => u.ToViewModel()));
    }

    [Theory]
    [InlineData(UserMembershipsJson)]
    [InlineData("{}")]
    public void UserMembershipsMapping_MapsAllEntries(string json)
    {
        var dto = JsonSerializer.Deserialize<SynapseAdminUserMembershipsResponse>(json)!;

        AssertMapsMemberships(dto.Memberships, dto.ToViewModels());
    }

    private static UserService CreateUserService<T>(string gatewayMethod, T? gatewayResult) where T : class
    {
        var gateway = Stub.Create<IMatrixGateway>(s => s.On(gatewayMethod, _ => Task.FromResult(gatewayResult)));
        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));
        var localizer = new KeyLocalizer();
        return new UserService(session, NullLogger<UserService>.Instance, localizer);
    }

    private static void AssertMapsUserMedia(IEnumerable<SynapseAdminUserMediaResult.MediaInfo> dto, IEnumerable<UserMediaItemViewModel> vm) =>
        Assert.Equal(
            dto.Select(m => (m.MediaId, m.UploadName, m.MediaType, m.MediaLength, m.CreatedTimestamp, m.QuarantinedBy, m.SafeFromQuarantine)),
            vm.Select(m => (m.MediaId, m.UploadName, m.MediaType, m.MediaLength, m.CreatedTimestamp, m.QuarantinedBy, m.SafeFromQuarantine)));

    private static void AssertMapsMediaStatistics(IEnumerable<UserMediaStatisticsResponse.UserMediaStatistic> dto, IEnumerable<UserMediaStatisticsViewModel> vm) =>
        Assert.Equal(dto.Select(u => (u.UserId, u.DisplayName, u.MediaCount, u.MediaLength)), vm.Select(u => (u.UserId, u.DisplayName, u.MediaCount, u.TotalSize)));

    private static void AssertMapsMemberships(Dictionary<string, string> dto, IEnumerable<UserMembershipViewModel> vm) =>
        Assert.Equal(dto.Select(kvp => (kvp.Key, kvp.Value)), vm.Select(m => (m.RoomId, m.Membership)));

    // As above. "{}" leaves current_updates null, which must map to an empty dictionary.
    private const string BackgroundUpdatesStatusJson = """
        {
          "enabled": true,
          "current_updates": {
            "main": { "name": "populate_stats_process_rooms", "total_item_count": 50, "total_duration_ms": 1234.5, "average_items_per_ms": 0.25 },
            "state": { "name": "bare_update" }
          }
        }
        """;

    [Theory]
    [InlineData(BackgroundUpdatesStatusJson)]
    [InlineData("{}")]
    public async Task BackgroundUpdatesStatus_ServiceMapsAllProperties(string json)
    {
        var dto = JsonSerializer.Deserialize<SynapseAdminBackgroundUpdatesStatusResponse>(json)!;
        var gateway = Stub.Create<IMatrixGateway>(s => s.On(nameof(IMatrixGateway.GetBackgroundUpdatesStatusAsync),
            _ => Task.FromResult<SynapseAdminBackgroundUpdatesStatusResponse?>(dto)));
        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));
        var localizer = new KeyLocalizer();

        var result = await new BackgroundUpdatesService(session, NullLogger<BackgroundUpdatesService>.Instance, localizer).GetStatusAsync();

        Assert.True(result.Success);
        AssertMapsBackgroundUpdatesStatus(dto, result.Data!);
    }

    [Theory]
    [InlineData(BackgroundUpdatesStatusJson)]
    [InlineData("{}")]
    public void BackgroundUpdatesStatusMapping_MapsAllProperties(string json)
    {
        var dto = JsonSerializer.Deserialize<SynapseAdminBackgroundUpdatesStatusResponse>(json)!;

        AssertMapsBackgroundUpdatesStatus(dto, dto.ToViewModel());
    }

    private static void AssertMapsBackgroundUpdatesStatus(SynapseAdminBackgroundUpdatesStatusResponse dto, BackgroundUpdatesStatusViewModel vm)
    {
        Assert.Equal(dto.Enabled, vm.Enabled);
        Assert.Equal(
            (dto.CurrentUpdates ?? []).Select(kvp => (kvp.Key, kvp.Value.Name, kvp.Value.TotalItemCount, kvp.Value.TotalDurationMs, kvp.Value.AverageItemsPerMs)),
            vm.CurrentUpdates.Select(kvp => (kvp.Key, kvp.Value.Name, kvp.Value.TotalItemCount, kvp.Value.TotalDurationMs, kvp.Value.AverageItemsPerMs)));
    }
}
