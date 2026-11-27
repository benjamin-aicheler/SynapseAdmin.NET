using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Infrastructure.Helpers;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Components.Pages;

/// <summary>
/// Asks for the user of a room recovery action and returns the trimmed user ID as a <see cref="string"/> (#587). It
/// names the room and the effect, so its submit button is the confirmation; the room page re-checks the shown room and
/// sends the request. Make room admin takes an empty ID (returned as <c>""</c>: the signed-in admin) or a full user ID on
/// any server. Join takes a full user ID on <see cref="ServerName"/>, compared exactly, that isn't one of
/// <see cref="JoinedMemberIds"/>. The service checks the ID again.
/// </summary>
public partial class RoomUserActionDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter] public RoomUserAction Action { get; set; }

    /// <summary>The room shown, as the page names it in its confirmations.</summary>
    [Parameter] public string RoomLabel { get; set; } = string.Empty;

    /// <summary>The signed-in admin, whom Make room admin targets when no user is entered.</summary>
    [Parameter] public string? SignedInUserId { get; set; }

    /// <summary>The signed-in admin's server, the only one whose users Join accepts.</summary>
    [Parameter] public string? ServerName { get; set; }

    /// <summary>The room's joined members as the page lists them; Join refuses them, as Synapse's invite would fail.</summary>
    [Parameter] public IReadOnlyCollection<string> JoinedMemberIds { get; set; } = [];

    private string? userId;

    private bool IsJoin => Action == RoomUserAction.Join;

    private string TitleIcon => IsJoin ? Icons.Material.Filled.PersonAdd : Icons.Material.Filled.AdminPanelSettings;

    private string HelperText => IsJoin
        ? L["JoinUserHint", ServerName ?? string.Empty].Value
        : L["MakeRoomAdminUserHint", SignedInUserId ?? string.Empty].Value;

    private string Trimmed => userId?.Trim() ?? string.Empty;

    // An empty Join field shows no error yet; it only keeps the button disabled.
    private bool CanSubmit => Validate(userId) == null && (!IsJoin || Trimmed.Length > 0);

    private string? Validate(string? value)
    {
        var id = value?.Trim() ?? string.Empty;
        if (id.Length == 0) return null;
        if (!MatrixIdHelper.IsUserId(id)) return L["InvalidUserId"].Value;
        if (!IsJoin) return null;
        if (!MatrixIdHelper.IsLocalUserId(id, ServerName)) return L["JoinUserNotLocal", ServerName ?? string.Empty].Value;
        if (JoinedMemberIds.Contains(id, StringComparer.Ordinal)) return L["UserAlreadyInRoom"].Value;
        return null;
    }

    private void Submit()
    {
        if (!CanSubmit) return;
        MudDialog.Close(DialogResult.Ok(Trimmed));
    }

    private void Cancel() => MudDialog.Cancel();
}
