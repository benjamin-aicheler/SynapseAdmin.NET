using System.Net;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// A destination is failing while <c>retry_last_ts</c> is not <c>0</c>: Synapse sets it to <c>0</c> after a successful
/// attempt and rejects <c>reset_connection</c> unless it is set. <c>failure_ts</c> only says when the failures began, so
/// the status and the Reset Connection button never depend on it (#544).
/// </summary>
public class FederationDestinationStatusTests : DetailPageTestBase
{
    private readonly Stub _federation;

    public FederationDestinationStatusTests()
    {
        Services.AddSingleton(Stub.Create<IFederationService>(out _federation));
        _federation.On(nameof(IFederationService.GetDestinationsAsync), _ =>
            Task.FromResult(OperationResult<(int, List<FederationDestinationListViewModel>)>.Ok((4,
            [
                new FederationDestinationListViewModel { Destination = "a-never-failed.org", RetryLastTs = 0, FailureTs = null },
                new FederationDestinationListViewModel { Destination = "b-recovered.org", RetryLastTs = 0, FailureTs = 1_000 },
                new FederationDestinationListViewModel { Destination = "c-failing.org", RetryLastTs = 2_000, FailureTs = 1_000 },
                new FederationDestinationListViewModel { Destination = "d-retrying.org", RetryLastTs = 2_000, FailureTs = null },
            ]))));
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    [Theory]
    [InlineData("a-never-failed.org", false)]
    [InlineData("b-recovered.org", false)]
    [InlineData("c-failing.org", true)]
    [InlineData("d-retrying.org", true)]
    public void StatusAndReset_FollowRetryLastTs(string destination, bool failing)
    {
        var cut = Render<FederationDestinations>();

        cut.WaitForAssertion(() =>
        {
            var row = cut.FindAll("tbody tr").Single(tr => tr.TextContent.Contains(destination));
            var status = WebUtility.HtmlDecode(row.QuerySelector(".mud-chip")!.TextContent).Trim();
            Assert.Equal(L[failing ? "Failing" : "OK"].Value, status);
            Assert.Equal(!failing, row.QuerySelector("button")!.HasAttribute("disabled"));
        });
    }

    [Theory]
    [InlineData(0L, null, false)]
    [InlineData(0L, 1_000L, false)]
    [InlineData(2_000L, 1_000L, true)]
    [InlineData(2_000L, null, true)]
    public void IsFailing_IsRetryLastTsNotZero(long retryLastTs, long? failureTs, bool expected) =>
        Assert.Equal(expected, new FederationDestinationListViewModel { RetryLastTs = retryLastTs, FailureTs = failureTs }.IsFailing);
}
