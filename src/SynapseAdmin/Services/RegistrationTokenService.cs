using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Microsoft.Extensions.Localization;
using SynapseAdmin.Extensions.Mapping;
using SynapseAdmin.Interfaces.Gateways;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;

namespace SynapseAdmin.Services;

public class RegistrationTokenService(IMatrixSessionService sessionService, ILogger<RegistrationTokenService> logger, IStringLocalizer<SharedResources> L) : IRegistrationTokenService
{
    private IMatrixGateway? Gateway => sessionService.Gateway;

    public async Task<OperationResult<List<RegistrationTokenViewModel>>> GetRegistrationTokensAsync()
    {
        if (Gateway == null) return OperationResult<List<RegistrationTokenViewModel>>.Failure(L["NotAuthenticated"]);

        try
        {
            var sdkTokens = await Gateway.GetRegistrationTokensAsync();
            
            var vms = sdkTokens.ToViewModels();

            return OperationResult<List<RegistrationTokenViewModel>>.Ok(vms);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching registration tokens");
            return OperationResult<List<RegistrationTokenViewModel>>.Failure(L["ErrorLoadingTokens"]);
        }
    }

    public async Task<OperationResult> CreateRegistrationTokenAsync(RegistrationTokenViewModel viewModel)
    {
        if (Gateway == null) return OperationResult.Failure(L["NotAuthenticated"]);

        try
        {
            var req = new SynapseAdminRegistrationTokenCreateRequest
            {
                Token = string.IsNullOrWhiteSpace(viewModel.Token) ? null : viewModel.Token,
                UsesAllowed = viewModel.UsesAllowed,
                ExpiryTime = viewModel.ExpiryTime
            };
            
            var result = await Gateway.CreateRegistrationTokenAsync(req);
            
            if (result == null) return OperationResult.Failure(L["ErrorCreatingToken"]);

            logger.LogInformation("Successfully created registration token");
            return OperationResult.Ok(L["TokenCreatedSuccessfully"]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating registration token");
            return OperationResult.Failure(L["ErrorCreatingToken"]);
        }
    }

    public async Task<OperationResult> UpdateRegistrationTokenAsync(string token, RegistrationTokenViewModel viewModel)
    {
        if (Gateway == null) return OperationResult.Failure(L["NotAuthenticated"]);

        try
        {
            var req = new SynapseAdminRegistrationTokenUpdateRequest
            {
                UsesAllowed = viewModel.UsesAllowed,
                ExpiryTime = viewModel.ExpiryTime
            };
            await Gateway.UpdateRegistrationTokenAsync(token, req);
            logger.LogInformation("Successfully updated registration token");
            return OperationResult.Ok(L["TokenUpdatedSuccessfully"]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating registration token");
            return OperationResult.Failure(L["ErrorUpdatingToken"]);
        }
    }

    public async Task<OperationResult> DeleteRegistrationTokenAsync(string token)
    {
        if (Gateway == null) return OperationResult.Failure(L["NotAuthenticated"]);
        try
        {
            await Gateway.DeleteRegistrationTokenAsync(token);
            logger.LogInformation("Successfully deleted registration token");
            return OperationResult.Ok(L["TokenDeletedSuccessfully"]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting registration token");
            return OperationResult.Failure(L["ErrorDeletingToken"]);
        }
    }
}
