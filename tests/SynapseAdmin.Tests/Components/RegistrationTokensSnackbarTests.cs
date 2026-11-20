using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// Which results the registration tokens page reports in a snackbar: every result except a silent cancellation, also a
/// token list that loaded without data.
/// </summary>
public class RegistrationTokensSnackbarTests : DetailPageTestBase
{
    private OperationResult<List<RegistrationTokenViewModel>> _tokens =
        OperationResult<List<RegistrationTokenViewModel>>.Ok([new() { Token = "tok" }]);
    private OperationResult _result = new();

    public RegistrationTokensSnackbarTests()
    {
        Services.AddSingleton(Stub.Create<IRegistrationTokenService>(s => s
            .On(nameof(IRegistrationTokenService.GetRegistrationTokensAsync), _ => Task.FromResult(_tokens))
            .On(nameof(IRegistrationTokenService.CreateRegistrationTokenAsync), _ => Task.FromResult(_result))
            .On(nameof(IRegistrationTokenService.UpdateRegistrationTokenAsync), _ => Task.FromResult(_result))
            .On(nameof(IRegistrationTokenService.DeleteRegistrationTokenAsync), _ => Task.FromResult(_result))));
        // The create and edit dialogs return a token at once.
        var dialog = Stub.Create<IDialogReference>(s => s.On("get_Result", _ =>
            Task.FromResult<DialogResult?>(DialogResult.Ok(new RegistrationTokenViewModel { Token = "new" }))));
        DialogStub.On(nameof(IDialogService.ShowAsync), _ => Task.FromResult(dialog));
    }

    [Theory]
    [InlineData("failure", true)]
    [InlineData("cancelled", false)]
    [InlineData("success", true)]
    public void TokenLoad_ReportsEveryResultWithoutTokensExceptACancellation(string kind, bool shown)
    {
        _tokens = Make<OperationResult<List<RegistrationTokenViewModel>>>(kind);

        Render<RegistrationTokens>();

        AssertSnackbars(shown ? [_tokens] : []);
    }

    [Fact]
    public async Task CreateAndEditDialogs_AreSmall_AndCloseOnEscape()
    {
        var cut = Render<RegistrationTokens>();

        await InvokeHandler(cut, "OpenCreateDialog");
        await InvokeHandler(cut, "OpenEditDialog", new RegistrationTokenViewModel { Token = "tok" });

        AssertDialogOptions(
            new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true },
            new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true });
    }

    /// <summary>After a success the list reloads, which reports nothing.</summary>
    [Theory]
    [InlineData("OpenCreateDialog", "success")]
    [InlineData("OpenCreateDialog", "failure")]
    [InlineData("OpenCreateDialog", "cancelled")]
    [InlineData("OpenEditDialog", "success")]
    [InlineData("OpenEditDialog", "failure")]
    [InlineData("OpenEditDialog", "cancelled")]
    [InlineData("DeleteToken", "success")]
    [InlineData("DeleteToken", "failure")]
    [InlineData("DeleteToken", "cancelled")]
    public async Task Action_ReportsEveryResultExceptACancellation(string handler, string kind)
    {
        _result = Make<OperationResult>(kind);
        var cut = Render<RegistrationTokens>();
        object?[] args = handler switch
        {
            "OpenEditDialog" => [new RegistrationTokenViewModel { Token = "tok" }],
            "DeleteToken" => ["tok"],
            _ => []
        };

        var action = InvokeHandler(cut, handler, args);
        if (handler == "DeleteToken")
        {
            cut.WaitForAssertion(() => Assert.Single(Confirmations));
            Confirmations[0].Result.SetResult(true);
        }
        await action;

        AssertSnackbars(kind == "cancelled" ? [] : [_result]);
    }
}
