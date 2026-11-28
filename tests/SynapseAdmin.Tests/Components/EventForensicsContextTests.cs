using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
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
/// The events around the shown event (#588, AC-32): requested with the event's room once the event has loaded, listed
/// oldest first with the event marked and the others linked, failures shown in the card only, and a superseded
/// event's context discarded.
/// </summary>
public class EventForensicsContextTests : DetailPageTestBase
{
    private const string Room = "!room:hs";

    private readonly List<(string EventId, TaskCompletionSource<OperationResult<EventForensicsViewModel>> Result)> _fetches = [];
    private readonly List<(string RoomId, string EventId, CancellationToken Token, TaskCompletionSource<OperationResult<EventContextViewModel>> Result)> _contexts = [];

    public EventForensicsContextTests()
    {
        Services.AddSingleton(Stub.Create<IEventForensicsService>(s => s
            .On(nameof(IEventForensicsService.FetchEventAsync), args =>
            {
                var fetch = Pending<OperationResult<EventForensicsViewModel>>();
                _fetches.Add(((string)args[0]!, fetch));
                return fetch.Task;
            })
            .On(nameof(IEventForensicsService.GetEventContextAsync), args =>
            {
                var context = Pending<OperationResult<EventContextViewModel>>();
                _contexts.Add(((string)args[0]!, (string)args[1]!, (CancellationToken)args[2]!, context));
                return context.Task;
            })));
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    private static OperationResult<EventForensicsViewModel> Loaded(string eventId, string? roomId = Room) =>
        OperationResult<EventForensicsViewModel>.Ok(new EventForensicsViewModel
        {
            EventId = eventId, RoomId = roomId, Type = "m.room.message", RawJson = "{}"
        });

    private static EventContextItemViewModel Item(string eventId, string? body = null, bool redacted = false) =>
        new() { EventId = eventId, Type = "m.room.message", Sender = "@u:hs", Body = body ?? $"text of {eventId}", IsRedacted = redacted };

    private static OperationResult<EventContextViewModel> Context(string eventId, params string[] neighbours) =>
        OperationResult<EventContextViewModel>.Ok(new EventContextViewModel
        {
            Before = [Item($"{neighbours[0]}1"), Item($"{neighbours[0]}2")],
            Event = Item(eventId),
            After = [Item($"{neighbours[1]}1")],
        });

    private IRenderedComponent<EventForensics> RenderEvent(string eventId)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/event-forensics?eventId={Uri.EscapeDataString(eventId)}");
        return Render<EventForensics>();
    }

    private void Navigate(IRenderedComponent<EventForensics> cut, string eventId) =>
        cut.InvokeAsync(() => Services.GetRequiredService<NavigationManager>().NavigateTo($"/event-forensics?eventId={Uri.EscapeDataString(eventId)}"));

    /// <summary>Answers a request and waits until the page has rendered again, whether it used or discarded the answer.</summary>
    private static void Answer<T>(IRenderedComponent<EventForensics> cut, TaskCompletionSource<T> request, T result)
    {
        var renders = cut.RenderCount;
        request.SetResult(result);
        cut.WaitForState(() => cut.RenderCount > renders);
    }

    private static List<string> RowEventTexts(IRenderedComponent<EventForensics> cut) =>
        cut.FindAll(".event-context tbody tr").Select(tr => WebUtility.HtmlDecode(tr.Children[3].TextContent).Trim()).ToList();

    [Fact]
    public void LoadedEvent_RequestsItsContext_AndListsItOldestFirst_WithTheEventMarked()
    {
        var cut = RenderEvent("$e");
        Answer(cut, _fetches[0].Result, Loaded("$e"));

        var request = Assert.Single(_contexts);
        Assert.Equal((Room, "$e"), (request.RoomId, request.EventId));
        Answer(cut, request.Result, Context("$e", "$b", "$a"));

        Assert.Equal(["text of $b1", "text of $b2", "text of $e", "text of $a1"], RowEventTexts(cut));
        var shown = Assert.Single(cut.FindAll("tr.event-context-shown"));
        Assert.Contains(L["ThisEvent"].Value, WebUtility.HtmlDecode(shown.TextContent));
        Assert.Empty(shown.QuerySelectorAll("a"));
        var links = cut.FindAll(".event-context a").Select(a => a.GetAttribute("href")).ToList();
        Assert.Equal(new[] { "$b1", "$b2", "$a1" }.Select(id => $"/event-forensics?eventId={Uri.EscapeDataString(id)}"), links);
        Assert.Contains(L["SurroundingEvents"].Value, WebUtility.HtmlDecode(cut.Markup));
        AssertSnackbars();
    }

    [Fact]
    public void Context_ShowsMessageTextAsText_AndMarksRedactedEvents()
    {
        var cut = RenderEvent("$e");
        Answer(cut, _fetches[0].Result, Loaded("$e"));
        Answer(cut, _contexts[0].Result, OperationResult<EventContextViewModel>.Ok(new EventContextViewModel
        {
            Before = [Item("$b1", body: "<b>bold</b>"), Item("$b2", redacted: true)],
            Event = Item("$e"),
        }));

        var rows = cut.FindAll(".event-context tbody tr");
        Assert.Equal("<b>bold</b>", rows[0].Children[3].TextContent.Trim());
        Assert.Empty(rows[0].QuerySelectorAll("b"));
        Assert.Equal(L["Redacted"].Value, WebUtility.HtmlDecode(rows[1].Children[3].TextContent).Trim());
    }

    [Fact]
    public void EventWithoutRoom_RequestsNoContext_AndShowsNoCard()
    {
        var cut = RenderEvent("$e");
        Answer(cut, _fetches[0].Result, Loaded("$e", roomId: null));

        Assert.Empty(_contexts);
        Assert.DoesNotContain(L["SurroundingEvents"].Value, WebUtility.HtmlDecode(cut.Markup));
    }

    [Fact]
    public void WhileTheContextLoads_TheEventAndAProgressBarShow()
    {
        var cut = RenderEvent("$e");
        Answer(cut, _fetches[0].Result, Loaded("$e"));

        Assert.NotNull(GetField<EventForensicsViewModel>(cut.Instance, "eventData"));
        Assert.NotEmpty(cut.FindAll(".mud-progress-linear"));
        Assert.Contains(L["SurroundingEvents"].Value, WebUtility.HtmlDecode(cut.Markup));
    }

    [Theory]
    [InlineData("warning")]
    [InlineData("failure")]
    public void FailedContext_ShowsInTheCard_KeepsTheEvent_AndShowsNoSnackbar(string kind)
    {
        var result = kind == "warning"
            ? OperationResult<EventContextViewModel>.Failure("No context", Severity.Warning)
            : Make<OperationResult<EventContextViewModel>>(kind);
        var cut = RenderEvent("$e");
        Answer(cut, _fetches[0].Result, Loaded("$e"));

        Answer(cut, _contexts[0].Result, result);

        var alert = Assert.Single(cut.FindComponents<MudAlert>());
        Assert.Equal((result.Severity, result.Message), (alert.Instance.Severity, WebUtility.HtmlDecode(alert.Find(".mud-alert-message").TextContent).Trim()));
        Assert.Equal("$e", GetField<EventForensicsViewModel>(cut.Instance, "eventData")?.EventId);
        Assert.Empty(cut.FindAll(".mud-progress-linear"));
        AssertSnackbars();
    }

    [Fact]
    public void CancelledContext_ShowsNothing()
    {
        var cut = RenderEvent("$e");
        Answer(cut, _fetches[0].Result, Loaded("$e"));

        Answer(cut, _contexts[0].Result, OperationResult<EventContextViewModel>.Cancelled());

        Assert.Empty(cut.FindComponents<MudAlert>());
        Assert.Empty(cut.FindAll(".event-context"));
        AssertSnackbars();
    }

    [Fact]
    public void SwitchingEvents_CancelsTheContextRequest_AndALateContextShowsNothing()
    {
        var cut = RenderEvent("$a");
        Answer(cut, _fetches[0].Result, Loaded("$a"));
        var previous = _contexts[0];

        Navigate(cut, "$b");
        Assert.True(previous.Token.IsCancellationRequested);
        Assert.Empty(cut.FindAll(".event-context"));
        Answer(cut, _fetches[1].Result, Loaded("$b"));
        Answer(cut, _contexts[1].Result, Context("$b", "$bb", "$ba"));

        Answer(cut, previous.Result, Context("$a", "$ab", "$aa"));

        Assert.Equal(["text of $bb1", "text of $bb2", "text of $b", "text of $ba1"], RowEventTexts(cut));
        AssertSnackbars();
    }

    /// <summary>A silent answer for the new event's context leaves the card empty, never the previous event's rows.</summary>
    [Fact]
    public void NewEventWithACancelledContext_ShowsNoRowsOfThePreviousEvent()
    {
        var cut = RenderEvent("$a");
        Answer(cut, _fetches[0].Result, Loaded("$a"));
        Answer(cut, _contexts[0].Result, Context("$a", "$ab", "$aa"));
        Assert.NotEmpty(RowEventTexts(cut));

        Navigate(cut, "$b");
        Answer(cut, _fetches[1].Result, Loaded("$b"));
        Answer(cut, _contexts[1].Result, OperationResult<EventContextViewModel>.Cancelled());

        Assert.Empty(RowEventTexts(cut));
        Assert.Empty(cut.FindComponents<MudAlert>());
    }
}
