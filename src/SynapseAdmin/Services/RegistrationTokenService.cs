using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Microsoft.Extensions.Localization;
using System.Net.Http.Json;
using SynapseAdmin.Extensions;

namespace SynapseAdmin.Services;

public class RegistrationTokenService(IMatrixSessionService sessionService, ILogger<RegistrationTokenService> logger, IStringLocalizer<SharedResources> L) : IRegistrationTokenService
{
    private AuthenticatedHomeserverSynapse? SynapseAdmin => sessionService.AuthenticatedHomeserver as AuthenticatedHomeserverSynapse;

    public async Task<OperationResult<List<RegistrationTokenViewModel>>> GetRegistrationTokensAsync()
    {
        if (SynapseAdmin == null) return OperationResult<List<RegistrationTokenViewModel>>.Failure(L["NotAuthenticated"]);

        try
        {
            var sdkTokens = await SynapseAdmin.Admin.GetRegistrationTokensAsync();
            
            var vms = sdkTokens.Select(t => new RegistrationTokenViewModel
            {
                Token = t.Token,
                UsesAllowed = t.UsesAllowed,
                Pending = t.Pending,
                Completed = t.Completed,
                ExpiryTime = t.ExpiryTime
            }).ToList();

            return OperationResult<List<RegistrationTokenViewModel>>.Ok(vms);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching registration tokens");
            return OperationResult<List<RegistrationTokenViewModel>>.Failure(string.Format(L["ErrorLoadingTokens"], ex.Message));
        }
    }

    public async Task<OperationResult> CreateRegistrationTokenAsync(RegistrationTokenViewModel viewModel)
    {
        if (SynapseAdmin == null) return OperationResult.Failure(L["NotAuthenticated"]);

        try
        {
            var req = new SynapseAdminRegistrationTokenCreateRequest
            {
                Token = string.IsNullOrWhiteSpace(viewModel.Token) ? null : viewModel.Token,
                UsesAllowed = viewModel.UsesAllowed,
                ExpiryTime = viewModel.ExpiryTime
            };
            
            var resp = await SynapseAdmin.ClientHttpClient.PostAsJsonAsync("/_synapse/admin/v1/registration_tokens", req);
            resp.EnsureSuccessStatusCode();
            var result = await resp.Content.ReadFromJsonAsync<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken>();
            
            if (result == null) return OperationResult.Failure(L["ErrorCreatingToken"]);

            logger.LogInformation("Successfully created registration token");
            return OperationResult.Ok(L["TokenCreatedSuccessfully"]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating registration token");
            return OperationResult.Failure(string.Format(L["ErrorCreatingToken"], ex.Message));
        }
    }

    public async Task<OperationResult> UpdateRegistrationTokenAsync(string token, RegistrationTokenViewModel viewModel)
    {
        if (SynapseAdmin == null) return OperationResult.Failure(L["NotAuthenticated"]);

        try
        {
            var req = new SynapseAdminRegistrationTokenUpdateRequest
            {
                UsesAllowed = viewModel.UsesAllowed,
                ExpiryTime = viewModel.ExpiryTime
            };
            await SynapseAdmin.Admin.UpdateRegistrationTokenAsync(token, req);
            logger.LogInformation("Successfully updated registration token");
            return OperationResult.Ok(L["TokenUpdatedSuccessfully"]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating registration token");
            return OperationResult.Failure(string.Format(L["ErrorUpdatingToken"], ex.Message));
        }
    }

    public async Task<OperationResult> DeleteRegistrationTokenAsync(string token)
    {
        if (SynapseAdmin == null) return OperationResult.Failure(L["NotAuthenticated"]);
        try
        {
            await SynapseAdmin.Admin.DeleteRegistrationTokenAsync(token);
            logger.LogInformation("Successfully deleted registration token");
            return OperationResult.Ok(L["TokenDeletedSuccessfully"]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting registration token");
            return OperationResult.Failure(string.Format(L["ErrorDeletingToken"], ex.Message));
        }
    }
}
