using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Components.Pages;

/// <summary>
/// Asks which of a user's events to redact and returns the options as a <see cref="UserRedactionRequestViewModel"/>
/// (#600, AC-25). The dialog sends nothing: the user page confirms, re-checks the shown user and starts the redaction.
/// </summary>
public partial class RedactUserEventsDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;

    /// <summary>The user shown, as the page names it in its confirmations.</summary>
    [Parameter] public string Target { get; set; } = string.Empty;

    /// <summary>The user's memberships as the page loaded them; "Selected rooms" offers every one of them.</summary>
    [Parameter] public IReadOnlyList<UserMembershipViewModel> Memberships { get; set; } = [];

    private readonly UserRedactionRequestViewModel model = new();
    private readonly HashSet<string> selectedRoomIds = new(StringComparer.Ordinal);

    /// <summary>Each room once, in the page's order.</summary>
    private IEnumerable<UserMembershipViewModel> Rooms => Memberships.DistinctBy(m => m.RoomId, StringComparer.Ordinal);

    /// <summary>The rooms Synapse redacts in for an empty room list: those the user is joined to or banned from.</summary>
    private int UserRoomCount => Rooms.Count(m => m.Membership is "join" or "ban");

    private bool CanSubmit => model.Limit >= 1 && (model.AllRooms || selectedRoomIds.Count > 0);

    private void SetAllRooms(bool allRooms) => model.AllRooms = allRooms;

    private void SetSelected(string roomId, bool selected)
    {
        if (selected) selectedRoomIds.Add(roomId);
        else selectedRoomIds.Remove(roomId);
    }

    private void Submit()
    {
        if (!CanSubmit) return;
        MudDialog.Close(DialogResult.Ok(new UserRedactionRequestViewModel
        {
            AllRooms = model.AllRooms,
            RoomIds = model.AllRooms ? [] : Rooms.Select(m => m.RoomId).Where(selectedRoomIds.Contains).ToList(),
            Reason = string.IsNullOrWhiteSpace(model.Reason) ? null : model.Reason,
            Limit = model.Limit,
            UseAdmin = model.UseAdmin
        }));
    }

    private void Cancel() => MudDialog.Cancel();
}
