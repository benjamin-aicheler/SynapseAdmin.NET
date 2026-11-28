using Microsoft.AspNetCore.Components;
using MudBlazor;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Components.Pages
{
    /// <summary>A loaded event report with the reported event's JSON (#588); read-only.</summary>
    public partial class EventReportDetailsDialog
    {
        [CascadingParameter]
        private IMudDialogInstance MudDialog { get; set; } = null!;

        [Inject]
        public ITimeZoneService TimeZoneService { get; set; } = null!;

        [Parameter, EditorRequired]
        public EventReportDetailViewModel Report { get; set; } = null!;

        private void Close() => MudDialog.Close();
    }
}
