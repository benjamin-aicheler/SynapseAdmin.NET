using System.Reflection;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Extensions;
using SynapseAdmin.Resources;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>Issue #371: the user media tab pages server-side and separates page statistics from totals.</summary>
public class UserDetailsMediaTabTests : DetailPageTestBase
{
    private const string UserA = "@alice:hs";
    private const string UserB = "@bob:hs";
    private const int UploadCount = 150;

    private readonly Stub _users;
    private TaskCompletionSource<OperationResult<UserMediaPageViewModel>>? _pendingPage;

    public UserDetailsMediaTabTests()
    {
        Services.AddSingleton(Stub.Create<IUserService>(out _users));
        _users
            .On(nameof(IUserService.GetUserDetailsAsync), args => Task.FromResult(
                OperationResult<UserDetailViewModel>.Ok(new UserDetailViewModel { UserId = (string)args[0]!, DisplayName = "Alice Example" })))
            .On(nameof(IUserService.GetUserMediaPageAsync), args =>
                _pendingPage?.Task ?? Task.FromResult(OperationResult<UserMediaPageViewModel>.Ok(Page((int)args[1]!, (int)args[2]!))))
            .On(nameof(IUserService.GetUserMediaTotalsAsync), _ => Task.FromResult(
                OperationResult<UserMediaTotalsViewModel>.Ok(new UserMediaTotalsViewModel { Count = UploadCount, Bytes = 1_536_000 })));
    }

    private static UserMediaPageViewModel Page(int offset, int limit) => new()
    {
        Offset = offset,
        TotalCount = UploadCount,
        Items = Enumerable.Range(offset, Math.Max(0, Math.Min(limit, UploadCount - offset)))
            .Select(i => new UserMediaItemViewModel { MediaId = $"upload{UploadCount - i:D3}", MediaLength = 1024 })
            .ToList()
    };

    private IReadOnlyList<object?[]> PageCalls => _users.CallsTo(nameof(IUserService.GetUserMediaPageAsync));

    private IRenderedComponent<UserDetails> RenderUser(string userId)
    {
        var cut = Render<UserDetails>(p => p.Add(x => x.UserId, userId));
        cut.WaitForAssertion(() => Assert.Contains("Alice Example", cut.Markup));
        return cut;
    }

    private static void OpenMediaTab(IRenderedComponent<UserDetails> cut) =>
        cut.FindAll(".mud-tab").First(t => t.TextContent.Trim() == "Media").Click();

    private static Task<TableData<UserMediaItemViewModel>> LoadPage(IRenderedComponent<UserDetails> cut, int page, int pageSize)
    {
        var method = typeof(UserDetails).GetMethod("LoadMediaPage", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var state = new TableState { Page = page, PageSize = pageSize };
        return cut.InvokeAsync(() => (Task<TableData<UserMediaItemViewModel>>)method.Invoke(cut.Instance, [state, CancellationToken.None])!);
    }

    [Fact]
    public void MediaTab_LoadsFirstPageFromServer_AndShowsTotalsSeparatelyFromPage()
    {
        var cut = RenderUser(UserA);
        Assert.Empty(PageCalls); // media is loaded lazily by the table, not with the user details

        OpenMediaTab(cut);

        cut.WaitForAssertion(() => Assert.Contains("upload150", cut.Markup));
        var call = Assert.Single(PageCalls);
        Assert.Equal(UserA, call[0]);
        Assert.Equal(0, call[1]);
        Assert.Equal(10, call[2]); // MudTable default page size

        // Totals across all uploads and the current page's statistics are shown as separate lines
        // (expected text built with the app's localizer, so the test does not depend on the machine's culture).
        var L = Services.GetRequiredService<IStringLocalizer<SharedResources>>();
        string Html(string text) => System.Net.WebUtility.HtmlEncode(text);
        Assert.Contains(Html(L["UserMediaTotals", UploadCount, 1_536_000L.FormatSize()]), cut.Markup);
        Assert.Contains(Html(L["UserMediaPageSummary", 1, 10, (10 * 1024L).FormatSize()]), cut.Markup);
        Assert.Single(_users.CallsTo(nameof(IUserService.GetUserMediaTotalsAsync)));
    }

    [Fact]
    public async Task LaterPages_RequestTheirOffset_AndReachUploadsBeyond100()
    {
        var cut = RenderUser(UserA);

        var data = await LoadPage(cut, page: 10, pageSize: 10);

        Assert.Equal(UploadCount, data.TotalItems);
        Assert.Equal("upload050", data.Items!.First().MediaId);
        Assert.Equal(100, PageCalls[^1][1]);
        Assert.Equal(10, PageCalls[^1][2]);
    }

    [Fact]
    public async Task PageResponse_ForUserNoLongerDisplayed_IsDiscarded()
    {
        var cut = RenderUser(UserA);
        _pendingPage = Pending<OperationResult<UserMediaPageViewModel>>();
        var load = LoadPage(cut, page: 0, pageSize: 10);

        cut.Render(p => p.Add(x => x.UserId, UserB));
        cut.WaitForAssertion(() => Assert.Equal(UserB, GetField<string>(cut.Instance, "_displayedUserId")));

        _pendingPage.SetResult(OperationResult<UserMediaPageViewModel>.Ok(Page(0, 10)));
        var data = await load;

        Assert.Equal(0, data.TotalItems);
        Assert.Empty(data.Items!);
        Assert.Null(GetField<UserMediaPageViewModel>(cut.Instance, "_mediaPage"));
    }
}
