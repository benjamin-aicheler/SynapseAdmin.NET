using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Components.Pages;

public partial class PurgeHistoryDialog : IDisposable
{
    [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = default!;

    [Inject] private IRoomService RoomService { get; set; } = default!;
    [Inject] private ITimeZoneService TimeZoneService { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    [Parameter] public string RoomId { get; set; } = string.Empty;
    [Parameter] public string? PreselectedEventId { get; set; }
    [Parameter] public DateTimeOffset? PreselectedTimestamp { get; set; }

    private MudForm form = default!;
    private bool success;
    private bool loading;

    private string purgeType = "date";
    private DateTime? selectedDate;
    private TimeSpan? selectedTime;
    private string? eventId;
    private bool deleteLocalEvents;
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

    protected override async Task OnInitializedAsync()
    {
        await TimeZoneService.InitializeAsync();

        var now = TimeZoneService.GetLocalNow();
        selectedDate = now.Date;
        selectedTime = new TimeSpan(now.Hour, now.Minute, 0);

        if (!string.IsNullOrEmpty(PreselectedEventId))
        {
            purgeType = "event";
            eventId = PreselectedEventId;
        }
        else if (PreselectedTimestamp.HasValue)
        {
            purgeType = "date";
            var local = TimeZoneService.ToLocal(PreselectedTimestamp.Value);
            selectedDate = local.Date;
            selectedTime = local.TimeOfDay;
        }
    }

    private async Task Submit()
    {
        await form.ValidateAsync();

        if (form.IsValid)
        {
            var request = new PurgeHistoryViewModel
            {
                DeleteLocalEvents = deleteLocalEvents
            };

            if (purgeType == "date")
            {
                if (selectedDate == null || selectedTime == null)
                {
                    Snackbar.Add(L["InvalidDateOrTime"], Severity.Warning);
                    return;
                }

                var localDateTime = selectedDate.Value.Date.Add(selectedTime.Value);
                var dto = TimeZoneService.ToDateTimeOffset(localDateTime);
                request.PurgeUpToTs = dto.ToUnixTimeMilliseconds();
            }
            else
            {
                if (string.IsNullOrWhiteSpace(eventId))
                {
                    Snackbar.Add(L["PurgeUpToRequired"], Severity.Warning);
                    return;
                }
                request.PurgeUpToEventId = eventId.Trim();
            }

            loading = true;
            var result = await RoomService.PurgeRoomHistoryAsync(RoomId, request, _cts.Token);
            loading = false;

            if (result.Success && !string.IsNullOrEmpty(result.Data))
            {
                RoomService.SetActivePurgeId(RoomId, result.Data);
                Snackbar.Add(result.Message, result.Severity);
                MudDialog.Close(DialogResult.Ok(result.Data));
            }
            else if (result.Severity != Severity.Normal)
            {
                Snackbar.Add(result.Message, result.Severity);
            }
        }
    }

    private void Cancel() => MudDialog.Cancel();

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
