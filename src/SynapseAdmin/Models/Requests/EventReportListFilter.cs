namespace SynapseAdmin.Models.Requests;

/// <summary>
/// The filters of <c>GET /_synapse/admin/v1/event_reports</c> (#588). A <c>null</c> or empty field is left out. Synapse
/// combines the set fields with AND, matches <see cref="RoomId"/> and <see cref="ReporterUserId"/> as substrings and
/// <see cref="SenderUserId"/> exactly, and counts <c>total</c> with the same filter.
/// </summary>
/// <param name="RoomId">Sent as <c>room_id</c>.</param>
/// <param name="ReporterUserId">Sent as <c>user_id</c>: the user who reported the event and wrote the reason.</param>
/// <param name="SenderUserId">Sent as <c>event_sender_user_id</c>: the user the report was made against.</param>
public sealed record EventReportListFilter(string? RoomId = null, string? ReporterUserId = null, string? SenderUserId = null);
