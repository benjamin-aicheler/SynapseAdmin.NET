namespace SynapseAdmin.Models.ViewModels;

public class RegistrationTokenViewModel
{
    public string Token { get; set; } = string.Empty;
    public int? UsesAllowed { get; set; }
    public int Completed { get; set; }
    public int Pending { get; set; }
    public long? ExpiryTime { get; set; }
    
    public DateTime? ExpiryTimeDateTime => ExpiryTime.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(ExpiryTime.Value).DateTime : null;
}
