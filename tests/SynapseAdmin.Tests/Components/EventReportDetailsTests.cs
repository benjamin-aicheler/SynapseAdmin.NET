using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// A report's details (#588, AC-31): the Details button loads the report and opens a dialog with its fields and the
/// reported event's JSON as text. A failed or superseded load opens nothing and leaves the list as it is.
/// </summary>
public class EventReportDetailsTests : DetailPageTestBase
{
    private readonly Stub _reports;
    private readonly Dictionary<string, TaskCompletionSource<OperationResult<EventReportDetailViewModel>>> _details = new();

    public EventReportDetailsTests()
    {
        Services.AddSingleton(Stub.Create<IEventReportService>(out _reports));
        _reports
            .On(nameof(IEventReportService.GetEventReportsAsync), _ => Task.FromResult(OperationResult<(int, List<EventReportListViewModel>)>.Ok(
                (2, [Row("1"), Row("2")]))))
            .On(nameof(IEventReportService.GetEventReportDetailsAsync), args => Pending((string)args[0]!).Task);
        // Replaces the base class's dialog stub: only a real dialog service renders the dialog's content.
        Services.AddScoped<IDialogService, DialogService>();
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private static EventReportListViewModel Row(string id) => new() { Id = id, EventId = $"$event{id}", RoomId = "!room:hs", UserId = "@reporter:hs" };

    private static EventReportDetailViewModel Report(string id) => new()
    {
        Id = id,
        EventId = $"$event{id}/x",
        RoomId = "!room:hs",
        RoomName = "Lobby",
        CanonicalAlias = "#lobby:hs",
        UserId = "@reporter:hs",
        Reason = "spam",
        Score = -100,
        Sender = "@bad:hs",
        EventJson = "{\n  \"formatted_body\": \"<strong>bold</strong>\"\n}",
    };

    private TaskCompletionSource<OperationResult<EventReportDetailViewModel>> Pending(string reportId)
    {
        if (!_details.TryGetValue(reportId, out var pending))
        {
            pending = new TaskCompletionSource<OperationResult<EventReportDetailViewModel>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _details[reportId] = pending;
        }
        return pending;
    }

    private (IRenderedComponent<MudDialogProvider> Provider, IRenderedComponent<EventReports> Page) RenderPage()
    {
        var provider = Render<MudDialogProvider>();
        var page = Render<EventReports>();
        page.WaitForAssertion(() => Assert.Equal(2, DetailsButtons(page).Count));
        return (provider, page);
    }

    private List<AngleSharp.Dom.IElement> DetailsButtons(IRenderedComponent<EventReports> page) =>
        page.FindAll("button").Where(b => WebUtility.HtmlDecode(b.TextContent).Trim() == L["Details"].Value).ToList();

    /// <summary>Clicks a row's Details button; the task ends when the click handler has finished.</summary>
    private Task ClickDetails(IRenderedComponent<EventReports> page, int row) =>
        DetailsButtons(page)[row].ClickAsync(new MouseEventArgs());

    private static Task Finished(Task click) => click.WaitAsync(TimeSpan.FromSeconds(10));

    [Fact]
    public async Task Details_ShowsTheReportAndTheEventJsonAsText()
    {
        var (provider, page) = RenderPage();

        var click = ClickDetails(page, 0);
        _details["1"].SetResult(OperationResult<EventReportDetailViewModel>.Ok(Report("1")));
        await Finished(click);

        provider.WaitForAssertion(() => Assert.NotEmpty(provider.FindComponents<EventReportDetailsDialog>()));
        var markup = WebUtility.HtmlDecode(provider.Markup);
        Assert.Contains(string.Format(L["EventReportDetailsTitle"], "1"), markup);
        foreach (var text in new[] { "@reporter:hs", "spam", "-100", "Lobby", "#lobby:hs", "!room:hs", "$event1/x", "@bad:hs" })
        {
            Assert.Contains(text, markup);
        }

        var json = provider.Find("pre.event-report-json");
        Assert.Equal("{\n  \"formatted_body\": \"<strong>bold</strong>\"\n}", json.TextContent);
        Assert.Empty(json.QuerySelectorAll("strong"));
        AssertSnackbars();
    }

    [Fact]
    public async Task Details_LinksTheEventToEventForensics()
    {
        var (provider, page) = RenderPage();

        var click = ClickDetails(page, 0);
        _details["1"].SetResult(OperationResult<EventReportDetailViewModel>.Ok(Report("1")));
        await Finished(click);

        provider.WaitForAssertion(() =>
            Assert.Contains(provider.FindAll("a"), a => a.GetAttribute("href") == $"/event-forensics?eventId={Uri.EscapeDataString("$event1/x")}"));
    }

    [Theory]
    [InlineData("warning")]
    [InlineData("failure")]
    [InlineData("cancelled")]
    public async Task Details_FailedLoad_OpensNothing_AndKeepsTheList(string kind)
    {
        var result = kind == "warning"
            ? OperationResult<EventReportDetailViewModel>.Failure("Not found", Severity.Warning)
            : Make<OperationResult<EventReportDetailViewModel>>(kind);
        var (provider, page) = RenderPage();

        var click = ClickDetails(page, 0);
        _details["1"].SetResult(result);
        await Finished(click);

        AssertSnackbars(kind == "cancelled" ? [] : [result]);
        Assert.Empty(provider.FindComponents<EventReportDetailsDialog>());
        Assert.Equal(2, DetailsButtons(page).Count);
        Assert.Single(_reports.CallsTo(nameof(IEventReportService.GetEventReportsAsync)));
    }

    [Fact]
    public async Task Details_AnotherClick_CancelsTheFirstLoad_AndItsLateAnswerOpensNothing()
    {
        var (provider, page) = RenderPage();

        var firstClick = ClickDetails(page, 0);
        var secondClick = ClickDetails(page, 1);
        _details["2"].SetResult(OperationResult<EventReportDetailViewModel>.Ok(Report("2")));
        await Finished(secondClick);
        provider.WaitForAssertion(() => Assert.Single(provider.FindComponents<EventReportDetailsDialog>()));

        var first = _reports.CallsTo(nameof(IEventReportService.GetEventReportDetailsAsync))[0];
        Assert.Equal("1", first[0]);
        Assert.True(((CancellationToken)first[1]!).IsCancellationRequested);

        _details["1"].SetResult(OperationResult<EventReportDetailViewModel>.Ok(Report("1")));
        await Finished(firstClick);

        provider.Render();
        Assert.Equal("2", Assert.Single(provider.FindComponents<EventReportDetailsDialog>()).Instance.Report.Id);
        AssertSnackbars();
    }
}
