using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Localization;
using SynapseAdmin.Resources;

namespace SynapseAdmin.Models.ViewModels;

public enum LoginMethod
{
    Password,
    AccessToken
}

public class LoginViewModel : IValidatableObject
{
    public LoginMethod Method { get; set; } = LoginMethod.Password;
    public string Homeserver { get; set; } = "https://matrix.org";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string AccessToken { get; set; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var L = validationContext.GetService(typeof(IStringLocalizer<SharedResources>)) as IStringLocalizer<SharedResources>;

        if (string.IsNullOrWhiteSpace(Homeserver))
        {
            yield return new ValidationResult(
                L?["HomeserverUrlRequired"] ?? "Homeserver URL is required!",
                new[] { nameof(Homeserver) }
            );
        }

        if (Method == LoginMethod.Password)
        {
            if (string.IsNullOrWhiteSpace(Username))
            {
                yield return new ValidationResult(
                    L?["UsernameRequired"] ?? "Username is required!",
                    new[] { nameof(Username) }
                );
            }
            if (string.IsNullOrWhiteSpace(Password))
            {
                yield return new ValidationResult(
                    L?["PasswordRequired"] ?? "Password is required!",
                    new[] { nameof(Password) }
                );
            }
        }
        else if (Method == LoginMethod.AccessToken)
        {
            if (string.IsNullOrWhiteSpace(AccessToken))
            {
                yield return new ValidationResult(
                    L?["AccessTokenRequired"] ?? "Access token is required!",
                    new[] { nameof(AccessToken) }
                );
            }
        }
    }
}
