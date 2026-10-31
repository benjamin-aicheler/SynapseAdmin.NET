using System.Text.Json;
using SynapseAdmin.Extensions.Mapping;
using SynapseAdmin.Models.Responses;
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
            AppserviceId = null
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
}
