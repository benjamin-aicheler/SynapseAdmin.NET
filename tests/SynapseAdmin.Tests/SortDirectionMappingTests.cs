using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Services;
using SynapseAdmin.Tests.Components;
using SynapseAdmin.Tests.TestSupport;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// Characterization of the <c>dir</c> parameter ("f" forwards, "b" backwards) that the list services send to Synapse.
/// Each list keeps its natural order when unsorted (AGENTS.md, Shared Helpers, whenUnsorted): room and user lists read forwards (A→Z),
/// event reports and federation destinations read backwards (newest first). Room keys that Synapse sorts largest first
/// reverse the mapping (#541).
/// </summary>
public sealed class SortDirectionMappingTests : IDisposable
{
    private const string Rooms = "rooms";
    private const string Users = "users";
    private const string EventReports = "event reports";
    private const string FederationDestinations = "federation destinations";

    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public void Dispose() => _cache.Dispose();

    [Theory]
    [InlineData(Rooms, SortDirection.None, "f")]
    [InlineData(Rooms, SortDirection.Ascending, "f")]
    [InlineData(Rooms, SortDirection.Descending, "b")]
    [InlineData(Users, SortDirection.None, "f")]
    [InlineData(Users, SortDirection.Ascending, "f")]
    [InlineData(Users, SortDirection.Descending, "b")]
    [InlineData(EventReports, SortDirection.None, "b")]
    [InlineData(EventReports, SortDirection.Ascending, "f")]
    [InlineData(EventReports, SortDirection.Descending, "b")]
    [InlineData(FederationDestinations, SortDirection.None, "b")]
    [InlineData(FederationDestinations, SortDirection.Ascending, "f")]
    [InlineData(FederationDestinations, SortDirection.Descending, "b")]
    public async Task ListServices_SendTheirSynapseDir(string list, SortDirection direction, string expectedDir)
    {
        var gateway = Stub.Create<IMatrixGateway>(out var stub);
        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));
        var localizer = new KeyLocalizer();

        // The unconfigured stub gateway returns null, which every list service maps to an empty page.
        var (gatewayMethod, dirIndex) = list switch
        {
            Rooms => (nameof(IMatrixGateway.GetRoomListAsync), 3),
            Users => (nameof(IMatrixGateway.GetUserListAsync), 3),
            EventReports => (nameof(IMatrixGateway.GetEventReportListAsync), 2),
            _ => (nameof(IMatrixGateway.GetFederationDestinationListAsync), 2),
        };
        var success = list switch
        {
            Rooms => (await new RoomService(session, NullLogger<RoomService>.Instance, localizer, _cache)
                .GetRoomListAsync(0, 10, "name", direction)).Success,
            Users => (await new UserService(session, NullLogger<UserService>.Instance, localizer)
                .GetUserListAsync(0, 10, "name", direction)).Success,
            EventReports => (await new EventReportService(session, NullLogger<EventReportService>.Instance, localizer)
                .GetEventReportsAsync(0, 10, direction)).Success,
            _ => (await new FederationService(session, NullLogger<FederationService>.Instance, localizer)
                .GetDestinationsAsync(0, 10, direction)).Success,
        };

        Assert.True(success);
        var call = Assert.Single(stub.CallsTo(gatewayMethod));
        Assert.Equal(expectedDir, call[dirIndex]);
    }

    // Synapse sorts these room keys largest first, and dir=b reverses that, so an ascending (smallest first) sort sends
    // "b". Unsorted keeps Synapse's own order. The service maps to the documented API for every server and never reads
    // the brand: the gateway corrects servers that sort differently (#541).
    [Theory]
    [InlineData("joined_members", SortDirection.None, "f")]
    [InlineData("joined_members", SortDirection.Ascending, "b")]
    [InlineData("joined_members", SortDirection.Descending, "f")]
    [InlineData("joined_local_members", SortDirection.Ascending, "b")]
    [InlineData("joined_local_members", SortDirection.Descending, "f")]
    [InlineData("version", SortDirection.Ascending, "b")]
    [InlineData("version", SortDirection.Descending, "f")]
    [InlineData("state_events", SortDirection.Ascending, "b")]
    [InlineData("state_events", SortDirection.Descending, "f")]
    [InlineData("size", SortDirection.Ascending, "b")]
    [InlineData("size", SortDirection.Descending, "f")]
    [InlineData("name", SortDirection.None, "f")]
    [InlineData("name", SortDirection.Ascending, "f")]
    [InlineData("name", SortDirection.Descending, "b")]
    [InlineData("creator", SortDirection.Ascending, "f")]
    [InlineData("creator", SortDirection.Descending, "b")]
    [InlineData("public", SortDirection.Ascending, "f")]
    [InlineData("public", SortDirection.Descending, "b")]
    [InlineData("Joined_Members", SortDirection.Ascending, "f")]
    public async Task RoomList_SendsTheDocumentedDirForEachKey_WithoutReadingTheBrand(string orderBy, SortDirection direction, string expectedDir)
    {
        var gateway = Stub.Create<IMatrixGateway>(out var stub);
        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));

        var result = await new RoomService(session, NullLogger<RoomService>.Instance, new KeyLocalizer(), _cache)
            .GetRoomListAsync(0, 10, orderBy, direction);

        Assert.True(result.Success);
        var call = Assert.Single(stub.CallsTo(nameof(IMatrixGateway.GetRoomListAsync)));
        Assert.Equal((orderBy, expectedDir), ((string?)call[2], (string?)call[3]));
        Assert.Empty(stub.CallsTo("get_" + nameof(IMatrixGateway.ServerBrand)));
    }

    [Theory]
    [InlineData(SortDirection.None, "f", "f")]
    [InlineData(SortDirection.None, "b", "b")]
    [InlineData(SortDirection.Ascending, "f", "f")]
    [InlineData(SortDirection.Ascending, "b", "f")]
    [InlineData(SortDirection.Descending, "f", "b")]
    [InlineData(SortDirection.Descending, "b", "b")]
    public void ToSynapseDir_MapsSortedDirections_AndFallsBackWhenUnsorted(SortDirection direction, string whenUnsorted, string expected) =>
        Assert.Equal(expected, direction.ToSynapseDir(whenUnsorted));
}
