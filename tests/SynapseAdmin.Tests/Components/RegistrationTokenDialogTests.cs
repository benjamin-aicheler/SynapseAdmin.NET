using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// The edit dialog can clear each limit of an existing registration token on its own: an emptied field comes back as
/// <c>null</c>, which the update sends as an explicit null to remove the limit (#519). A cap of 0 makes the token
/// invalid without deleting it, and a custom token follows Synapse's format before anything is sent (#548).
/// </summary>
public class RegistrationTokenDialogTests : DetailPageTestBase
{
    public RegistrationTokenDialogTests()
    {
        // Replaces the base class's dialog stub: only a real dialog instance renders a dialog's form.
        Services.AddScoped<IDialogService, DialogService>();
        // Submit converts the picked wall-clock date; the base class's time zone stub has no ToDateTimeOffset.
        Services.AddSingleton(Stub.Create<ITimeZoneService>(s => s
            .On("get_UserTimeZone", _ => TimeZoneInfo.Utc)
            .On("ToDateTimeOffset", a => new DateTimeOffset((DateTime)a[0]!, TimeSpan.Zero))));
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private Task<(IRenderedComponent<RegistrationTokenDialog> Dialog, IDialogReference Reference)> ShowEditDialogAsync() =>
        ShowDialogAsync(new DialogParameters
        {
            [nameof(RegistrationTokenDialog.IsEdit)] = true,
            [nameof(RegistrationTokenDialog.ExistingToken)] = "tok",
            [nameof(RegistrationTokenDialog.ExistingUsesAllowed)] = 5,
            [nameof(RegistrationTokenDialog.ExistingExpiryDate)] = new DateTime(2030, 1, 2),
        });

    private Task<(IRenderedComponent<RegistrationTokenDialog> Dialog, IDialogReference Reference)> ShowCreateDialogAsync() =>
        ShowDialogAsync([]);

    private async Task<(IRenderedComponent<RegistrationTokenDialog> Dialog, IDialogReference Reference)> ShowDialogAsync(DialogParameters parameters)
    {
        var provider = Render<MudDialogProvider>();
        IDialogReference reference = null!;
        await provider.InvokeAsync(async () => reference = await Services.GetRequiredService<IDialogService>().ShowAsync<RegistrationTokenDialog>("Dialog", parameters));
        var dialog = provider.FindComponent<RegistrationTokenDialog>();
        dialog.WaitForElement("input");
        return (dialog, reference);
    }

    private static void EnterToken(IRenderedComponent<RegistrationTokenDialog> dialog, string value) =>
        dialog.Find(".mud-input-control:not(.mud-input-number-control):not(.mud-picker) input").Input(value);

    private static async Task<RegistrationTokenViewModel> SubmitAsync(IRenderedComponent<RegistrationTokenDialog> dialog, IDialogReference reference)
    {
        dialog.FindAll(".mud-dialog-actions button")[^1].Click(); // Save
        var result = await reference.Result;
        Assert.NotNull(result);
        Assert.False(result.Canceled);
        return Assert.IsType<RegistrationTokenViewModel>(result.Data);
    }

    private static void ClearUsesAllowed(IRenderedComponent<RegistrationTokenDialog> dialog) =>
        dialog.Find(".mud-input-number-control .mud-input-clear-button").Click();

    private static void ClearExpiry(IRenderedComponent<RegistrationTokenDialog> dialog) =>
        dialog.Find(".mud-picker .mud-input-clear-button").Click();

    [Fact]
    public async Task Edit_Unchanged_KeepsBothLimits()
    {
        var (dialog, reference) = await ShowEditDialogAsync();

        var vm = await SubmitAsync(dialog, reference);

        Assert.Equal(5, vm.UsesAllowed);
        Assert.NotNull(vm.ExpiryTime);
    }

    [Fact]
    public async Task Edit_ClearUsesAllowed_ReturnsNullUsesAndKeepsExpiry()
    {
        var (dialog, reference) = await ShowEditDialogAsync();

        ClearUsesAllowed(dialog);
        var vm = await SubmitAsync(dialog, reference);

        Assert.Null(vm.UsesAllowed);
        Assert.NotNull(vm.ExpiryTime);
    }

    [Fact]
    public async Task Edit_EmptyUsesAllowedInput_ReturnsNullUses()
    {
        var (dialog, reference) = await ShowEditDialogAsync();

        dialog.Find(".mud-input-number-control input").Change("");
        var vm = await SubmitAsync(dialog, reference);

        Assert.Null(vm.UsesAllowed);
    }

    [Fact]
    public async Task Edit_ClearExpiry_ReturnsNullExpiryAndKeepsUses()
    {
        var (dialog, reference) = await ShowEditDialogAsync();

        ClearExpiry(dialog);
        var vm = await SubmitAsync(dialog, reference);

        Assert.Equal(5, vm.UsesAllowed);
        Assert.Null(vm.ExpiryTime);
    }

    [Fact]
    public async Task Edit_ClearBoth_ReturnsNoLimits()
    {
        var (dialog, reference) = await ShowEditDialogAsync();

        ClearUsesAllowed(dialog);
        ClearExpiry(dialog);
        var vm = await SubmitAsync(dialog, reference);

        Assert.Null(vm.UsesAllowed);
        Assert.Null(vm.ExpiryTime);
    }

    [Fact]
    public async Task Edit_ZeroUsesAllowed_ReturnsZero()
    {
        var (dialog, reference) = await ShowEditDialogAsync();

        dialog.Find(".mud-input-number-control input").Change("0");
        var vm = await SubmitAsync(dialog, reference);

        Assert.Equal(0, vm.UsesAllowed);
    }

    [Fact]
    public async Task Create_ZeroUsesAllowed_ReturnsZero()
    {
        var (dialog, reference) = await ShowCreateDialogAsync();

        dialog.Find(".mud-input-number-control input").Change("0");
        var vm = await SubmitAsync(dialog, reference);

        Assert.Equal(0, vm.UsesAllowed);
    }

    /// <summary>An empty token field lets the homeserver generate one.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_BlankToken_ReturnsNoToken(string value)
    {
        var (dialog, reference) = await ShowCreateDialogAsync();

        EnterToken(dialog, value);
        var vm = await SubmitAsync(dialog, reference);

        Assert.Equal(string.Empty, vm.Token);
    }

    public static TheoryData<string> ValidTokens => new()
    {
        "a",
        "Invite-2026_spring.batch~1",
        new string('x', 60) + "._~-", // 64 characters, the maximum
    };

    [Theory]
    [MemberData(nameof(ValidTokens))]
    public async Task Create_ValidCustomToken_ReturnsIt(string value)
    {
        var (dialog, reference) = await ShowCreateDialogAsync();

        EnterToken(dialog, value);
        var vm = await SubmitAsync(dialog, reference);

        Assert.Equal(value, vm.Token);
    }

    public static TheoryData<string, string> InvalidTokens => new()
    {
        { new string('x', 65), "TokenTooLong" },
        { "with space", "TokenInvalidCharacters" },
        { "a/b", "TokenInvalidCharacters" },
        { "a+b", "TokenInvalidCharacters" },
        { "t\u00f6k", "TokenInvalidCharacters" }, // not ASCII
    };

    /// <summary>The homeserver would reject the token, so the dialog shows why and doesn't close, also when submitted.</summary>
    [Theory]
    [MemberData(nameof(InvalidTokens))]
    public async Task Create_InvalidCustomToken_ShowsTheReason_AndStaysOpen(string value, string messageKey)
    {
        var (dialog, reference) = await ShowCreateDialogAsync();

        EnterToken(dialog, value);
        await InvokeHandler(dialog, "Submit");

        dialog.WaitForAssertion(() => Assert.Contains(L[messageKey, 64].Value, WebUtility.HtmlDecode(dialog.Markup)));
        Assert.False(reference.Result.IsCompleted);
    }
}
