using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Components.Shared;
using SynapseAdmin.Infrastructure.Helpers;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Components.Pages;

/// <summary>
/// Asks for the room to join the shown user to and returns a <see cref="RoomJoinRequestViewModel"/> with the trimmed room
/// ID or alias and the servers to join through (#605, #606). The room is typed, or picked from the user's <c>leave</c>,
/// <c>invite</c> and <c>knock</c> memberships. It must have the form of a room ID or an alias. A room ID the user is
/// joined to or banned from is refused, because Synapse invites before it joins and the Matrix authorization rules
/// refuse an invite to a joined or banned user; an alias isn't checked so, because the dialog doesn't know its room. The
/// servers field is disabled for an alias, whose resolution returns its own servers, and none are returned then. The
/// dialog names the user and the conditions, so its submit button is the confirmation; the user page re-checks the shown
/// user and sends the join. The service checks the room and the servers again.
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
    private string? servers;

    private List<UserMembershipViewModel> Picks =>
        Memberships.Where(m => PickableMemberships.Contains(m.Membership, StringComparer.Ordinal)).ToList();

    private string Trimmed => roomId?.Trim() ?? string.Empty;

    private bool IsAlias => MatrixIdHelper.IsRoomAlias(Trimmed);

    private string ServersHelperText => IsAlias ? L["JoinServersAliasNote"].Value : L["JoinServersHint"].Value;

    // The disabled field of an alias shows no error: its servers aren't sent.
    private string? ServersError => IsAlias ? null : JoinServerInput.Validate(servers, L);

    // An empty room field shows no error yet; it only keeps the button disabled.
    private bool CanSubmit => Trimmed.Length > 0 && Validate(roomId) == null && ServersError == null;

    private string? Validate(string? value)
    {
        var id = value?.Trim() ?? string.Empty;
        if (id.Length == 0) return null;
        if (MatrixIdHelper.IsRoomAlias(id)) return null;
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
        MudDialog.Close(DialogResult.Ok(new RoomJoinRequestViewModel
        {
            Target = Trimmed,
            Servers = IsAlias ? [] : JoinServerInput.Split(servers)
        }));
    }

    private void Cancel() => MudDialog.Cancel();
}
