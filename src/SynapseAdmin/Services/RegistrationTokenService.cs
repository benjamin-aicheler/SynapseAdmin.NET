using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Requests;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Services;

public class RegistrationTokenService(MatrixSessionService sessionService, ILogger<RegistrationTokenService> logger)
{
    private AuthenticatedHomeserverSynapse? SynapseAdmin => sessionService.AuthenticatedHomeserver as AuthenticatedHomeserverSynapse;

    public async Task<List<RegistrationTokenViewModel>> GetRegistrationTokensAsync()
    {
        if (SynapseAdmin == null) return [];

        try
        {
            var sdkTokens = await SynapseAdmin.Admin.GetRegistrationTokensAsync();
            
            return sdkTokens.Select(t => new RegistrationTokenViewModel
            {
                Token = t.Token,
                UsesAllowed = t.UsesAllowed,
                Pending = t.Pending,
                Completed = t.Completed,
                ExpiryTime = t.ExpiryTime
            }).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching registration tokens");
            throw;
        }
    }

    public async Task CreateRegistrationTokenAsync(RegistrationTokenViewModel viewModel)
    {
        if (SynapseAdmin == null) return;

        try
        {
            var req = new SynapseAdminRegistrationTokenCreateRequest
            {
                Token = string.IsNullOrWhiteSpace(viewModel.Token) ? null : viewModel.Token,
                UsesAllowed = viewModel.UsesAllowed,
                ExpiryTime = viewModel.ExpiryTime
            };
            var result = await SynapseAdmin.Admin.CreateRegistrationTokenAsync(req);
            logger.LogInformation("Successfully created registration token: {Token}", result.Token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating registration token");
            throw;
        }
    }

    public async Task UpdateRegistrationTokenAsync(string token, RegistrationTokenViewModel viewModel)
    {
        if (SynapseAdmin == null) return;

        try
        {
            var req = new SynapseAdminRegistrationTokenUpdateRequest
            {
                UsesAllowed = viewModel.UsesAllowed,
                ExpiryTime = viewModel.ExpiryTime
            };
            await SynapseAdmin.Admin.UpdateRegistrationTokenAsync(token, req);
            logger.LogInformation("Successfully updated registration token: {Token}", token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating registration token {Token}", token);
            throw;
        }
    }

    public async Task DeleteRegistrationTokenAsync(string token)
    {
        if (SynapseAdmin == null) return;
        try
        {
            await SynapseAdmin.Admin.DeleteRegistrationTokenAsync(token);
            logger.LogInformation("Successfully deleted registration token: {Token}", token);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting registration token {Token}", token);
            throw;
        }
    }
}
