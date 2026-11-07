using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Components.Pages;

public partial class RenewAccountDialog : IDisposable
{
    [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = default!;

    [Inject] private IUserService UserService { get; set; } = default!;
    [Inject] private ITimeZoneService TimeZoneService { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    [Parameter] public string UserId { get; set; } = string.Empty;
    [Parameter] public DateTimeOffset? CurrentExpirationTimestamp { get; set; }

    private MudForm form = default!;
    private bool success = true;
    private bool loading;

    private string expirationType = "default";
    private DateTime? selectedDate;
    private TimeSpan? selectedTime;
    private bool enableRenewalEmails = true;
    private readonly CancellationTokenSource _cts = new();

    private DateTime? SelectedLocalDateTime =>
        selectedDate.HasValue && selectedTime.HasValue
            ? selectedDate.Value.Date.Add(selectedTime.Value)
            : null;

    private string ActiveTimeZoneDisplay =>
        TimeZoneService.GetDisplayOffset(SelectedLocalDateTime);

    private string? ResolvedUtcPreview =>
        SelectedLocalDateTime.HasValue
            ? $"{TimeZoneService.ToDateTimeOffset(SelectedLocalDateTime.Value).UtcDateTime:yyyy-MM-dd HH:mm} UTC"
            : null;

    private bool IsSelectedLocalTimeAmbiguous =>
        SelectedLocalDateTime.HasValue && TimeZoneService.IsAmbiguousLocalTime(SelectedLocalDateTime.Value);

    protected override async Task OnInitializedAsync()
    {
        await TimeZoneService.InitializeAsync();

        var baseTime = CurrentExpirationTimestamp.HasValue && CurrentExpirationTimestamp.Value > DateTimeOffset.UtcNow
            ? TimeZoneService.ToLocal(CurrentExpirationTimestamp.Value)
            : TimeZoneService.GetLocalNow().AddDays(30);

        selectedDate = baseTime.Date;
        selectedTime = new TimeSpan(baseTime.Hour, baseTime.Minute, 0);
    }

    private async Task Submit()
    {
        await form.ValidateAsync();

        if (!form.IsValid) return;

        long? expirationTs = null;
        if (expirationType == "custom")
        {
            if (selectedDate == null || selectedTime == null)
            {
                Snackbar.Add(L["InvalidDateOrTime"], Severity.Warning);
                return;
            }

            var localDateTime = selectedDate.Value.Date.Add(selectedTime.Value);
            var dto = TimeZoneService.ToDateTimeOffset(localDateTime);
            expirationTs = dto.ToUnixTimeMilliseconds();
        }

        var request = new AccountValidityRenewViewModel
        {
            UserId = UserId,
            ExpirationTs = expirationTs,
            EnableRenewalEmails = enableRenewalEmails
        };

        loading = true;
        var result = await UserService.RenewAccountValidityAsync(request, _cts.Token);
        loading = false;

        if (result.Success)
        {
            Snackbar.Add(result.Message, result.Severity);
            MudDialog.Close(DialogResult.Ok(result.Data));
        }
        else if (result.Severity != Severity.Normal)
        {
            Snackbar.Add(result.Message, result.Severity);
        }
    }

    private void Cancel() => MudDialog.Cancel();

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}

