using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Components.Shared;

namespace SynapseAdmin.Components.Pages
{
    public partial class EventReports : IDisposable
    {
        [Inject]
        public IMatrixSessionService MatrixSession { get; set; } = null!;

        [Inject]
        public IEventReportService EventReportService { get; set; } = null!;

        [Inject]
        public ISnackbar Snackbar { get; set; } = null!;

        [Inject]
        public IDialogService DialogService { get; set; } = null!;

        [Inject]
        public ITimeZoneService TimeZoneService { get; set; } = null!;

        private MudTable<EventReportListViewModel>? table;
        private int? totalReports;
        private string? _roomId;
        private string? _reporterUserId;
        private string? _senderUserId;
        private readonly CancellationTokenSource _cts = new();
        private CancellationTokenSource? _detailsCts;

        /// <summary>Whether the count above the table counts matching reports rather than every report.</summary>
        private bool IsFiltered =>
            !string.IsNullOrWhiteSpace(_roomId) || !string.IsNullOrWhiteSpace(_reporterUserId) || !string.IsNullOrWhiteSpace(_senderUserId);

        protected override Task OnInitializedAsync() => TimeZoneService.InitializeAsync();

        private async Task ReloadTable()
        {
            if (table != null)
            {
                await table.ReloadServerData();
            }
        }

        /// <summary>
        /// A changed filter can leave fewer pages than the one shown, so the list starts again at the first page (#588).
        /// <c>NavigateTo</c> loads the page itself.
        /// </summary>
        private async Task ReloadFromFirstPage()
        {
            if (table == null) return;
            if (table.CurrentPage != 0)
            {
                table.NavigateTo(0);
                return;
            }
            await table.ReloadServerData();
        }

        private async Task OnRoomIdChanged(string text)
        {
            _roomId = text;
            await ReloadFromFirstPage();
        }

        private async Task OnReporterChanged(string text)
        {
            _reporterUserId = text;
            await ReloadFromFirstPage();
        }

        private async Task OnSenderChanged(string text)
        {
            _senderUserId = text;
            await ReloadFromFirstPage();
        }

        private async Task<TableData<EventReportListViewModel>> ServerReload(TableState state, CancellationToken token)
        {
            var offset = state.Page * state.PageSize;

            var result = await EventReportService.GetEventReportsAsync(offset, state.PageSize, state.SortDirection,
                new EventReportListFilter(_roomId, _reporterUserId, _senderUserId), token);

            if (result.Success && result.Data != default)
            {
                totalReports = result.Data.Total;
                StateHasChanged();
                return new TableData<EventReportListViewModel>() { TotalItems = result.Data.Total, Items = result.Data.Reports };
            }

            Snackbar.ShowFailure(result);

            return new TableData<EventReportListViewModel>() { TotalItems = 0, Items = new List<EventReportListViewModel>() };
        }

        /// <summary>
        /// Loads the report and shows it (#588). A click on another report cancels a load that is still running, and its
        /// late answer opens nothing. A failure leaves the list as it is.
        /// </summary>
        private async Task ShowDetails(string reportId)
        {
            _detailsCts?.Cancel();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            _detailsCts = cts;
            try
            {
                var result = await EventReportService.GetEventReportDetailsAsync(reportId, cts.Token);
                if (_detailsCts != cts || cts.IsCancellationRequested) return;
                if (Snackbar.ShowFailure(result) || result.Data == null) return;

                var parameters = new DialogParameters<EventReportDetailsDialog> { { x => x.Report, result.Data } };
                await DialogService.ShowAsync<EventReportDetailsDialog>(
                    string.Format(L["EventReportDetailsTitle"], result.Data.Id), parameters, DialogPresets.MediumWithCloseButton());
            }
            finally
            {
                if (_detailsCts == cts) _detailsCts = null;
                cts.Dispose();
            }
        }

        private async Task DeleteReport(string reportId)
        {
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L["DismissReportTitle"],
                L["DismissReportConfirmation"],
                yesText: L["Dismiss"], cancelText: L["Cancel"]);

            if (confirmed == true)
            {
                var result = await EventReportService.DeleteEventReportAsync(reportId, _cts.Token);
                Snackbar.ShowResult(result);
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
