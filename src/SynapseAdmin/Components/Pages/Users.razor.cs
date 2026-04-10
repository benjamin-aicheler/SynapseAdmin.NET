using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Resources;

namespace SynapseAdmin.Components.Pages
{
    public partial class Users
    {
        [Inject]
        public IMatrixSessionService MatrixSession { get; set; } = null!;
        [Inject]
        public IUserService UserService { get; set; } = null!;
        [Inject]
        public NavigationManager Navigation { get; set; } = null!;
        [Inject]
        public ISnackbar Snackbar { get; set; } = null!;
        [Inject]
        public IDialogService DialogService { get; set; } = null!;

        private MudTable<UserListViewModel>? table;
        private int? totalUsers;

        private async Task ReloadTable()
        {
            if (table != null)
            {
                await table.ReloadServerData();
            }
        }

        private async Task OpenCreateUserDialog()
        {
            var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true };
            var dialog = await DialogService.ShowAsync<UserCreateDialog>(L["CreateUser"], options);
            var result = await dialog.Result;

            if (result != null && !result.Canceled)
            {
                await ReloadTable();
            }
        }

        private async Task<TableData<UserListViewModel>> ServerReload(TableState state, CancellationToken token)
        {
            var offset = state.Page * state.PageSize;
            var orderBy = state.SortLabel ?? "name";

            var result = await UserService.GetUserListAsync(offset, state.PageSize, orderBy, state.SortDirection, token: token);
            
            if (result.Success && result.Data != default)
            {
                totalUsers = result.Data.Total;
                StateHasChanged();
                return new TableData<UserListViewModel>() { TotalItems = result.Data.Total, Items = result.Data.Users };
            }
            
            if (!result.Success)
            {
                Snackbar.Add(result.Message, result.Severity);
            }
            
            return new TableData<UserListViewModel>() { TotalItems = 0, Items = new List<UserListViewModel>() };
        }
    }
}
