namespace SynapseAdmin.Interfaces;

public interface ITimeZoneService
{
    TimeZoneInfo UserTimeZone { get; }
    Task InitializeAsync();
    DateTimeOffset GetLocalNow();
    DateTimeOffset ToLocal(DateTimeOffset timestamp);

    /// <summary>
    /// Converts a wall-clock <paramref name="localDateTime"/> in <see cref="UserTimeZone"/> to a <see cref="DateTimeOffset"/>.
    /// Invalid times (DST spring-forward gaps) are advanced by one hour; ambiguous times (DST fall-back overlaps)
    /// resolve to the earlier (first / daylight-saving) occurrence.
    /// </summary>
    DateTimeOffset ToDateTimeOffset(DateTime localDateTime);

    bool IsAmbiguousLocalTime(DateTime localDateTime);
    string GetDisplayOffset(DateTime? forLocalDateTime = null);
}
