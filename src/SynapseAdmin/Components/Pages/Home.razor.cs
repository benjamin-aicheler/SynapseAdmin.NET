using Microsoft.AspNetCore.Components;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models.ViewModels;
using MudBlazor;
using SynapseAdmin.Infrastructure.Gateways;

namespace SynapseAdmin.Components.Pages
{
    public partial class Home : IDisposable
    {
        [Inject]
        public IMatrixSessionService MatrixSession { get; set; } = null!;

        [Inject]
        public IUserService UserService { get; set; } = null!;

        [Inject]
        public IRoomService RoomService { get; set; } = null!;

        [Inject]
        public IEventReportService EventReportService { get; set; } = null!;

        private bool isSynapse;
        private int totalUsers;
        private int totalRooms;
        private int totalReports;
        private List<UserListViewModel> latestUsers = [];
        private List<EventReportListViewModel> latestReports = [];
        private List<RoomStatisticsViewModel> largestRooms = [];
        private List<UserMediaStatisticsViewModel> topMediaUsers = [];
        private string? serverVersion;
        private bool loading = true;
        private readonly CancellationTokenSource _cts = new();

        protected override async Task OnInitializedAsync()
        {
            if (MatrixSession.IsLoggedIn)
            {
                // We check if the gateway is Synapse
                isSynapse = MatrixSession.Gateway is SynapseAdminGateway;
                if (isSynapse)
                {
                    await LoadDashboardData();
                }
            }
            loading = false;
        }

        private async Task LoadDashboardData()
        {
            loading = true;
            try
            {
                var userTask = UserService.GetUserListAsync(0, 5, "creation_ts", SortDirection.Descending, _cts.Token);
                var roomTask = RoomService.GetRoomListAsync(0, 1, "room_id", SortDirection.Ascending, token: _cts.Token);
                var reportTask = EventReportService.GetEventReportsAsync(0, 5, SortDirection.Descending, _cts.Token);
                var largestRoomsTask = RoomService.GetLargestRoomsAsync(_cts.Token);
                var topMediaUsersTask = UserService.GetTopMediaUsersAsync(10, _cts.Token);
                var versionTask = MatrixSession.Gateway?.GetSynapseVersionAsync(_cts.Token) ?? Task.FromResult<SynapseAdmin.Models.Responses.SynapseVersionResponse?>(null);

                await Task.WhenAll(userTask, roomTask, reportTask, largestRoomsTask, topMediaUsersTask, versionTask);

                var userResult = await userTask;
                var roomResult = await roomTask;
                var reportResult = await reportTask;
                var largestRoomsResult = await largestRoomsTask;
                var topMediaUsersResult = await topMediaUsersTask;
                var versionResult = await versionTask;

                if (userResult.Success)
                {
                    totalUsers = userResult.Data.Total;
                    latestUsers = userResult.Data.Users;
                }

                if (roomResult.Success)
                {
                    totalRooms = roomResult.Data.Total;
                }

                if (reportResult.Success)
                {
                    totalReports = reportResult.Data.Total;
                    latestReports = reportResult.Data.Reports;
                }

                if (largestRoomsResult.Success && largestRoomsResult.Data != null)
                {
                    largestRooms = largestRoomsResult.Data;
                }

                if (topMediaUsersResult.Success && topMediaUsersResult.Data != null)
                {
                    topMediaUsers = topMediaUsersResult.Data;
                }

                if (versionResult != null)
                {
                    serverVersion = versionResult.ServerVersion;
                }
            }
            catch (OperationCanceledException)
            {
                // Silent cancellation
            }
            finally
            {
                loading = false;
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
