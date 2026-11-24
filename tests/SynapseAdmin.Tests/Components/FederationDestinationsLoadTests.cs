using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// The federation page loads every destination once and then searches, sorts and pages them locally. It keeps only a
/// complete load: after a failure, which <c>GetAllDestinationsAsync</c> reports for the whole list when any page fails,
/// the next reload asks again (#551).
/// </summary>
public class FederationDestinationsLoadTests : DetailPageTestBase
{
    private readonly Stub _federation;
    private readonly Queue<OperationResult<List<FederationDestinationListViewModel>>> _loads = new();

    public FederationDestinationsLoadTests()
    {
        Services.AddSingleton(Stub.Create<IFederationService>(out _federation));
        _federation.On(nameof(IFederationService.GetAllDestinationsAsync), _ => Task.FromResult(_loads.Dequeue()));
    }

    private static OperationResult<List<FederationDestinationListViewModel>> Loaded(params string[] destinations) =>
        OperationResult<List<FederationDestinationListViewModel>>.Ok(
            destinations.Select(d => new FederationDestinationListViewModel { Destination = d }).ToList());

    private int LoadCount => _federation.CallsTo(nameof(IFederationService.GetAllDestinationsAsync)).Count;

    [Fact]
    public async Task CompleteLoad_IsKept_SearchFiltersWithoutLoadingAgain()
    {
        _loads.Enqueue(Loaded("alpha.org", "beta.org"));
        var cut = Render<FederationDestinations>();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("tbody tr").Count));

        await InvokeHandler(cut, "OnSearchChanged", "beta");

        cut.WaitForAssertion(() => Assert.Contains("beta.org", Assert.Single(cut.FindAll("tbody tr")).TextContent));
        Assert.Equal(1, LoadCount);
        AssertSnackbars();
    }

    [Fact]
    public async Task FailedLoad_ShowsTheFailure_AndTheNextReloadLoadsAgain()
    {
        var failure = Make<OperationResult<List<FederationDestinationListViewModel>>>("failure");
        _loads.Enqueue(failure);
        _loads.Enqueue(Loaded("alpha.org"));
        var cut = Render<FederationDestinations>();
        cut.WaitForAssertion(() => Assert.Equal(1, LoadCount));

        await InvokeHandler(cut, "OnSearchChanged", "");

        cut.WaitForAssertion(() => Assert.Contains("alpha.org", Assert.Single(cut.FindAll("tbody tr")).TextContent));
        Assert.Equal(2, LoadCount);
        AssertSnackbars(failure);
    }
}
