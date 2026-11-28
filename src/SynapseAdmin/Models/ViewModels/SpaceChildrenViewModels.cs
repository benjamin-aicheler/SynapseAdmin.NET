namespace SynapseAdmin.Models.ViewModels;

/// <summary>One page of a space's hierarchy (#588), for the room page's "Space children" tab.</summary>
public class SpaceChildrenPageViewModel
{
    /// <summary>
    /// The children the space names, in the homeserver's order. Only the first page has them; <c>null</c> on later
    /// pages, and on a first page without the space's own entry (this server isn't in the space).
    /// </summary>
    public List<SpaceChildViewModel>? Children { get; set; }

    /// <summary>The details of children this server is in, from this page.</summary>
    public List<SpaceChildSummaryViewModel> Summaries { get; set; } = [];

    /// <summary>The token for the next page, passed back unchanged; <c>null</c> when there is none.</summary>
    public string? NextBatch { get; set; }
}

/// <summary>A child the space links to with <c>m.space.child</c>.</summary>
public class SpaceChildViewModel
{
    public string RoomId { get; set; } = string.Empty;
    public bool Suggested { get; set; }
}

/// <summary>A child's details, as far as this server has them.</summary>
public class SpaceChildSummaryViewModel
{
    public string RoomId { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? CanonicalAlias { get; set; }
    public int? JoinedMembers { get; set; }
    public string? JoinRule { get; set; }
    public string? RoomType { get; set; }
}
