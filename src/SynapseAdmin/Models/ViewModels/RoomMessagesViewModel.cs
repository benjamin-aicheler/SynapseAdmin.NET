namespace SynapseAdmin.Models.ViewModels;

public class RoomMessagesViewModel
{
    public List<RoomMessageItemViewModel> Messages { get; set; } = [];
    public string? StartToken { get; set; }
    public string? EndToken { get; set; }
}

public class RoomMessageItemViewModel
{
    public string EventId { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public DateTime OriginServerTs { get; set; }
    public string Type { get; set; } = string.Empty;
    public string? StateKey { get; set; }
    public string? Content { get; set; } 
    public string? Body { get; set; }
}
