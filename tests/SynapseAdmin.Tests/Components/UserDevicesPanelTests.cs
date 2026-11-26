using System.Net;
using AngleSharp.Dom;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Components.Shared;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// The Devices tab of the user page (#598, AC-23): the device list with unknown values, rename, delete one or several
/// with a confirmation naming the user and devices, the protection of this session's device on the signed-in admin's
/// own account, and that a changed or closed panel neither shows a late answer nor runs a confirmed action.
/// </summary>
public class UserDevicesPanelTests : DetailPageTestBase
{
    private const string UserA = "@alice:hs";
    private const string UserB = "@bob:hs";
    private const string TargetA = "Alice Example (@alice:hs)";
    private const string AdminId = "@admin:hs";
    private const string SessionDevice = "APPSESSION";

    private static readonly DateTimeOffset SeenAt = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);

    private readonly Stub _devices;
    private string _signedInAs = AdminId;
    private string? _sessionDevice = SessionDevice;
    private OperationResult _actionResult = OperationResult.Ok("Done");
    private readonly Dictionary<string, Func<Task<OperationResult<List<UserDeviceViewModel>>>>> _lists = new();

    public UserDevicesPanelTests()
    {
        var gateway = Stub.Create<IMatrixGateway>(s => s
            .On("get_SupportsAdminApi", _ => true)
            .On("get_UserId", _ => _signedInAs)
            .On("get_DeviceId", _ => _sessionDevice));
        Services.AddSingleton(Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway)));
        Services.AddSingleton(Stub.Create<IUserDeviceService>(out _devices));
        _devices
            .On(nameof(IUserDeviceService.GetUserDevicesAsync), args =>
                _lists.TryGetValue((string)args[0]!, out var list) ? list() : Task.FromResult(Devices(Device("PHONE"))))
            .On(nameof(IUserDeviceService.RenameUserDeviceAsync), _ => Task.FromResult(_actionResult))
            .On(nameof(IUserDeviceService.DeleteUserDevicesAsync), _ => Task.FromResult(_actionResult));
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private static UserDeviceViewModel Device(string id, string? name = "Phone") => new() { DeviceId = id, DisplayName = name };

    private static OperationResult<List<UserDeviceViewModel>> Devices(params UserDeviceViewModel[] devices) =>
        OperationResult<List<UserDeviceViewModel>>.Ok(devices.ToList());

    private void UserHas(string userId, params UserDeviceViewModel[] devices) => _lists[userId] = () => Task.FromResult(Devices(devices));

    private int ListReads => _devices.CallsTo(nameof(IUserDeviceService.GetUserDevicesAsync)).Count;

    private IRenderedComponent<UserDevicesPanel> RenderPanel(string userId = UserA, string target = TargetA)
    {
        var cut = Render<UserDevicesPanel>(p => p.Add(x => x.UserId, userId).Add(x => x.Target, target));
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("table")));
        return cut;
    }

    private static string Text(IRenderedComponent<UserDevicesPanel> cut) => WebUtility.HtmlDecode(cut.Markup);

    private static IElement Row(IRenderedComponent<UserDevicesPanel> cut, string deviceId) =>
        cut.FindAll("tbody tr").Single(r => r.TextContent.Contains(deviceId, StringComparison.Ordinal));

    private IElement DeleteButton(IRenderedComponent<UserDevicesPanel> cut, string deviceId) =>
        Row(cut, deviceId).QuerySelectorAll("button").Single(b => b.TextContent.Trim() == L["Delete"].Value);

    private IElement DeleteSelectedButton(IRenderedComponent<UserDevicesPanel> cut) =>
        cut.FindAll("button").Single(b => b.TextContent.Contains(L["DeleteSelectedDevices", 0].Value.Split('(')[0].Trim(), StringComparison.Ordinal));

    private void DialogsReturn(object data, Task? closed = null) =>
        DialogStub.On(nameof(IDialogService.ShowAsync), _ => Task.FromResult(Stub.Create<IDialogReference>(s => s.On("get_Result",
            _ => CloseWith(data, closed)))));

    private static async Task<DialogResult?> CloseWith(object data, Task? closed)
    {
        if (closed != null) await closed;
        return DialogResult.Ok(data);
    }

    private void SelectAll(IRenderedComponent<UserDevicesPanel> cut, params string[] deviceIds) =>
        cut.InvokeAsync(() =>
        {
            var selected = GetField<HashSet<UserDeviceViewModel>>(cut.Instance, "_selected")!;
            foreach (var device in GetField<List<UserDeviceViewModel>>(cut.Instance, "_devices")!.Where(d => deviceIds.Contains(d.DeviceId)))
            {
                selected.Add(device);
            }
        });

    // ---- List ---------------------------------------------------------------------------------------------

    [Fact]
    public void ListsEveryField_AndUnknownForMissingValues()
    {
        UserHas(UserA,
            new UserDeviceViewModel { DeviceId = "QBUAZIFURK", DisplayName = "android", LastSeenIp = "192.0.2.1", LastSeenUserAgent = "Mozilla/5.0", LastSeen = SeenAt, Dehydrated = true },
            new UserDeviceViewModel { DeviceId = "NEVERSEEN" });

        var cut = RenderPanel();

        var seen = WebUtility.HtmlDecode(Row(cut, "QBUAZIFURK").InnerHtml);
        Assert.Contains("android", seen);
        Assert.Contains("192.0.2.1", seen);
        Assert.Contains("Mozilla/5.0", seen);
        Assert.Contains(SeenAt.DateTime.ToString("g"), seen);
        Assert.Contains(L["Dehydrated"].Value, seen);
        var unknown = Row(cut, "NEVERSEEN");
        Assert.Equal(4, unknown.QuerySelectorAll("td").Count(td => td.TextContent.Trim() == L["UnknownValue"].Value));
        Assert.DoesNotContain(L["Dehydrated"].Value, unknown.TextContent);
        Assert.Equal(UserA, Assert.Single(_devices.CallsTo(nameof(IUserDeviceService.GetUserDevicesAsync)))[0]);
    }

    [Fact]
    public void NoDevices_SaysSo()
    {
        UserHas(UserA);

        var cut = RenderPanel();

        Assert.Contains(L["NoDevices"].Value, Text(cut));
    }

    [Fact]
    public void LoadFailure_ShowsTheFailure_AndNoTable()
    {
        var failure = OperationResult<List<UserDeviceViewModel>>.Failure("Failed");
        _lists[UserA] = () => Task.FromResult(failure);

        var cut = Render<UserDevicesPanel>(p => p.Add(x => x.UserId, UserA).Add(x => x.Target, TargetA));

        cut.WaitForAssertion(() => AssertSnackbars(failure));
        Assert.Empty(cut.FindAll("table"));
    }

    [Fact]
    public void Refresh_ReadsTheDevicesAgain()
    {
        var cut = RenderPanel();

        cut.FindAll("button").Single(b => b.TextContent.Trim() == L["Refresh"].Value).Click();

        cut.WaitForAssertion(() => Assert.Equal(2, ListReads));
    }

    // ---- Rename -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Rename_PassesTheDevice_SendsTheName_AndReloads()
    {
        UserHas(UserA, Device("PHONE", "Old name"));
        DialogsReturn("New name");
        var cut = RenderPanel();

        await InvokeHandler(cut, "RenameDevice", GetField<List<UserDeviceViewModel>>(cut.Instance, "_devices")![0]);

        var shown = Assert.Single(DialogStub.CallsTo(nameof(IDialogService.ShowAsync)));
        Assert.Equal(L["RenameDevice"].Value, shown[0]);
        var parameters = (DialogParameters)shown[1]!;
        Assert.Equal((TargetA, "PHONE", "Old name"),
            ((string?)parameters[nameof(RenameDeviceDialog.Target)], (string?)parameters[nameof(RenameDeviceDialog.DeviceId)], (string?)parameters[nameof(RenameDeviceDialog.CurrentName)]));
        AssertDialogOptions(DialogPresets.Small());
        var call = Assert.Single(_devices.CallsTo(nameof(IUserDeviceService.RenameUserDeviceAsync)));
        Assert.Equal((UserA, "PHONE", "New name"), ((string?)call[0], (string?)call[1], (string?)call[2]));
        AssertSnackbars(_actionResult);
        Assert.Equal(2, ListReads);
    }

    [Fact]
    public async Task Rename_Cancelled_SendsNothing()
    {
        CancelDialogs();
        var cut = RenderPanel();

        await InvokeHandler(cut, "RenameDevice", GetField<List<UserDeviceViewModel>>(cut.Instance, "_devices")![0]);

        Assert.Empty(_devices.CallsTo(nameof(IUserDeviceService.RenameUserDeviceAsync)));
        AssertSnackbars();
    }

    [Fact]
    public async Task Rename_Failed_ShowsTheFailure_WithoutReloading()
    {
        _actionResult = OperationResult.Failure("Nope");
        DialogsReturn("New name");
        var cut = RenderPanel();

        await InvokeHandler(cut, "RenameDevice", GetField<List<UserDeviceViewModel>>(cut.Instance, "_devices")![0]);

        AssertSnackbars(_actionResult);
        Assert.Equal(1, ListReads);
    }

    // ---- Delete -------------------------------------------------------------------------------------------

    [Fact]
    public void DeleteOne_ConfirmsTheUserAndDevice_SendsIt_AndReloads()
    {
        UserHas(UserA, Device("PHONE"), Device("TABLET"));
        var cut = RenderPanel();

        DeleteButton(cut, "TABLET").Click();

        var (message, result) = Assert.Single(Confirmations);
        Assert.Equal($"{L["DeleteDeviceConfirmation", TargetA, "TABLET"]} {L["DeviceDeletionHint"]}", message);
        Assert.Equal(L["DeleteDevice"].Value, Assert.Single(DialogStub.CallsTo(nameof(IDialogService.ShowMessageBoxAsync)))[0]);
        result.SetResult(true);
        cut.WaitForAssertion(() => Assert.Equal(2, ListReads));
        var call = Assert.Single(_devices.CallsTo(nameof(IUserDeviceService.DeleteUserDevicesAsync)));
        Assert.Equal(UserA, call[0]);
        Assert.Equal(["TABLET"], (IEnumerable<string>)call[1]!);
        AssertSnackbars(_actionResult);
    }

    [Fact]
    public void DeleteSelected_ConfirmsEveryDevice_AndSendsThemTogether()
    {
        UserHas(UserA, Device("PHONE"), Device("TABLET"), Device("LAPTOP"));
        var cut = RenderPanel();
        SelectAll(cut, "PHONE", "LAPTOP");
        cut.Render(p => p.Add(x => x.UserId, UserA).Add(x => x.Target, TargetA));

        Assert.Contains(L["DeleteSelectedDevices", 2].Value, Text(cut));
        DeleteSelectedButton(cut).Click();

        var (message, result) = Assert.Single(Confirmations);
        var ids = GetField<HashSet<UserDeviceViewModel>>(cut.Instance, "_selected")!.Select(d => d.DeviceId).ToList();
        Assert.Equal($"{L["DeleteDevicesConfirmation", TargetA, string.Join(", ", ids), 2]} {L["DeviceDeletionHint"]}", message);
        Assert.Equal(L["DeleteDevices"].Value, Assert.Single(DialogStub.CallsTo(nameof(IDialogService.ShowMessageBoxAsync)))[0]);
        result.SetResult(true);
        cut.WaitForAssertion(() => Assert.Single(_devices.CallsTo(nameof(IUserDeviceService.DeleteUserDevicesAsync))));
        Assert.Equal(ids, (IEnumerable<string>)_devices.CallsTo(nameof(IUserDeviceService.DeleteUserDevicesAsync))[0][1]!);
        Assert.Equivalent(new[] { "PHONE", "LAPTOP" }, ids);
    }

    [Fact]
    public void DeleteSelected_IsDisabledWithoutASelection()
    {
        var cut = RenderPanel();

        Assert.True(DeleteSelectedButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public void Delete_NotConfirmed_SendsNothing()
    {
        var cut = RenderPanel();

        DeleteButton(cut, "PHONE").Click();
        Assert.Single(Confirmations).Result.SetResult(false);

        cut.WaitForAssertion(() => Assert.Single(_devices.CallsTo(nameof(IUserDeviceService.GetUserDevicesAsync))));
        Assert.Empty(_devices.CallsTo(nameof(IUserDeviceService.DeleteUserDevicesAsync)));
        AssertSnackbars();
    }

    // ---- The signed-in admin's own devices ----------------------------------------------------------------

    [Fact]
    public void OwnAccount_TheSessionDevice_CantBeDeleted_OthersCan()
    {
        UserHas(AdminId, Device(SessionDevice, "This browser"), Device("PHONE"));
        var cut = RenderPanel(AdminId, "Admin (@admin:hs)");

        Assert.True(DeleteButton(cut, SessionDevice).HasAttribute("disabled"));
        Assert.False(DeleteButton(cut, "PHONE").HasAttribute("disabled"));
        Assert.Contains(L["ThisSession"].Value, Row(cut, SessionDevice).TextContent);
        Assert.DoesNotContain(L["ThisSession"].Value, Row(cut, "PHONE").TextContent);
        Assert.Contains(L["OwnSessionDeviceNote"].Value, Text(cut));
    }

    [Fact]
    public void OwnAccount_ASelectionWithTheSessionDevice_CantBeDeleted()
    {
        UserHas(AdminId, Device(SessionDevice), Device("PHONE"));
        var cut = RenderPanel(AdminId, "Admin (@admin:hs)");

        SelectAll(cut, SessionDevice, "PHONE");
        cut.Render(p => p.Add(x => x.UserId, AdminId).Add(x => x.Target, "Admin (@admin:hs)"));
        Assert.True(DeleteSelectedButton(cut).HasAttribute("disabled"));

        cut.InvokeAsync(() => GetField<HashSet<UserDeviceViewModel>>(cut.Instance, "_selected")!.RemoveWhere(d => d.DeviceId == SessionDevice));
        cut.Render(p => p.Add(x => x.UserId, AdminId).Add(x => x.Target, "Admin (@admin:hs)"));
        Assert.False(DeleteSelectedButton(cut).HasAttribute("disabled"));
    }

    [Fact]
    public async Task OwnAccount_DeletingTheSessionDevice_AsksNothing_AndSendsNothing()
    {
        UserHas(AdminId, Device(SessionDevice));
        var cut = RenderPanel(AdminId, "Admin (@admin:hs)");

        // A timeout instead of a hang if a regression asks for confirmation, which nothing answers here.
        await InvokeHandler(cut, "DeleteDevice", GetField<List<UserDeviceViewModel>>(cut.Instance, "_devices")![0])
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(Confirmations);
        Assert.Empty(_devices.CallsTo(nameof(IUserDeviceService.DeleteUserDevicesAsync)));
    }

    /// <summary>Without the session's device ID, any of the admin's own devices could be it.</summary>
    [Fact]
    public void OwnAccount_SessionDeviceUnknown_NoDeviceCanBeDeleted()
    {
        _sessionDevice = null;
        UserHas(AdminId, Device("BROWSER"), Device("PHONE"));
        var cut = RenderPanel(AdminId, "Admin (@admin:hs)");

        Assert.True(DeleteButton(cut, "BROWSER").HasAttribute("disabled"));
        Assert.True(DeleteButton(cut, "PHONE").HasAttribute("disabled"));
        Assert.Contains(L["OwnSessionDeviceUnknownNote"].Value, Text(cut));
        Assert.DoesNotContain(L["ThisSession"].Value, Text(cut));
    }

    [Fact]
    public void AnotherUser_IsUnaffected_EvenWithTheSameDeviceId()
    {
        _sessionDevice = null;
        UserHas(UserA, Device(SessionDevice));
        var cut = RenderPanel();

        Assert.False(DeleteButton(cut, SessionDevice).HasAttribute("disabled"));
        Assert.DoesNotContain(L["OwnSessionDeviceNote"].Value, Text(cut));
        Assert.DoesNotContain(L["OwnSessionDeviceUnknownNote"].Value, Text(cut));
        Assert.DoesNotContain(L["ThisSession"].Value, Text(cut));
    }

    // ---- Stale selection (AC-5) ---------------------------------------------------------------------------

    [Fact]
    public void LateAnswerForAPreviousUser_IsDiscarded()
    {
        var late = Pending<OperationResult<List<UserDeviceViewModel>>>();
        _lists[UserA] = () => late.Task;
        UserHas(UserB, Device("BOBPHONE"));
        var cut = Render<UserDevicesPanel>(p => p.Add(x => x.UserId, UserA).Add(x => x.Target, TargetA));

        cut.Render(p => p.Add(x => x.UserId, UserB).Add(x => x.Target, "Bob (@bob:hs)"));
        cut.WaitForAssertion(() => Assert.Contains("BOBPHONE", cut.Markup));
        cut.InvokeAsync(() => late.SetResult(Devices(Device("ALICEPHONE"))));

        cut.WaitForAssertion(() => Assert.Contains("BOBPHONE", cut.Markup));
        Assert.DoesNotContain("ALICEPHONE", cut.Markup);
    }

    [Fact]
    public void ANewUser_ClearsThePreviousDevices_BeforeItsAnswer()
    {
        var pending = Pending<OperationResult<List<UserDeviceViewModel>>>();
        UserHas(UserA, Device("ALICEPHONE"));
        _lists[UserB] = () => pending.Task;
        var cut = RenderPanel();
        Assert.Contains("ALICEPHONE", cut.Markup);

        cut.Render(p => p.Add(x => x.UserId, UserB).Add(x => x.Target, "Bob (@bob:hs)"));

        Assert.DoesNotContain("ALICEPHONE", cut.Markup);
        Assert.Empty(cut.FindAll("table"));
    }

    [Fact]
    public void AnotherUserWhileConfirming_AbortsTheDeletion()
    {
        UserHas(UserA, Device("ALICEPHONE"));
        UserHas(UserB, Device("BOBPHONE"));
        var cut = RenderPanel();

        DeleteButton(cut, "ALICEPHONE").Click();
        cut.Render(p => p.Add(x => x.UserId, UserB).Add(x => x.Target, "Bob (@bob:hs)"));
        cut.WaitForAssertion(() => Assert.Contains("BOBPHONE", cut.Markup));
        Assert.Single(Confirmations).Result.SetResult(true);

        cut.WaitForAssertion(() => Assert.True(WarningShown()));
        Assert.Empty(_devices.CallsTo(nameof(IUserDeviceService.DeleteUserDevicesAsync)));
        Assert.Equal(L["SelectionChangedActionAborted"].Value, SnackbarStub.CallsTo(nameof(ISnackbar.Add)).Single()[0]);
    }

    [Fact]
    public async Task AnotherUserWhileRenaming_AbortsTheRename()
    {
        UserHas(UserA, Device("ALICEPHONE"));
        UserHas(UserB, Device("BOBPHONE"));
        var closed = new TaskCompletionSource();
        DialogsReturn("New name", closed.Task);
        var cut = RenderPanel();

        var action = InvokeHandler(cut, "RenameDevice", GetField<List<UserDeviceViewModel>>(cut.Instance, "_devices")![0]);
        cut.Render(p => p.Add(x => x.UserId, UserB).Add(x => x.Target, "Bob (@bob:hs)"));
        cut.WaitForAssertion(() => Assert.Contains("BOBPHONE", cut.Markup));
        closed.SetResult();
        await action;

        Assert.Empty(_devices.CallsTo(nameof(IUserDeviceService.RenameUserDeviceAsync)));
        Assert.True(WarningShown());
    }

    /// <summary>Leaving the tab or the page while confirming closes the panel; the confirmed deletion doesn't run.</summary>
    [Fact]
    public async Task ClosedPanelWhileConfirming_AbortsTheDeletion()
    {
        var cut = RenderPanel();

        DeleteButton(cut, "PHONE").Click();
        await cut.InvokeAsync(() => cut.Instance.Dispose());
        Assert.Single(Confirmations).Result.SetResult(true);

        cut.WaitForAssertion(() => Assert.True(WarningShown()));
        Assert.Empty(_devices.CallsTo(nameof(IUserDeviceService.DeleteUserDevicesAsync)));
    }
}
