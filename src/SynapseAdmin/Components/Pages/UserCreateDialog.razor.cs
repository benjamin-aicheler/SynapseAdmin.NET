using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Components.Pages;

public partial class UserCreateDialog : IDisposable
{
    [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = default!;

    [Inject] private IUserService UserService { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    private UserCreateViewModel model = new();
    private MudForm form = default!;
    private bool success;
    private bool loading;
    private readonly CancellationTokenSource _cts = new();

    private async Task Submit()
    {
        await form.ValidateAsync();

        if (form.IsValid)
        {
            loading = true;
            var result = await UserService.CreateUserAsync(model, _cts.Token);
            loading = false;

            if (result.Success)
            {
                Snackbar.Add(result.Message, result.Severity);
                MudDialog.Close(DialogResult.Ok(true));
            }
            else if (result.Severity != Severity.Normal)
            {
                Snackbar.Add(result.Message, result.Severity);
            }
        }
    }

    private void Cancel() => MudDialog.Cancel();

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
