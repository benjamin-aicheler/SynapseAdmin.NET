using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Resources;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SynapseAdmin.Components.Pages
{
    public partial class RoomAliasesDialog
    {
        [CascadingParameter]
        private IMudDialogInstance MudDialog { get; set; } = null!;

        [Parameter]
        public string RoomId { get; set; } = string.Empty;

        [Parameter]
        public string? CanonicalAlias { get; set; }

        [Inject]
        public IMatrixSessionService MatrixSession { get; set; } = null!;

        [Inject]
        public IRoomService RoomService { get; set; } = null!;

        [Inject]
        public ISnackbar Snackbar { get; set; } = null!;

        [Inject]
        public IDialogService DialogService { get; set; } = null!;

        private MudForm? form;
        private bool success;
        private bool _isLoading = true;
        private bool _isSaving;
        private List<string> _aliases = [];
        private string _newAlias = string.Empty;

        protected override async Task OnInitializedAsync()
        {
            await LoadAliasesAsync();
        }

        private async Task LoadAliasesAsync()
        {
            _isLoading = true;
            StateHasChanged();

            var result = await RoomService.GetLocalRoomAliasesAsync(RoomId);
            if (result.Success && result.Data != null)
            {
                _aliases = result.Data;
            }
            else
            {
                Snackbar.Add(result.Message, result.Severity);
            }

            _isLoading = false;
            StateHasChanged();
        }

        private void Close() => MudDialog.Close();

        private string? ValidateAliasFormat(string val)
        {
            if (string.IsNullOrEmpty(val)) return null;
            if (!val.StartsWith("#")) return L["MustStartWithHash"].Value;
            
            var serverName = MatrixSession.Gateway?.ServerName;
            if (!string.IsNullOrEmpty(serverName) && !val.EndsWith($":{serverName}"))
            {
                return string.Format(L["MustEndWithServerName"].Value, serverName);
            }

            return null;
        }

        private async Task AddAliasAsync()
        {
            if (form == null) return;
            await form.ValidateAsync();
            if (!form.IsValid) return;

            _isSaving = true;
            StateHasChanged();

            var result = await RoomService.PutRoomAliasAsync(_newAlias, RoomId);

            _isSaving = false;
            if (result.Success)
            {
                Snackbar.Add(L["AliasAddedSuccessfully"], Severity.Success);
                _newAlias = string.Empty;
                if (form != null)
                {
                    await form.ResetValidationAsync();
                }
                await LoadAliasesAsync();
            }
            else
            {
                Snackbar.Add(result.Message, result.Severity);
            }
        }

        private async Task DeleteAliasAsync(string alias)
        {
            bool? confirm = await DialogService.ShowMessageBoxAsync(
                L["DeleteAlias"],
                string.Format(L["DeleteAliasConfirmText"].Value, alias),
                yesText: L["Delete"].Value, cancelText: L["Cancel"].Value);

            if (confirm == true)
            {
                _isSaving = true;
                StateHasChanged();

                var result = await RoomService.DeleteRoomAliasAsync(alias);

                _isSaving = false;
                if (result.Success)
                {
                    Snackbar.Add(L["AliasDeletedSuccessfully"], Severity.Success);
                    await LoadAliasesAsync();
                }
                else
                {
                    Snackbar.Add(result.Message, result.Severity);
                }
            }
        }
    }
}
