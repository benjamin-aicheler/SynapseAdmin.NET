using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models.ViewModels;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// The edit dialog can clear each limit of an existing registration token on its own: an emptied field comes back as
/// <c>null</c>, which the update sends as an explicit null to remove the limit (#519).
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

    private async Task<(IRenderedComponent<RegistrationTokenDialog> Dialog, IDialogReference Reference)> ShowEditDialogAsync()
    {
        var provider = Render<MudDialogProvider>();
        IDialogReference reference = null!;
        await provider.InvokeAsync(async () => reference = await Services.GetRequiredService<IDialogService>().ShowAsync<RegistrationTokenDialog>("Dialog",
            new DialogParameters
            {
                [nameof(RegistrationTokenDialog.IsEdit)] = true,
                [nameof(RegistrationTokenDialog.ExistingToken)] = "tok",
                [nameof(RegistrationTokenDialog.ExistingUsesAllowed)] = 5,
                [nameof(RegistrationTokenDialog.ExistingExpiryDate)] = new DateTime(2030, 1, 2),
            }));
        var dialog = provider.FindComponent<RegistrationTokenDialog>();
        dialog.WaitForElement("input");
        return (dialog, reference);
    }

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
}
