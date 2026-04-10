using System.ComponentModel.DataAnnotations;

namespace SynapseAdmin.Models.ViewModels;

public class UserCreateViewModel
{
    [Required]
    [RegularExpression(@"^@[a-z0-9._=\-/]+:[a-z0-9.-]+\.[a-z]{2,}$", ErrorMessage = "Invalid MXID format")]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [MinLength(8, ErrorMessage = "Password must be at least 8 characters")]
    public string Password { get; set; } = string.Empty;

    public string? DisplayName { get; set; }
    
    public bool Admin { get; set; }
    
    public bool Deactivated { get; set; }
}
