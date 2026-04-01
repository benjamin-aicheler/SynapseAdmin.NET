namespace SynapseAdmin.Models.ViewModels;

public class RoomDetailViewModel
{
    public string RoomId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? CanonicalAlias { get; set; }
    public int JoinedMembers { get; set; }
    public int JoinedLocalMembers { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Creator { get; set; } = string.Empty;
    public string? Encryption { get; set; }
    public bool Federated { get; set; }
    public bool Public { get; set; }
    public string? AvatarUrl { get; set; }
    public string? JoinRules { get; set; }
    public string? GuestAccess { get; set; }
    public string? HistoryVisibility { get; set; }
    public string? RoomType { get; set; }
    public bool Forgotten { get; set; }
    
    // We could add memberships or state here later
    public bool IsTombstoned { get; set; }
    public string? ReplacementRoom { get; set; }
    
    public List<string> Members { get; set; } = [];
    public List<RoomStateEventViewModel> StateEvents { get; set; } = [];
}

public class RoomStateEventViewModel
{
    public string Type { get; set; } = string.Empty;
    public string? StateKey { get; set; }
    public string? Sender { get; set; }
    public string? RawContent { get; set; }
}
