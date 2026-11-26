using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Components.Shared;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// The user page's Devices and Recent connections tabs (#598): each loads only when opened, for the loaded user, and
/// the devices tab names the user as the page's confirmations do.
/// </summary>
public class UserDetailsDevicesTabTests : DetailPageTestBase
{
    private const string UserA = "@alice:hs";

    private readonly Stub _devices;

    public UserDetailsDevicesTabTests()
    {
        Services.AddSingleton(Stub.Create<IUserService>(s => s
            .On(nameof(IUserService.GetUserDetailsAsync), _ => Task.FromResult(OperationResult<UserDetailViewModel>.Ok(
                new UserDetailViewModel { UserId = UserA, DisplayName = "Alice Example" })))
            .On(nameof(IUserService.GetUserMediaPageAsync), _ => Task.FromResult(OperationResult<UserMediaPageViewModel>.Ok(new UserMediaPageViewModel())))
            .On(nameof(IUserService.GetUserMediaTotalsAsync), _ => Task.FromResult(OperationResult<UserMediaTotalsViewModel>.Ok(new UserMediaTotalsViewModel())))));
        Services.AddSingleton(Stub.Create<IUserDeviceService>(out _devices));
        _devices
            .On(nameof(IUserDeviceService.GetUserDevicesAsync), _ => Task.FromResult(OperationResult<List<UserDeviceViewModel>>.Ok([])))
            .On(nameof(IUserDeviceService.GetUserConnectionsAsync), _ => Task.FromResult(OperationResult<List<UserConnectionViewModel>>.Ok([])));
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private IRenderedComponent<UserDetails> RenderUser()
    {
        var cut = Render<UserDetails>(p => p.Add(x => x.UserId, UserA));
        cut.WaitForAssertion(() => Assert.Contains("Alice Example", cut.Markup));
        return cut;
    }

    private void OpenTab(IRenderedComponent<UserDetails> cut, string titleKey) =>
        cut.FindAll(".mud-tab").Single(t => t.TextContent.Trim() == L[titleKey].Value).Click();

    [Fact]
    public void TabsLoadNothing_UntilOpened()
    {
        var cut = RenderUser();

        Assert.Contains(cut.FindAll(".mud-tab"), t => t.TextContent.Trim() == L["Devices"].Value);
        Assert.Contains(cut.FindAll(".mud-tab"), t => t.TextContent.Trim() == L["RecentConnections"].Value);
        Assert.Empty(_devices.Calls);
    }

    [Fact]
    public void DevicesTab_ShowsTheLoadedUsersDevices()
    {
        var cut = RenderUser();

        OpenTab(cut, "Devices");

        var panel = cut.WaitForComponent<UserDevicesPanel>();
        Assert.Equal((UserA, $"Alice Example ({UserA})"), (panel.Instance.UserId, panel.Instance.Target));
        cut.WaitForAssertion(() => Assert.Equal(UserA, Assert.Single(_devices.CallsTo(nameof(IUserDeviceService.GetUserDevicesAsync)))[0]));
    }

    [Fact]
    public void RecentConnectionsTab_ShowsTheLoadedUsersConnections()
    {
        var cut = RenderUser();

        OpenTab(cut, "RecentConnections");

        cut.WaitForAssertion(() => Assert.Equal(UserA, Assert.Single(_devices.CallsTo(nameof(IUserDeviceService.GetUserConnectionsAsync)))[0]));
    }
}
