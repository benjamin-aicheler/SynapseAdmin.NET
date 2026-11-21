using System.Net;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// Issue #521: Blazor reuses the event forensics page when only the <c>eventId</c> query changes. A new event cancels
/// the previous event's request and is requested at once, a late answer for the previous event changes nothing (no
/// details, no snackbar), and removing the query clears the result.
/// </summary>
public class EventForensicsStaleSelectionTests : DetailPageTestBase
{
    private const string EventA = "$a";
    private const string EventB = "$b";

    /// <summary>Every event request, in call order. Each one stays pending until the test answers it.</summary>
    private readonly List<(string EventId, CancellationToken Token, TaskCompletionSource<OperationResult<EventForensicsViewModel>> Result)> _fetches = [];

    public EventForensicsStaleSelectionTests()
    {
        Services.AddSingleton(Stub.Create<IEventForensicsService>(s => s
            .On(nameof(IEventForensicsService.FetchEventAsync), args =>
            {
                var fetch = Pending<OperationResult<EventForensicsViewModel>>();
                _fetches.Add(((string)args[0]!, (CancellationToken)args[1]!, fetch));
                return fetch.Task;
            })));
    }

    private static OperationResult<EventForensicsViewModel> Loaded(string eventId) =>
        OperationResult<EventForensicsViewModel>.Ok(new EventForensicsViewModel
        {
            EventId = eventId,
            Type = TypeOf(eventId),
            RawJson = "{}"
        });

    /// <summary>A distinct event type per event, so the markup shows which event is displayed.</summary>
    private static string TypeOf(string eventId) => $"m.forensics.{eventId.TrimStart('$')}";

    private static string Uri(string? eventId) =>
        eventId == null ? "/forensics" : $"/forensics?eventId={System.Uri.EscapeDataString(eventId)}";

    /// <summary>The page reads <c>eventId</c> from the query, so it is selected by navigating, as in the app.</summary>
    private IRenderedComponent<EventForensics> RenderEvent(string? eventId)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(Uri(eventId));
        return Render<EventForensics>();
    }

    private void Navigate(IRenderedComponent<EventForensics> cut, string? eventId) =>
        cut.InvokeAsync(() => Services.GetRequiredService<NavigationManager>().NavigateTo(Uri(eventId)));

    /// <summary>Answers a request and waits until the page has handled the answer, whether it used or discarded it.</summary>
    private static void Answer(
        IRenderedComponent<EventForensics> cut,
        TaskCompletionSource<OperationResult<EventForensicsViewModel>> fetch,
        OperationResult<EventForensicsViewModel> result)
    {
        var renders = cut.RenderCount;
        fetch.SetResult(result);
        cut.WaitForState(() => cut.RenderCount > renders);
    }

    private static string? DisplayedEventId(IRenderedComponent<EventForensics> cut) =>
        GetField<EventForensicsViewModel>(cut.Instance, "eventData")?.EventId;

    private static void AssertDisplays(IRenderedComponent<EventForensics> cut, string? eventId)
    {
        Assert.Equal(eventId, DisplayedEventId(cut));
        var markup = WebUtility.HtmlDecode(cut.Markup);
        foreach (var other in new[] { EventA, EventB }.Where(e => e != eventId))
        {
            Assert.DoesNotContain(TypeOf(other), markup);
        }
        if (eventId != null) Assert.Contains(TypeOf(eventId), markup);
    }

    [Fact]
    public void SwitchingEvents_RequestsTheNewEventAndCancelsThePrevious()
    {
        var cut = RenderEvent(EventA);
        Navigate(cut, EventB);

        Assert.Equal([EventA, EventB], _fetches.Select(f => f.EventId));
        Assert.True(_fetches[0].Token.IsCancellationRequested);
        Assert.False(_fetches[1].Token.IsCancellationRequested);
        Assert.Equal(EventB, GetField<string>(cut.Instance, "eventId"));
    }

    /// <summary>A answers after B was selected: while B is still loading, or after B is shown.</summary>
    [Theory]
    [InlineData("success", false)]
    [InlineData("success", true)]
    [InlineData("failure", false)]
    [InlineData("failure", true)]
    public void LateAnswerForThePreviousEvent_ChangesNothing(string previousResult, bool currentAnswersFirst)
    {
        var cut = RenderEvent(EventA);
        Navigate(cut, EventB);
        var (previous, current) = (_fetches[0].Result, _fetches[1].Result);
        var lateAnswer = previousResult == "success" ? Loaded(EventA) : Make<OperationResult<EventForensicsViewModel>>(previousResult);

        if (currentAnswersFirst)
        {
            Answer(cut, current, Loaded(EventB));
            Answer(cut, previous, lateAnswer);
        }
        else
        {
            Answer(cut, previous, lateAnswer);
            AssertDisplays(cut, null);
            Answer(cut, current, Loaded(EventB));
        }

        AssertDisplays(cut, EventB);
        AssertSnackbars([]);
    }

    [Fact]
    public void RemovingTheQuery_ClearsTheShownEvent()
    {
        var cut = RenderEvent(EventA);
        Answer(cut, _fetches[0].Result, Loaded(EventA));
        AssertDisplays(cut, EventA);

        Navigate(cut, null);

        AssertDisplays(cut, null);
        Assert.Equal(string.Empty, GetField<string>(cut.Instance, "eventId"));
        Assert.False(GetField<bool>(cut.Instance, "isLoading"));
    }

    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    public void RemovingTheQuery_WhileLoading_CancelsAndIgnoresTheLateAnswer(string lateResult)
    {
        var cut = RenderEvent(EventA);
        Navigate(cut, null);

        Assert.True(_fetches[0].Token.IsCancellationRequested);
        Assert.False(GetField<bool>(cut.Instance, "isLoading"));
        Answer(cut, _fetches[0].Result, lateResult == "success" ? Loaded(EventA) : Make<OperationResult<EventForensicsViewModel>>(lateResult));

        AssertDisplays(cut, null);
        AssertSnackbars([]);
    }

    [Fact]
    public async Task ManuallyFetchedEvent_StaysWhenThePageIsRenderedAgainWithoutAQuery()
    {
        var cut = RenderEvent(null);
        cut.Find("input").Input(EventB);
        var fetch = InvokeHandler(cut, "FetchEvent");
        _fetches[0].Result.SetResult(Loaded(EventB));
        await fetch;

        cut.Render(); // parameters set again, still without a query

        Assert.Equal([EventB], _fetches.Select(f => f.EventId));
        AssertDisplays(cut, EventB);
    }

    [Fact]
    public void Failure_ForTheCurrentEvent_IsReported()
    {
        var cut = RenderEvent(EventA);
        var failure = Make<OperationResult<EventForensicsViewModel>>("failure");

        Answer(cut, _fetches[0].Result, failure);

        AssertDisplays(cut, null);
        AssertSnackbars([failure]);
    }
}
