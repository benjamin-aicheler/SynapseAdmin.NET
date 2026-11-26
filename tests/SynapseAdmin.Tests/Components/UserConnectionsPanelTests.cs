using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using SynapseAdmin.Components.Shared;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// The Recent connections tab of the user page (#598, AC-23): the connections in the service's order (newest first),
/// unknown values, the hint, and that a late answer for a previous user is discarded.
/// </summary>
public class UserConnectionsPanelTests : DetailPageTestBase
{
    private const string UserA = "@alice:hs";
    private const string UserB = "@bob:hs";

    private readonly Stub _devices;
    private readonly Dictionary<string, Func<Task<OperationResult<List<UserConnectionViewModel>>>>> _lists = new();

    public UserConnectionsPanelTests()
    {
        Services.AddSingleton(Stub.Create<IUserDeviceService>(out _devices));
        _devices.On(nameof(IUserDeviceService.GetUserConnectionsAsync), args =>
            _lists.TryGetValue((string)args[0]!, out var list) ? list() : Task.FromResult(Connections()));
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private static OperationResult<List<UserConnectionViewModel>> Connections(params UserConnectionViewModel[] connections) =>
        OperationResult<List<UserConnectionViewModel>>.Ok(connections.ToList());

    private IRenderedComponent<UserConnectionsPanel> RenderPanel(string userId = UserA)
    {
        var cut = Render<UserConnectionsPanel>(p => p.Add(x => x.UserId, userId));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("table")));
        return cut;
    }

    [Fact]
    public void ListsTheConnections_InTheServicesOrder_WithUnknownValues()
    {
        var newer = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_100_000);
        _lists[UserA] = () => Task.FromResult(Connections(
            new UserConnectionViewModel { Ip = "192.0.2.10", UserAgent = "Dalvik/2.1.0", LastSeen = newer },
            new UserConnectionViewModel()));

        var cut = RenderPanel();

        var rows = cut.FindAll("tbody tr");
        Assert.Equal(2, rows.Count);
        var first = WebUtility.HtmlDecode(rows[0].InnerHtml);
        Assert.Contains("192.0.2.10", first);
        Assert.Contains("Dalvik/2.1.0", first);
        Assert.Contains(newer.DateTime.ToString("g"), first);
        Assert.Equal(3, rows[1].QuerySelectorAll("td").Count(td => td.TextContent.Trim() == L["UnknownValue"].Value));
        Assert.Contains(L["RecentConnectionsHint"].Value, WebUtility.HtmlDecode(cut.Markup));
    }

    [Fact]
    public void NoConnections_SaysSo()
    {
        var cut = RenderPanel();

        Assert.Contains(L["NoRecentConnections"].Value, WebUtility.HtmlDecode(cut.Markup));
    }

    [Fact]
    public void LoadFailure_ShowsTheFailure_AndNoTable()
    {
        var failure = OperationResult<List<UserConnectionViewModel>>.Failure("Failed");
        _lists[UserA] = () => Task.FromResult(failure);

        var cut = Render<UserConnectionsPanel>(p => p.Add(x => x.UserId, UserA));

        cut.WaitForAssertion(() => AssertSnackbars(failure));
        Assert.Empty(cut.FindAll("table"));
    }

    [Fact]
    public void Refresh_ReadsTheConnectionsAgain()
    {
        var cut = RenderPanel();

        cut.FindAll("button").Single(b => b.TextContent.Trim() == L["Refresh"].Value).Click();

        cut.WaitForAssertion(() => Assert.Equal(2, _devices.CallsTo(nameof(IUserDeviceService.GetUserConnectionsAsync)).Count));
    }

    [Fact]
    public void LateAnswerForAPreviousUser_IsDiscarded()
    {
        var late = Pending<OperationResult<List<UserConnectionViewModel>>>();
        _lists[UserA] = () => late.Task;
        _lists[UserB] = () => Task.FromResult(Connections(new UserConnectionViewModel { Ip = "198.51.100.2" }));
        var cut = Render<UserConnectionsPanel>(p => p.Add(x => x.UserId, UserA));

        cut.Render(p => p.Add(x => x.UserId, UserB));
        cut.WaitForAssertion(() => Assert.Contains("198.51.100.2", cut.Markup));
        cut.InvokeAsync(() => late.SetResult(Connections(new UserConnectionViewModel { Ip = "192.0.2.1" })));

        cut.WaitForAssertion(() => Assert.Contains("198.51.100.2", cut.Markup));
        Assert.DoesNotContain("192.0.2.1", cut.Markup);
    }
}
