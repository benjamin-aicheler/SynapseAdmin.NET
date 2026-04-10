using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Microsoft.Extensions.Localization;

namespace SynapseAdmin.Components.Pages;

public partial class UserCreateDialog
{
    [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = default!;

    [Inject] private IUserService UserService { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private UserCreateViewModel model = new();
    private MudForm form = default!;
    private bool success;
    private bool loading;

    private async Task Submit()
    {
        await form.ValidateAsync();

        if (form.IsValid)
        {
            loading = true;
            var result = await UserService.CreateUserAsync(model);
            loading = false;

            if (result.Success)
            {
                Snackbar.Add(result.Message, result.Severity);
                MudDialog.Close(DialogResult.Ok(true));
            }
            else
            {
                Snackbar.Add(result.Message, result.Severity);
            }
        }
    }

    private void Cancel() => MudDialog.Cancel();
}
