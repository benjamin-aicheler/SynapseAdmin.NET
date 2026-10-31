namespace SynapseAdmin.Interfaces;

public interface ITimeZoneService
{
    TimeZoneInfo UserTimeZone { get; }
    Task InitializeAsync();
    DateTimeOffset GetLocalNow();
    DateTimeOffset ToLocal(DateTimeOffset timestamp);
    DateTimeOffset ToDateTimeOffset(DateTime localDateTime);
    string GetDisplayOffset(DateTime? forLocalDateTime = null);
}
