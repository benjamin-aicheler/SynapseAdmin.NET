using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models.Responses;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SynapseAdmin.Components.Pages
{
    public partial class RoomDirectory
    {
        [Inject]
        public IRoomService RoomService { get; set; } = null!;

        [Inject]
        public ISnackbar Snackbar { get; set; } = null!;

        [Inject]
        public IDialogService DialogService { get; set; } = null!;

        private bool _isLoading;
        private string? _searchTerm;
        private MatrixPublicRoomDirectoryResult? _directoryResult;
        private string? _since;
        private readonly Stack<string?> _history = new();

        protected override async Task OnInitializedAsync()
        {
            await LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            _isLoading = true;
            StateHasChanged();

            var result = await RoomService.GetPublicRoomsAsync(50, _since, _searchTerm);

            _isLoading = false;
            if (result.Success && result.Data != null)
            {
                _directoryResult = result.Data;
            }
            else
            {
                _directoryResult = null;
                if (result.Severity != Severity.Normal)
                {
                    Snackbar.Add(result.Message, result.Severity);
                }
            }
            StateHasChanged();
        }

        private async Task OnSearchChanged(string text)
        {
            _searchTerm = text;
            _since = null;
            _history.Clear();
            await LoadDataAsync();
        }

        private bool CanGoBack => _history.Count > 0;
        private bool CanGoForward => !string.IsNullOrEmpty(_directoryResult?.NextBatch) && _directoryResult.Chunk.Count > 0;

        private async Task GoBackPage()
        {
            if (CanGoBack)
            {
                _since = _history.Pop();
                await LoadDataAsync();
            }
        }

        private async Task GoForwardPage()
        {
            if (CanGoForward && _directoryResult != null)
            {
                _history.Push(_since);
                _since = _directoryResult.NextBatch;
                await LoadDataAsync();
            }
        }

        private async Task OpenPublishRoomDialog()
        {
            var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small, FullWidth = true };
            var dialog = await DialogService.ShowAsync<PublishRoomDialog>(L["PublishRoomToDirectory"], options);
            var result = await dialog.Result;

            if (result != null && !result.Canceled)
            {
                _since = null;
                _history.Clear();
                await LoadDataAsync();
            }
        }

        private async Task RemoveFromDirectoryAsync(string roomId)
        {
            bool? confirm = await DialogService.ShowMessageBoxAsync(
                L["RemoveFromDirectory"],
                L["RemoveFromDirectoryConfirmText"],
                yesText: L["Remove"].Value, cancelText: L["Cancel"].Value);

            if (confirm == true)
            {
                var result = await RoomService.SetRoomDirectoryVisibilityAsync(roomId, "private");
                if (result.Success)
                {
                    Snackbar.Add(L["RoomRemovedFromDirectory"], Severity.Success);
                    await LoadDataAsync();
                }
                else
                {
                    Snackbar.Add(result.Message, result.Severity);
                }
            }
        }

        private async Task OpenManageAliasesDialog(string roomId, string? canonicalAlias)
        {
            var parameters = new DialogParameters<RoomAliasesDialog>
            {
                { x => x.RoomId, roomId },
                { x => x.CanonicalAlias, canonicalAlias }
            };

            var options = new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Medium, FullWidth = true };
            var dialog = await DialogService.ShowAsync<RoomAliasesDialog>(L["ManageAliases"], parameters, options);
            await dialog.Result;
            
            // Reload directory in case the canonical alias was modified or aliases changed
            await LoadDataAsync();
        }
    }
}
