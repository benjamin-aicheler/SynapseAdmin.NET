namespace SynapseAdmin.Models.ViewModels;

/// <summary>
/// The options of a redaction of a user's events, as <c>RedactUserEventsDialog</c> collects them (#600). With
/// <see cref="AllRooms"/>, the homeserver redacts in the rooms the user is joined to or banned from, and
/// <see cref="RoomIds"/> is ignored; otherwise only in <see cref="RoomIds"/>, which must not be empty, because an empty
/// list sent to the homeserver means the user's rooms.
/// </summary>
public class UserRedactionRequestViewModel
{
    /// <summary>Synapse's default for <c>limit</c>, which the dialog starts with.</summary>
    public const int DefaultLimit = 1000;

    public bool AllRooms { get; set; } = true;
    public List<string> RoomIds { get; set; } = [];

    /// <summary>Included in each redaction, so room members see it. Blank means none.</summary>
    public string? Reason { get; set; }

    /// <summary>How many of the user's events to search per room, newest first; at least 1.</summary>
    public int Limit { get; set; } = DefaultLimit;

    /// <summary>Whether the admin sends the redactions instead of the user.</summary>
    public bool UseAdmin { get; set; }
}

/// <summary>
/// Where a redaction of a user's events stands, from Synapse's <c>status</c> (#600). <see cref="Unknown"/> covers a
/// redaction the homeserver no longer knows (its status is kept 24 hours after it ends, or until Synapse restarts) and
/// any status this app doesn't know.
/// </summary>
public enum UserRedactionState
{
    Unknown,
    Scheduled,
    Active,
    Complete,
    Failed
}

/// <summary>
/// One redaction of a user's events (#600). The event IDs and error texts of failed redactions, and the error of a
/// failed redaction, are not part of it: they go only to the log.
/// </summary>
public class UserRedactionStatusViewModel
{
    public string RedactId { get; set; } = string.Empty;
    public UserRedactionState State { get; set; }

    /// <summary>How many events couldn't be redacted; only set for <see cref="UserRedactionState.Complete"/>.</summary>
    public int FailedRedactionCount { get; set; }

    /// <summary>Whether the redaction still has to finish: the user page shows its banner and checks it.</summary>
    public bool IsRunning => State is UserRedactionState.Scheduled or UserRedactionState.Active;
}
