using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models.Requests;
using System;
using System.Threading.Tasks;

namespace SynapseAdmin.Components.Pages;

public partial class PurgeHistoryDialog
{
    [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = default!;

    [Inject] private IRoomService RoomService { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    [Parameter] public string RoomId { get; set; } = string.Empty;
    [Parameter] public string? PreselectedEventId { get; set; }
    [Parameter] public DateTimeOffset? PreselectedTimestamp { get; set; }

    private MudForm form = default!;
    private bool success;
    private bool loading;

    private string purgeType = "date";
    private DateTime? selectedDate = DateTime.Today;
    private TimeSpan? selectedTime = DateTime.Now.TimeOfDay;
    private string? eventId;
    private bool deleteLocalEvents;

    protected override void OnInitialized()
    {
        if (!string.IsNullOrEmpty(PreselectedEventId))
        {
            purgeType = "event";
            eventId = PreselectedEventId;
        }
        else if (PreselectedTimestamp.HasValue)
        {
            purgeType = "date";
            selectedDate = PreselectedTimestamp.Value.LocalDateTime.Date;
            selectedTime = PreselectedTimestamp.Value.LocalDateTime.TimeOfDay;
        }
    }

    private async Task Submit()
    {
        await form.ValidateAsync();

        if (form.IsValid)
        {
            var request = new SynapseAdminPurgeHistoryRequest
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
                var dto = new DateTimeOffset(localDateTime, TimeZoneInfo.Local.GetUtcOffset(localDateTime));
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
            var result = await RoomService.PurgeRoomHistoryAsync(RoomId, request);
            loading = false;

            if (result.Success && result.Data != null)
            {
                RoomService.SetActivePurgeId(RoomId, result.Data.PurgeId);
                Snackbar.Add(result.Message, result.Severity);
                MudDialog.Close(DialogResult.Ok(result.Data.PurgeId));
            }
            else
            {
                Snackbar.Add(result.Message, result.Severity);
            }
        }
    }

    private void Cancel() => MudDialog.Cancel();
}
