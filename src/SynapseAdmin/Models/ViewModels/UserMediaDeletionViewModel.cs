namespace SynapseAdmin.Models.ViewModels;

/// <summary>How deleting all of a user's media ended (#599).</summary>
public enum UserMediaDeletionOutcome
{
    /// <summary>The media list is empty afterwards.</summary>
    AllDeleted,
    /// <summary>A request deleted nothing, but media remain (files the homeserver couldn't remove).</summary>
    SomeRemain,
    /// <summary>The request cap was reached with media left; running it again continues.</summary>
    CapReached,
    /// <summary>Cancelled (Stop or the page closed) after some media were deleted.</summary>
    Stopped,
    /// <summary>A request failed after some media were deleted.</summary>
    Failed,
    /// <summary>The deletion ended, but the media list couldn't be read to check what remains.</summary>
    RemainingUnknown
}

/// <summary>The result of deleting all of a user's media (#599).</summary>
public class UserMediaDeletionViewModel
{
    public int Deleted { get; init; }

    /// <summary>The uploads left afterwards, or <c>null</c> if not checked.</summary>
    public long? Remaining { get; init; }

    public UserMediaDeletionOutcome Outcome { get; init; }
}
