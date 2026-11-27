using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Infrastructure.Helpers;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Components.Pages;

/// <summary>
/// Asks for the room to join the shown user to and returns the trimmed room ID as a <see cref="string"/> (#605). The room
/// is typed, or picked from the user's <c>leave</c>, <c>invite</c> and <c>knock</c> memberships. It must have the form of a
/// room ID (no alias, #606), and a room the user is joined to or banned from is refused, because Synapse invites before
/// it joins and the Matrix authorization rules refuse an invite to a joined or banned user. The dialog names the user and
/// the conditions, so its submit button is the confirmation; the user page re-checks the shown user and sends the join.
/// The service checks the room ID again.
/// </summary>
public partial class UserJoinRoomDialog
{
    private static readonly string[] PickableMemberships = ["leave", "invite", "knock"];

    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;

    /// <summary>The user shown, as the page names it.</summary>
    [Parameter] public string UserLabel { get; set; } = string.Empty;

    /// <summary>The user's memberships as the page loaded them.</summary>
    [Parameter] public IReadOnlyList<UserMembershipViewModel> Memberships { get; set; } = [];

    private string? roomId;

    private List<UserMembershipViewModel> Picks =>
        Memberships.Where(m => PickableMemberships.Contains(m.Membership, StringComparer.Ordinal)).ToList();

    private string Trimmed => roomId?.Trim() ?? string.Empty;

    // An empty field shows no error yet; it only keeps the button disabled.
    private bool CanSubmit => Trimmed.Length > 0 && Validate(roomId) == null;

    private string? Validate(string? value)
    {
        var id = value?.Trim() ?? string.Empty;
        if (id.Length == 0) return null;
        if (!MatrixIdHelper.IsRoomId(id)) return L["InvalidRoomId"].Value;

        var membership = Memberships.FirstOrDefault(m => string.Equals(m.RoomId, id, StringComparison.Ordinal))?.Membership;
        return membership switch
        {
            "join" => L["UserAlreadyInRoom"].Value,
            "ban" => L["UserBannedFromRoom"].Value,
            _ => null
        };
    }

    private void Pick(string pickedRoomId) => roomId = pickedRoomId;

    private void Submit()
    {
        if (!CanSubmit) return;
        MudDialog.Close(DialogResult.Ok(Trimmed));
    }

    private void Cancel() => MudDialog.Cancel();
}
