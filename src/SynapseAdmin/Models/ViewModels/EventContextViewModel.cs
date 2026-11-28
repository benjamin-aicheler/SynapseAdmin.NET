namespace SynapseAdmin.Models.ViewModels;

/// <summary>The events around an event in its room (#588), oldest first on both sides.</summary>
public class EventContextViewModel
{
    public List<EventContextItemViewModel> Before { get; set; } = [];

    /// <summary>The event itself, as the context API returns it (pruned if redacted).</summary>
    public EventContextItemViewModel? Event { get; set; }

    public List<EventContextItemViewModel> After { get; set; } = [];
}

/// <summary>One event of a context, as a row: no content beyond the message text. Never log <see cref="Body"/>.</summary>
public class EventContextItemViewModel
{
    public string EventId { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? Sender { get; set; }
    public string? StateKey { get; set; }
    public long? OriginServerTs { get; set; }
    public DateTimeOffset? Timestamp => OriginServerTs.HasValue
        ? DateTimeOffset.FromUnixTimeMilliseconds(OriginServerTs.Value)
        : null;

    /// <summary><c>content.body</c> when it is a string (messages); otherwise <c>null</c>.</summary>
    public string? Body { get; set; }

    /// <summary>Whether <c>unsigned.redacted_because</c> is set: the homeserver returned the event pruned.</summary>
    public bool IsRedacted { get; set; }
}
