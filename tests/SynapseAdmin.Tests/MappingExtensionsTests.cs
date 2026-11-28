using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <summary>An unknown creation time (Tuwunel stores none) stays unknown, so the pages show "unknown", not 1970.</summary>
    [Fact]
    public void UserMappers_UnknownCreationTs_StaysNull()
    {
        var dto = new SynapseAdminUserListResult.SynapseAdminUserListResultUser { Name = "@carol:example.com", CreationTs = null };

        var list = dto.ToViewModel();
        var details = dto.ToDetailViewModel();

        Assert.Null(list.CreationTs);
        Assert.Null(list.CreationTsDateTime);
        Assert.Null(details.CreationTs);
        Assert.Null(details.CreationTsDateTime);
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

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, false)]
    public void UserToDetailViewModel_MapsSuspended(bool? suspended, bool expected)
    {
        // Synapse reports `suspended` on the v2 user details; a homeserver that leaves it out means not suspended.
        var dto = new SynapseAdminUserListResult.SynapseAdminUserListResultUser { Name = "@bob:example.com", Suspended = suspended };

        Assert.Equal(expected, dto.ToDetailViewModel().Suspended);
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

    [Theory]
    [InlineData("""{ "via": ["hs"], "suggested": true }""", true)]
    [InlineData("""{ "via": ["hs"], "suggested": false }""", false)]
    [InlineData("""{ "via": ["hs"], "suggested": "yes" }""", false)]
    [InlineData("""{ "via": ["hs"] }""", false)]
    [InlineData(null, false)]
    public void SpaceChild_MapsTheRoomAndWhetherItIsSuggested(string? content, bool suggested)
    {
        var link = new SynapseAdminSpaceChildEvent
        {
            Type = "m.space.child", StateKey = "!c:hs",
            Content = content == null ? null : JsonDocument.Parse(content).RootElement,
        };

        var vm = link.ToSpaceChildViewModel();

        Assert.Equal(("!c:hs", suggested), (vm.RoomId, vm.Suggested));
    }

    [Fact]
    public void SpaceChildSummary_MapsEveryProperty()
    {
        var room = new SynapseAdminRoomHierarchyRoom
        {
            RoomId = "!c:hs", Name = "Child", CanonicalAlias = "#c:hs", NumJoinedMembers = 4, JoinRule = "invite", RoomType = "m.space",
        };

        var vm = room.ToSpaceChildSummaryViewModel();

        Assert.Equal(("!c:hs", "Child", "#c:hs", 4, "invite", "m.space"), (vm.RoomId, vm.Name, vm.CanonicalAlias, vm.JoinedMembers, vm.JoinRule, vm.RoomType));
    }

    [Fact]
    public void SpaceChildSummary_NullFields_StayNull()
    {
        var vm = new SynapseAdminRoomHierarchyRoom { RoomId = "!c:hs" }.ToSpaceChildSummaryViewModel();

        Assert.Equal(("!c:hs", (string?)null, (string?)null, (int?)null, (string?)null, (string?)null),
            (vm.RoomId, vm.Name, vm.CanonicalAlias, vm.JoinedMembers, vm.JoinRule, vm.RoomType));
    }

    [Fact]
    public void RoomMessage_MapsEveryProperty()
    {
        var evt = new SynapseAdminEvent
        {
            EventId = "$e", Sender = "@u:hs", OriginServerTs = 1432735824653, Type = "m.room.message", StateKey = "",
            Content = JsonDocument.Parse("""{"body":"hi","msgtype":"m.text"}""").RootElement,
        };

        var vm = evt.ToMessageItemViewModel();

        Assert.Equal(("$e", "@u:hs", DateTimeOffset.FromUnixTimeMilliseconds(1432735824653), "m.room.message", ""),
            (vm.EventId, vm.Sender, vm.OriginServerTs, vm.Type, vm.StateKey));
        Assert.Equal(("""{"body":"hi","msgtype":"m.text"}""", "hi"), (vm.Content, vm.Body));
    }

    [Fact]
    public void RoomMessageContext_NewestFirst_ListsAfterReversedThenTheEventThenBefore()
    {
        static SynapseAdminEvent E(string id) => new() { EventId = id };
        var context = new SynapseAdminRoomMessageContextResponse
        {
            EventsBefore = [E("$b1"), E("$b2")],
            Event = E("$e"),
            EventsAfter = [E("$a1"), E("$a2")],
        };

        Assert.Equal(["$a2", "$a1", "$e", "$b1", "$b2"], context.NewestFirst().Select(e => e.EventId));
        Assert.Empty(new SynapseAdminRoomMessageContextResponse().NewestFirst());
    }

    [Fact]
    public void EventContextItem_MapsEveryProperty()
    {
        var evt = JsonNode.Parse("""
            {
                "event_id": "$e", "type": "m.room.message", "sender": "@u:hs", "state_key": "@v:hs",
                "origin_server_ts": 1432735824653,
                "content": { "body": "<b>hi</b>" },
                "unsigned": { "redacted_because": { "type": "m.room.redaction" } }
            }
            """)!.AsObject();

        var vm = evt.ToContextItemViewModel();

        Assert.Equal(("$e", "m.room.message", "@u:hs", "@v:hs"), (vm.EventId, vm.Type, vm.Sender, vm.StateKey));
        Assert.Equal((1432735824653L, DateTimeOffset.FromUnixTimeMilliseconds(1432735824653L)), (vm.OriginServerTs, vm.Timestamp!.Value));
        Assert.Equal(("<b>hi</b>", true), (vm.Body, vm.IsRedacted));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "event_id": 1, "type": null, "origin_server_ts": "soon", "content": { "body": { "x": 1 } }, "unsigned": {} }""")]
    [InlineData("""{ "content": "text", "unsigned": "x" }""")]
    public void EventContextItem_MissingOrUnexpectedFields_MapToEmptyDefaults(string json)
    {
        var vm = JsonNode.Parse(json)!.AsObject().ToContextItemViewModel();

        Assert.Equal((string.Empty, (string?)null, (string?)null, (string?)null), (vm.EventId, vm.Type, vm.Sender, vm.StateKey));
        Assert.Equal(((long?)null, (DateTimeOffset?)null, (string?)null, false), (vm.OriginServerTs, vm.Timestamp, vm.Body, vm.IsRedacted));
    }

    [Fact]
    public void EventContext_ListsBothSidesOldestFirst()
    {
        static JsonObject Event(string id) => new() { ["event_id"] = id };
        var context = new SynapseAdminEventContextResponse
        {
            EventsBefore = [Event("$b3"), Event("$b2"), Event("$b1")],
            Event = Event("$e"),
            EventsAfter = [Event("$a1"), Event("$a2")],
        };

        var vm = context.ToContextViewModel();

        Assert.Equal(["$b1", "$b2", "$b3"], vm.Before.Select(e => e.EventId));
        Assert.Equal("$e", vm.Event?.EventId);
        Assert.Equal(["$a1", "$a2"], vm.After.Select(e => e.EventId));
        Assert.Equal(["$b3", "$b2", "$b1"], context.EventsBefore.Select(e => e["event_id"]!.GetValue<string>()));
    }

    [Fact]
    public void EventContext_Empty_MapsToEmptyLists()
    {
        var vm = new SynapseAdminEventContextResponse().ToContextViewModel();

        Assert.Equal((0, (EventContextItemViewModel?)null, 0), (vm.Before.Count, vm.Event, vm.After.Count));
    }

    [Fact]
    public void EventReportDetails_MapsEveryProperty()
    {
        var report = new SynapseAdminEventReportDetailsResponse
        {
            Id = "42",
            ReceivedTs = 1570897107409L,
            RoomId = "!r1:example.com",
            Name = "Matrix HQ",
            CanonicalAlias = "#hq:example.com",
            EventId = "$ev1",
            UserId = "@reporter:example.com",
            Reason = "spam",
            Score = -100,
            Sender = "@bad:example.com",
            EventJson = new System.Text.Json.Nodes.JsonObject { ["type"] = "m.room.message" },
        };

        var vm = report.ToDetailViewModel("{ formatted }");

        Assert.Equal(("42", 1570897107409L, DateTimeOffset.FromUnixTimeMilliseconds(1570897107409L)), (vm.Id, vm.ReceivedTs, vm.ReceivedTsDateTime));
        Assert.Equal(("!r1:example.com", "Matrix HQ", "#hq:example.com"), (vm.RoomId, vm.RoomName, vm.CanonicalAlias));
        Assert.Equal(("$ev1", "@reporter:example.com", "spam", -100, "@bad:example.com"), (vm.EventId, vm.UserId, vm.Reason, vm.Score, vm.Sender));
        Assert.Equal("{ formatted }", vm.EventJson);
    }

    [Fact]
    public void EventReportDetails_NullFields_MapToEmptyDefaults()
    {
        var report = new SynapseAdminEventReportDetailsResponse { Id = "1", RoomId = "!r", EventId = "$e", UserId = "@u:hs" };

        var vm = report.ToDetailViewModel(string.Empty);

        Assert.Equal((string.Empty, (int?)null, (string?)null, (string?)null, (string?)null, string.Empty),
            (vm.Reason, vm.Score, vm.Sender, vm.RoomName, vm.CanonicalAlias, vm.EventJson));
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

    // Room deletion status (#518): Synapse's status in lower case, anything else is Unknown (the API is experimental).
    [Theory]
    [InlineData("scheduled", RoomDeletionState.Scheduled, true)]
    [InlineData("active", RoomDeletionState.Active, true)]
    [InlineData("complete", RoomDeletionState.Complete, false)]
    [InlineData("failed", RoomDeletionState.Failed, false)]
    [InlineData("Active", RoomDeletionState.Unknown, false)]
    [InlineData("paused", RoomDeletionState.Unknown, false)]
    [InlineData("", RoomDeletionState.Unknown, false)]
    [InlineData(null, RoomDeletionState.Unknown, false)]
    public void RoomDeletionStatusMapping_MapsAllProperties(string? status, RoomDeletionState state, bool running)
    {
        var dto = new SynapseAdminRoomDeleteStatusResponse { DeleteId = "delete-1", RoomId = "!room:example.com", Status = status!, Error = "boom" };

        var vm = dto.ToViewModel();

        Assert.Equal(("delete-1", "!room:example.com", state, running), (vm.DeleteId, vm.RoomId, vm.State, vm.IsRunning));
    }

    [Fact]
    public void RoomDeletionStatusMapping_MissingFields_BecomeEmpty()
    {
        var vm = JsonSerializer.Deserialize<SynapseAdminRoomDeleteStatusResponse>("{}")!.ToViewModel();

        Assert.Equal((string.Empty, string.Empty, RoomDeletionState.Unknown), (vm.DeleteId, vm.RoomId, vm.State));
    }

    // User redaction status (#600): Synapse's status in lower case, anything else is Unknown; only the failure count.
    [Theory]
    [InlineData("scheduled", UserRedactionState.Scheduled, true)]
    [InlineData("active", UserRedactionState.Active, true)]
    [InlineData("complete", UserRedactionState.Complete, false)]
    [InlineData("failed", UserRedactionState.Failed, false)]
    [InlineData("Active", UserRedactionState.Unknown, false)]
    [InlineData("pending", UserRedactionState.Unknown, false)]
    [InlineData("", UserRedactionState.Unknown, false)]
    [InlineData(null, UserRedactionState.Unknown, false)]
    public void UserRedactionStatusMapping_MapsAllProperties(string? status, UserRedactionState state, bool running)
    {
        var dto = new SynapseAdminUserRedactStatusResponse
        {
            Status = status!,
            FailedRedactions = new Dictionary<string, string> { ["$event1"] = "Not allowed", ["$event2"] = "Timeout" },
            Error = "boom"
        };

        var vm = dto.ToViewModel("redact-1");

        Assert.Equal(("redact-1", state, 2, running), (vm.RedactId, vm.State, vm.FailedRedactionCount, vm.IsRunning));
    }

    [Fact]
    public void UserRedactionStatusMapping_MissingFields_BecomeDefaults()
    {
        var vm = JsonSerializer.Deserialize<SynapseAdminUserRedactStatusResponse>("{}")!.ToViewModel("redact-1");

        Assert.Equal(("redact-1", UserRedactionState.Unknown, 0), (vm.RedactId, vm.State, vm.FailedRedactionCount));
    }

    [Fact]
    public void UserRedactionStatusMapping_EmptyFailures_CountZero()
    {
        var vm = new SynapseAdminUserRedactStatusResponse { Status = "complete", FailedRedactions = [] }.ToViewModel("redact-1");

        Assert.Equal((UserRedactionState.Complete, 0), (vm.State, vm.FailedRedactionCount));
    }

    // Room block state (#586): user_id is only sent for a blocked room, and Tuwunel may leave it out even then.
    [Theory]
    [InlineData(true, "@admin:example.com")]
    [InlineData(false, "@admin:example.com")]
    public void RoomBlockStatusMapping_MapsAllProperties(bool block, string userId)
    {
        var vm = new SynapseAdminRoomBlockStatusResponse { Block = block, UserId = userId }.ToViewModel();

        Assert.Equal((block, userId), (vm.IsBlocked, vm.BlockedBy));
    }

    [Fact]
    public void RoomBlockStatusMapping_MissingFields_BecomeEmpty()
    {
        var vm = JsonSerializer.Deserialize<SynapseAdminRoomBlockStatusResponse>("{}")!.ToViewModel();

        Assert.Equal((false, string.Empty), (vm.IsBlocked, vm.BlockedBy));
    }

    // ---- User devices and recent connections (#598) ---------------------------------------------------

    [Fact]
    public void UserDeviceToViewModel_CopiesEveryField()
    {
        var dto = new SynapseAdminUserDeviceListResult.SynapseAdminUserDevice
        {
            DeviceId = "QBUAZIFURK",
            DisplayName = "android",
            LastSeenIp = "192.0.2.1",
            LastSeenUserAgent = "Mozilla/5.0",
            LastSeenTs = 1474491775024,
            UserId = "@alice:example.com",
            Dehydrated = true
        };

        var vm = dto.ToViewModel();

        Assert.Equal("QBUAZIFURK", vm.DeviceId);
        Assert.Equal("android", vm.DisplayName);
        Assert.Equal("192.0.2.1", vm.LastSeenIp);
        Assert.Equal("Mozilla/5.0", vm.LastSeenUserAgent);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1474491775024), vm.LastSeen);
        Assert.True(vm.Dehydrated);
    }

    /// <summary>
    /// A device without a name, never seen (no IP, no time, or a time of <c>0</c>), on Tuwunel without a user agent and
    /// without <c>dehydrated</c>.
    /// </summary>
    [Theory]
    [InlineData("""{"device_id":"NEW"}""")]
    [InlineData("""{"device_id":"NEW","display_name":null,"last_seen_ip":null,"last_seen_user_agent":null,"last_seen_ts":0,"dehydrated":null}""")]
    public void UserDeviceToViewModel_MissingFields_AreNullOrFalse(string json)
    {
        var vm = JsonSerializer.Deserialize<SynapseAdminUserDeviceListResult.SynapseAdminUserDevice>(json)!.ToViewModel();

        Assert.Equal(("NEW", (string?)null, (string?)null, (string?)null, (DateTimeOffset?)null, false),
            (vm.DeviceId, vm.DisplayName, vm.LastSeenIp, vm.LastSeenUserAgent, vm.LastSeen, vm.Dehydrated));
    }

    [Fact]
    public void UserDevicesToViewModels_KeepsTheOrder()
    {
        var vms = new[] { new SynapseAdminUserDeviceListResult.SynapseAdminUserDevice { DeviceId = "B" }, new() { DeviceId = "A" } }.ToViewModels();

        Assert.Equal(["B", "A"], vms.Select(v => v.DeviceId));
    }

    [Fact]
    public void WhoisConnectionToViewModel_CopiesEveryField()
    {
        var vm = new SynapseAdminWhoisResponse.WhoisConnection { Ip = "192.0.2.1", LastSeen = 1417222374433, UserAgent = "Mozilla/5.0" }.ToViewModel();

        Assert.Equal(("192.0.2.1", "Mozilla/5.0", (DateTimeOffset?)DateTimeOffset.FromUnixTimeMilliseconds(1417222374433)),
            (vm.Ip, vm.UserAgent, vm.LastSeen));
    }

    [Fact]
    public void WhoisConnectionToViewModel_MissingFields_AreNull()
    {
        var vm = JsonSerializer.Deserialize<SynapseAdminWhoisResponse.WhoisConnection>("""{"last_seen":0}""")!.ToViewModel();

        Assert.Equal(((string?)null, (string?)null, (DateTimeOffset?)null), (vm.Ip, vm.UserAgent, vm.LastSeen));
    }

    /// <summary>Every device key and session in one list, newest first, connections without a time last.</summary>
    [Fact]
    public void WhoisToConnectionViewModels_FlattensAndSortsNewestFirst()
    {
        var whois = JsonSerializer.Deserialize<SynapseAdminWhoisResponse>("""
            {"user_id":"@alice:example.com","devices":{
              "":{"sessions":[{"connections":[{"ip":"192.0.2.1","last_seen":1000},{"ip":"192.0.2.2"}]},
                              {"connections":[{"ip":"192.0.2.3","last_seen":3000}]}]},
              "DEVICE":{"sessions":[{"connections":[{"ip":"192.0.2.4","last_seen":2000}]},{}]},
              "EMPTY":{}}}
            """)!;

        var vms = whois.ToConnectionViewModels();

        Assert.Equal(["192.0.2.3", "192.0.2.4", "192.0.2.1", "192.0.2.2"], vms.Select(v => v.Ip));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"devices":{}}""")]
    [InlineData("""{"devices":null}""")]
    public void WhoisToConnectionViewModels_NoConnections_IsEmpty(string json)
    {
        Assert.Empty(JsonSerializer.Deserialize<SynapseAdminWhoisResponse>(json)!.ToConnectionViewModels());
    }
}
