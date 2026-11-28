namespace SynapseAdmin.Models.ViewModels;

public class RoomMessagesViewModel
{
    public List<RoomMessageItemViewModel> Messages { get; set; } = [];
    /// <summary>The token to page older messages with (<c>dir=b</c>); <c>null</c> when there are none.</summary>
    public string? EndToken { get; set; }

    /// <summary>
    /// After a jump to a date (#588): the token to page newer messages with (<c>dir=f</c>); <c>null</c> when there are
    /// none, and for the newest messages.
    /// </summary>
    public string? NewerToken { get; set; }

    /// <summary>After a jump to a date: the event found for the time, which the list marks if it is a message.</summary>
    public string? AnchorEventId { get; set; }
}

public class RoomMessageItemViewModel
{
    public string EventId { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public DateTimeOffset OriginServerTs { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? StateKey { get; set; }
    public string? Content { get; set; }
    public string? Body { get; set; }
}
