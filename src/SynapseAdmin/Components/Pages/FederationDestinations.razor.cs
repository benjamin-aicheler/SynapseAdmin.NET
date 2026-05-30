using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Components.Pages
{
    public partial class FederationDestinations : IDisposable
    {
        [Inject]
        public IMatrixSessionService MatrixSession { get; set; } = null!;
        [Inject]
        public IFederationService FederationService { get; set; } = null!;
        [Inject]
        public NavigationManager Navigation { get; set; } = null!;
        [Inject]
        public ISnackbar Snackbar { get; set; } = null!;
        [Inject]
        public IDialogService DialogService { get; set; } = null!;

        private MudTable<FederationDestinationListViewModel>? table;
        private int? totalDestinations;
        private List<FederationDestinationListViewModel>? _allDestinations;
        private string? _searchTerm;
        private readonly CancellationTokenSource _cts = new();

        private async Task ReloadTable()
        {
            _allDestinations = null;
            if (table != null)
            {
                await table.ReloadServerData();
            }
        }

        private async Task OnSearchChanged(string text)
        {
            _searchTerm = text;
            if (table != null)
            {
                await table.ReloadServerData();
            }
        }

        private async Task<TableData<FederationDestinationListViewModel>> ServerReload(TableState state, CancellationToken token)
        {
            if (_allDestinations == null)
            {
                var result = await FederationService.GetDestinationsAsync(0, 10000, SortDirection.Ascending, token: token);

                if (result.Success && result.Data != default)
                {
                    _allDestinations = result.Data.Destinations;
                    var totalCount = result.Data.Total;
                    while (_allDestinations.Count < totalCount)
                    {
                        var chunkResult = await FederationService.GetDestinationsAsync(_allDestinations.Count, 10000, SortDirection.Ascending, token: token);
                        if (chunkResult.Success && chunkResult.Data != default && chunkResult.Data.Destinations.Count > 0)
                        {
                            _allDestinations.AddRange(chunkResult.Data.Destinations);
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                else
                {
                    if (!result.Success && result.Severity != Severity.Normal)
                    {
                        Snackbar.Add(result.Message, result.Severity);
                    }
                    totalDestinations = 0;
                    return new TableData<FederationDestinationListViewModel>() { TotalItems = 0, Items = new List<FederationDestinationListViewModel>() };
                }
            }

            var filtered = _allDestinations.AsEnumerable();
            if (!string.IsNullOrEmpty(_searchTerm))
            {
                filtered = filtered.Where(x => x.Destination.Contains(_searchTerm, StringComparison.OrdinalIgnoreCase));
            }

            var isDescending = state.SortDirection == SortDirection.Descending;
            var orderBy = state.SortLabel ?? "destination";

            filtered = orderBy switch
            {
                "retry_last_ts" => isDescending ? filtered.OrderByDescending(x => x.RetryLastTsDateTime) : filtered.OrderBy(x => x.RetryLastTsDateTime),
                "retry_interval" => isDescending ? filtered.OrderByDescending(x => x.RetryInterval) : filtered.OrderBy(x => x.RetryInterval),
                "failure_ts" => isDescending ? filtered.OrderByDescending(x => x.FailureTsDateTime) : filtered.OrderBy(x => x.FailureTsDateTime),
                _ => isDescending ? filtered.OrderByDescending(x => x.Destination) : filtered.OrderBy(x => x.Destination)
            };

            var filteredList = filtered.ToList();
            totalDestinations = filteredList.Count;

            var pageItems = filteredList
                .Skip(state.Page * state.PageSize)
                .Take(state.PageSize)
                .ToList();

            StateHasChanged();
            return new TableData<FederationDestinationListViewModel>() { TotalItems = filteredList.Count, Items = pageItems };
        }

        private async Task ResetConnection(string destination)
        {
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L["ResetConnectionTitle"], 
                string.Format(L["ResetConnectionConfirmation"], destination), 
                yesText: L["Reset"], cancelText: L["Cancel"]);
                
            if (confirmed == true)
            {
                var result = await FederationService.ResetConnectionTimeoutAsync(destination, _cts.Token);
                Snackbar.Add(result.Message, result.Severity);
                if (result.Success)
                {
                    await ReloadTable();
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
