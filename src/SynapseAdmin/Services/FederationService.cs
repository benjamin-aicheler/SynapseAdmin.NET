using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Microsoft.Extensions.Localization;
using SynapseAdmin.Extensions;
using SynapseAdmin.Extensions.Mapping;

namespace SynapseAdmin.Services;

public class FederationService(IMatrixSessionService sessionService, ILogger<FederationService> logger, IStringLocalizer<SharedResources> L) : IFederationService
{
    /// <summary>Destinations per request. Synapse sets no maximum, so few requests load even a large list.</summary>
    private const int DestinationPageLimit = 10000;

    /// <summary>The order is irrelevant (the page sorts locally), but one order keeps the pages consistent.</summary>
    private const string DestinationPageDir = "f";

    public Task<OperationResult<List<FederationDestinationListViewModel>>> GetAllDestinationsAsync(CancellationToken token = default)
    {
        string? from = null;
        return sessionService.RunGatewayCallAsync(L, "ErrorFetchingFederationDestinations",
            ex => logger.LogError(ex, "Error fetching federation destinations (from: {From})", from?.SanitizeForLogging()),
            async gateway =>
            {
                var destinations = new List<FederationDestinationListViewModel>();
                var seenTokens = new HashSet<string>(StringComparer.Ordinal);
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    var page = await gateway.GetFederationDestinationListAsync(from, DestinationPageLimit, DestinationPageDir, token);
                    if (page == null) break;

                    destinations.AddRange(page.Destinations.ToViewModels());

                    // next_token is opaque: no total or page size says when the list ends, only its absence does.
                    if (string.IsNullOrEmpty(page.NextToken)) break;
                    if (!seenTokens.Add(page.NextToken))
                    {
                        logger.LogWarning("The homeserver repeated the federation destinations next_token {NextToken}", page.NextToken.SanitizeForLogging());
                        return OperationResult<List<FederationDestinationListViewModel>>.Failure(L["ErrorFetchingFederationDestinations"]);
                    }

                    from = page.NextToken;
                }

                return OperationResult<List<FederationDestinationListViewModel>>.Ok(destinations);
            });
    }

    public Task<OperationResult> ResetConnectionTimeoutAsync(string destination, CancellationToken token = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorResettingFederationConnection",
            ex => logger.LogError(ex, "Error resetting federation connection timeout for {Destination}", destination.SanitizeForLogging()),
            async gateway =>
            {
                await gateway.ResetFederationConnectionTimeoutAsync(destination, token);
                logger.LogInformation("Successfully reset federation connection timeout for {Destination}", destination.SanitizeForLogging());
                return OperationResult.Ok(L["ResetFederationConnectionSuccessful"]);
            });
}
