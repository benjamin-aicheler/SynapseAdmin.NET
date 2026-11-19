using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// Which results the create user, publish room, renew account and purge history dialogs report in a snackbar: a
/// success (the dialog then closes) and every failure except a silent cancellation.
/// </summary>
public class DialogSnackbarTests : DetailPageTestBase
{
    private OperationResult _created = new();
    private OperationResult _published = new();
    private OperationResult<long> _renewed = new();
    private OperationResult<string> _purged = new();

    public DialogSnackbarTests()
    {
        Services.AddSingleton(Stub.Create<IUserService>(s => s
            .On(nameof(IUserService.CreateUserAsync), _ => Task.FromResult(_created))
            .On(nameof(IUserService.RenewAccountValidityAsync), _ => Task.FromResult(_renewed))));
        Services.AddSingleton(Stub.Create<IRoomService>(s => s
            .On(nameof(IRoomService.SetRoomDirectoryVisibilityAsync), _ => Task.FromResult(_published))
            .On(nameof(IRoomService.PurgeRoomHistoryAsync), _ => Task.FromResult(_purged))));
        // Replaces the base class's dialog stub: only a real dialog instance renders a dialog's form.
        Services.AddScoped<IDialogService, DialogService>();
    }

    /// <summary>Shows the dialog in a dialog provider and waits until its form has rendered.</summary>
    private async Task<IRenderedComponent<TDialog>> ShowDialogAsync<TDialog>(DialogParameters? parameters = null) where TDialog : IComponent
    {
        var provider = Render<MudDialogProvider>();
        await provider.InvokeAsync(() => Services.GetRequiredService<IDialogService>().ShowAsync<TDialog>("Dialog", parameters ?? []));
        var dialog = provider.FindComponent<TDialog>();
        dialog.WaitForElement("input");
        return dialog;
    }

    public static TheoryData<string, string> Dialogs()
    {
        var data = new TheoryData<string, string>();
        foreach (var dialog in new[] { nameof(UserCreateDialog), nameof(PublishRoomDialog), nameof(RenewAccountDialog), nameof(PurgeHistoryDialog) })
        {
            foreach (var kind in new[] { "success", "failure", "cancelled" }) data.Add(dialog, kind);
        }
        return data;
    }

    /// <summary>
    /// Each dialog is submitted with valid input; the purge dialog runs its purge directly, which skips the date form.
    /// </summary>
    [Theory]
    [MemberData(nameof(Dialogs))]
    public async Task Submit_ReportsEveryResultExceptACancellation(string dialog, string kind)
    {
        OperationResult result;
        switch (dialog)
        {
            case nameof(UserCreateDialog):
            {
                result = _created = Make<OperationResult>(kind);
                var cut = await ShowDialogAsync<UserCreateDialog>();
                var inputs = cut.FindAll("input");
                inputs[0].Change("@new:hs");
                inputs[1].Change("password1"); // the model requires at least 8 characters
                await InvokeHandler(cut, "Submit");
                break;
            }
            case nameof(PublishRoomDialog):
            {
                result = _published = Make<OperationResult>(kind);
                var cut = await ShowDialogAsync<PublishRoomDialog>();
                cut.Find("input").Change("!a:hs");
                await InvokeHandler(cut, "Submit");
                break;
            }
            case nameof(RenewAccountDialog):
            {
                result = _renewed = Make<OperationResult<long>>(kind);
                var cut = await ShowDialogAsync<RenewAccountDialog>(new DialogParameters { [nameof(RenewAccountDialog.UserId)] = "@a:hs" });
                await InvokeHandler(cut, "Submit");
                break;
            }
            default:
            {
                result = _purged = Make<OperationResult<string>>(kind);
                var cut = await ShowDialogAsync<PurgeHistoryDialog>(new DialogParameters { [nameof(PurgeHistoryDialog.RoomId)] = "!a:hs" });
                await cut.InvokeAsync(() => cut.Instance.ExecutePurgeAsync(new PurgeHistoryViewModel { PurgeUpToEventId = "$e" }));
                break;
            }
        }

        AssertSnackbars(kind == "cancelled" ? [] : [result]);
    }
}
