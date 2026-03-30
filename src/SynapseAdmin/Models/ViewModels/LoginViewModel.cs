namespace SynapseAdmin.Models.ViewModels;

public class LoginViewModel
{
    public string Homeserver { get; set; } = "https://matrix.org";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}
