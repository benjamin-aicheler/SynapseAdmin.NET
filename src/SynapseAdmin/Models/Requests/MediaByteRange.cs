namespace SynapseAdmin.Models.Requests;

/// <summary>
/// A single HTTP byte range (RFC 9110, section 14.1.2), forwarded to the homeserver when media is downloaded.
/// </summary>
/// <param name="From">First byte position, or <c>null</c> for a suffix range ("the last <paramref name="To"/> bytes").</param>
/// <param name="To">Last byte position (inclusive), or <c>null</c> for an open-ended range.</param>
public readonly record struct MediaByteRange(long? From, long? To);
