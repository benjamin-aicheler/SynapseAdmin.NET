namespace SynapseAdmin.Models.ViewModels;

public enum LoginMethod
{
    Password,
    AccessToken
}

public class LoginViewModel
{
    public LoginMethod Method { get; set; } = LoginMethod.Password;
    public string Homeserver { get; set; } = "https://matrix.org";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string AccessToken { get; set; } = "";
}
