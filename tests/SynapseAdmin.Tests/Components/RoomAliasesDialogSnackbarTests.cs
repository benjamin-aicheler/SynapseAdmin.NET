using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Resources;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// Which results the room aliases dialog reports in a snackbar: an added or deleted alias, and every failure except a
/// silent cancellation, also an alias list that loaded without data.
/// </summary>
public class RoomAliasesDialogSnackbarTests : DetailPageTestBase
{
    private const string Alias = "#a:hs";

    private OperationResult<List<string>> _aliases = OperationResult<List<string>>.Ok([Alias]);
    private OperationResult _result = new();

    public RoomAliasesDialogSnackbarTests()
    {
        Services.AddSingleton(Stub.Create<IRoomService>(s => s
            .On(nameof(IRoomService.GetLocalRoomAliasesAsync), _ => Task.FromResult(_aliases))
            .On(nameof(IRoomService.PutRoomAliasAsync), _ => Task.FromResult(_result))
            .On(nameof(IRoomService.DeleteRoomAliasAsync), _ => Task.FromResult(_result))));
        // Replaces the base class's dialog stub: only a real dialog instance renders the dialog's form, and the delete
        // confirmation is a real message box.
        Services.AddScoped<IDialogService, DialogService>();
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    /// <summary>Shows the dialog in a dialog provider and waits until its aliases have loaded.</summary>
    private async Task<(IRenderedComponent<MudDialogProvider> Provider, IRenderedComponent<RoomAliasesDialog> Dialog)> ShowDialogAsync()
    {
        var provider = Render<MudDialogProvider>();
        await provider.InvokeAsync(() => Services.GetRequiredService<IDialogService>().ShowAsync<RoomAliasesDialog>(
            "Aliases", new DialogParameters { [nameof(RoomAliasesDialog.RoomId)] = "!a:hs" }));
        var dialog = provider.FindComponent<RoomAliasesDialog>();
        dialog.WaitForAssertion(() => Assert.Contains(L["LocalAliases"].Value, dialog.Markup));
        return (provider, dialog);
    }

    [Theory]
    [InlineData("failure", true)]
    [InlineData("cancelled", false)]
    [InlineData("success", true)]
    public async Task AliasLoad_ReportsEveryResultWithoutAliasesExceptACancellation(string kind, bool shown)
    {
        _aliases = Make<OperationResult<List<string>>>(kind);

        await ShowDialogAsync();

        AssertSnackbars(shown ? [_aliases] : []);
    }

    /// <summary>A valid alias is sent; after a success the list reloads, which reports nothing.</summary>
    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancelled")]
    public async Task AddAlias_ReportsEveryResultExceptACancellation(string kind)
    {
        _result = Make<OperationResult>(kind);
        var (_, dialog) = await ShowDialogAsync();
        dialog.Find("input").Change("#b:hs");

        await InvokeHandler(dialog, "AddAliasAsync");

        AssertSnackbars(kind == "cancelled" ? [] : [_result]);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancelled")]
    public async Task DeleteAlias_ReportsEveryResultExceptACancellation(string kind)
    {
        _result = Make<OperationResult>(kind);
        var (provider, dialog) = await ShowDialogAsync();

        var action = InvokeHandler(dialog, "DeleteAliasAsync", Alias);
        provider.WaitForElement(".mud-message-box");
        provider.FindAll("button").Single(b => b.TextContent.Trim() == L["Delete"].Value).Click();
        await action;

        AssertSnackbars(kind == "cancelled" ? [] : [_result]);
    }
}
