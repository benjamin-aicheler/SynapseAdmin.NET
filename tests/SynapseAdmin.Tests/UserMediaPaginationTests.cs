using System.Text.Json;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using SynapseAdmin.Infrastructure.Gateways;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Resources;
using SynapseAdmin.Services;
using SynapseAdmin.Tests.Components;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>Issue #371: a user's uploads must be reachable beyond Synapse's default first page of 100.</summary>
public class UserMediaPaginationTests
{
    private const string UserId = "@bob:example.com";
    private const int UploadCount = 150;

    private sealed class KeyLocalizer : IStringLocalizer<SharedResources>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, name);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    /// <summary>Exposes the gateway's protected JSON options and internal URL builders.</summary>
    private abstract class GatewayProbe : SynapseCompatibleAdminGateway
    {
        protected GatewayProbe() : base(null!) { }
        public static JsonSerializerOptions Options => SynapseCompatibilityJsonOptions;
    }

    private readonly Stub _gateway;
    private readonly UserService _service;
    private UserMediaStatisticsResponse? _statistics;

    public UserMediaPaginationTests()
    {
        var gateway = Stub.Create<IMatrixGateway>(out _gateway);
        _gateway
            .On("get_ServerName", _ => "example.com")
            .On(nameof(IMatrixGateway.GetUserMediaAsync), args => Task.FromResult<SynapseAdminUserMediaResult?>(
                ServeUploads((int)args[1]!, (int)args[2]!)))
            .On(nameof(IMatrixGateway.GetUserMediaStatisticsAsync), _ => Task.FromResult(_statistics));

        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));
        _service = new UserService(session, NullLogger<UserService>.Instance, new KeyLocalizer());
    }

    /// <summary>Simulates Synapse: newest first, <c>next_token</c> = from + returned items while more remain.</summary>
    private static SynapseAdminUserMediaResult ServeUploads(int from, int limit)
    {
        var items = Enumerable.Range(from, Math.Max(0, Math.Min(limit, UploadCount - from)))
            .Select(i => new SynapseAdminUserMediaResult.MediaInfo
            {
                MediaId = $"upload{UploadCount - i:D3}", // upload150 (newest) ... upload001 (oldest)
                MediaLength = 10,
                CreatedTimestamp = 1_700_000_000_000 - i
            })
            .ToList();
        var next = from + items.Count;
        return new SynapseAdminUserMediaResult
        {
            Total = UploadCount,
            Media = items,
            NextToken = next < UploadCount ? next.ToString() : null
        };
    }

    private IReadOnlyList<object?[]> MediaCalls => _gateway.CallsTo(nameof(IMatrixGateway.GetUserMediaAsync));

    [Fact]
    public async Task UploadsBeyondTheFirst100_AreReachableOnTheNextPage()
    {
        var first = await _service.GetUserMediaPageAsync(UserId, 0, 100);
        Assert.True(first.Success);
        Assert.Equal(100, first.Data!.Items.Count);
        Assert.Equal(UploadCount, first.Data.TotalCount);
        Assert.Equal("upload150", first.Data.Items[0].MediaId); // newest first

        // The next page's offset is exactly the continuation token Synapse returned for page 1.
        var nextToken = ServeUploads(0, 100).NextToken;
        Assert.Equal("100", nextToken);

        var second = await _service.GetUserMediaPageAsync(UserId, int.Parse(nextToken!), 100);
        Assert.True(second.Success);
        Assert.Equal(50, second.Data!.Items.Count);
        Assert.Equal("upload050", second.Data.Items[0].MediaId);
        Assert.Equal("upload001", second.Data.Items[^1].MediaId);
        Assert.Equal(100, second.Data.Offset);

        Assert.Equal([0, 100], MediaCalls.Select(c => (int)c[1]!));
        Assert.All(MediaCalls, c =>
        {
            Assert.Equal(UserId, c[0]);
            Assert.Equal("created_ts", c[3]);
            Assert.Equal("b", c[4]);
        });
    }

    [Fact]
    public async Task PageJump_RequestsTheMatchingOffsetDirectly()
    {
        var page = await _service.GetUserMediaPageAsync(UserId, 4 * 25, 25);

        Assert.Equal(25, page.Data!.Items.Count);
        Assert.Equal("upload050", page.Data.Items[0].MediaId);
        var call = Assert.Single(MediaCalls);
        Assert.Equal(100, call[1]);
        Assert.Equal(25, call[2]);
    }

    [Fact]
    public async Task PageRequest_IsClampedToValidOffsetAndLimit()
    {
        await _service.GetUserMediaPageAsync(UserId, -5, 10_000);

        var call = Assert.Single(MediaCalls);
        Assert.Equal(0, call[1]);
        Assert.Equal(100, call[2]);
    }

    [Fact]
    public async Task PageStatistics_DescribeThePageOnly_AndTotalsComeFromTheStatisticsEndpoint()
    {
        _statistics = new UserMediaStatisticsResponse
        {
            Total = 2,
            Users =
            [
                // search_term is a substring match: "@bob:example.com" also matches this user.
                new() { UserId = "@bob:example.com.evil", MediaCount = 3, MediaLength = 999 },
                new() { UserId = UserId, MediaCount = UploadCount, MediaLength = 1_500 }
            ]
        };

        var page = await _service.GetUserMediaPageAsync(UserId, 0, 100);
        var totals = await _service.GetUserMediaTotalsAsync(UserId);

        Assert.Equal(1_000, page.Data!.PageBytes);   // 100 items x 10 bytes on this page
        Assert.True(totals.Success);
        Assert.Equal(UploadCount, totals.Data!.Count);
        Assert.Equal(1_500, totals.Data.Bytes);     // all 150 uploads, from the exact user match

        var statsCall = Assert.Single(_gateway.CallsTo(nameof(IMatrixGateway.GetUserMediaStatisticsAsync)));
        Assert.Equal(UserId, statsCall[3]);
    }

    [Fact]
    public async Task Totals_ForUserWithoutUploads_AreZero_AndUnknownWhenTheMatchMayBeCutOff()
    {
        _statistics = new UserMediaStatisticsResponse { Total = 1, Users = [new() { UserId = "@bobby:example.com", MediaCount = 1, MediaLength = 5 }] };
        var none = await _service.GetUserMediaTotalsAsync(UserId);
        Assert.True(none.Success);
        Assert.Equal(0, none.Data!.Count);
        Assert.Equal(0, none.Data.Bytes);

        _statistics = new UserMediaStatisticsResponse { Total = 500, Users = [new() { UserId = "@bobby:example.com" }] };
        var unknown = await _service.GetUserMediaTotalsAsync(UserId);
        Assert.False(unknown.Success);
    }

    [Fact]
    public async Task UserDetails_NoLongerPreloadsMedia()
    {
        _gateway.On(nameof(IMatrixGateway.GetUserDetailsAsync), _ => Task.FromResult<SynapseAdminUserListResult.SynapseAdminUserListResultUser?>(
            new() { Name = UserId }));

        var details = await _service.GetUserDetailsAsync(UserId);

        Assert.True(details.Success);
        Assert.Empty(MediaCalls);
    }

    [Fact]
    public void NumericContinuationToken_DeserializesIntoTheStringModel()
    {
        const string json = """{"media":[{"media_id":"abc","media_length":10,"created_ts":1}],"next_token":101,"total":150}""";

        var result = JsonSerializer.Deserialize<SynapseAdminUserMediaResult>(json, GatewayProbe.Options)!;

        Assert.Equal("101", result.NextToken);
        Assert.Equal(150, result.Total);
        Assert.Single(result.Media);
    }

    [Fact]
    public void Urls_CarryPagingSortingAndSearchParameters()
    {
        Assert.Equal(
            "/_synapse/admin/v1/users/%40bob%3aexample.com/media?from=100&limit=25&order_by=created_ts&dir=b",
            SynapseCompatibleAdminGateway.BuildUserMediaUrl(UserId, 100, 25, "created_ts", "b"));

        Assert.Equal(
            "/_synapse/admin/v1/statistics/users/media?limit=100&order_by=media_length&dir=b&search_term=%40bob%3aexample.com",
            SynapseCompatibleAdminGateway.BuildUserMediaStatisticsUrl(100, "media_length", "b", UserId));

        Assert.Equal(
            "/_synapse/admin/v1/statistics/users/media?limit=10&order_by=media_length&dir=b",
            SynapseCompatibleAdminGateway.BuildUserMediaStatisticsUrl(10, "media_length", "b", null));
    }
}
