using LibMatrix.Homeservers;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using MudBlazor;

namespace SynapseAdmin.Components.Pages
{
    public partial class Home
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
        private bool loading = true;

        protected override async Task OnInitializedAsync()
        {
            if (MatrixSession.IsLoggedIn)
            {
                isSynapse = MatrixSession.AuthenticatedHomeserver is AuthenticatedHomeserverSynapse;
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
                var userTask = UserService.GetUserListAsync(0, 5, "creation_ts", SortDirection.Descending);
                var roomTask = RoomService.GetRoomListAsync(0, 1, "room_id", SortDirection.Ascending);
                var reportTask = EventReportService.GetEventReportsAsync(0, 5, SortDirection.Descending);
                var largestRoomsTask = RoomService.GetLargestRoomsAsync();

                await Task.WhenAll(userTask, roomTask, reportTask, largestRoomsTask);

                var userResult = await userTask;
                var roomResult = await roomTask;
                var reportResult = await reportTask;
                var largestRoomsResult = await largestRoomsTask;

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
            }
            finally
            {
                loading = false;
            }
        }
    }
}
