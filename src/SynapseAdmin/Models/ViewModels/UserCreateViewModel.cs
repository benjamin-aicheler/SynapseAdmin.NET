using System.ComponentModel.DataAnnotations;
using SynapseAdmin.Resources;

namespace SynapseAdmin.Models.ViewModels;

public class UserCreateViewModel
{
    [Required(ErrorMessageResourceType = typeof(SharedResources), ErrorMessageResourceName = "UserIdRequired")]
    [RegularExpression(@"^@[a-z0-9._=\-/]+:[a-zA-Z0-9.-]+(?::\d+)?$", ErrorMessageResourceType = typeof(SharedResources), ErrorMessageResourceName = "InvalidMxidFormat")]
    public string UserId { get; set; } = string.Empty;

    [Required(ErrorMessageResourceType = typeof(SharedResources), ErrorMessageResourceName = "PasswordRequired")]
    [MinLength(8, ErrorMessageResourceType = typeof(SharedResources), ErrorMessageResourceName = "PasswordTooShort")]
    public string Password { get; set; } = string.Empty;

    public string? DisplayName { get; set; }
    
    public bool Admin { get; set; }
    
    public bool Deactivated { get; set; }
}
