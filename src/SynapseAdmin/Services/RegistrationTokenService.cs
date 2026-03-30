using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Requests;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Services;

public class RegistrationTokenService(MatrixSessionService sessionService)
{
    private AuthenticatedHomeserverSynapse? SynapseAdmin => sessionService.AuthenticatedHomeserver as AuthenticatedHomeserverSynapse;

    public async Task<List<RegistrationTokenViewModel>> GetRegistrationTokensAsync()
    {
        if (SynapseAdmin == null) return [];
        var sdkTokens = await SynapseAdmin.Admin.GetRegistrationTokensAsync();
        
        return sdkTokens.Select(t => new RegistrationTokenViewModel
        {
            Token = t.Token,
            UsesAllowed = t.UsesAllowed,
            Completed = t.Completed,
            Pending = t.Pending,
            ExpiryTime = t.ExpiryTime
        }).ToList();
    }

    public async Task CreateRegistrationTokenAsync(RegistrationTokenViewModel viewModel)
    {
        if (SynapseAdmin == null) return;
        
        var request = new SynapseAdminRegistrationTokenCreateRequest
        {
            Token = string.IsNullOrWhiteSpace(viewModel.Token) ? null : viewModel.Token,
            UsesAllowed = viewModel.UsesAllowed,
            ExpiryTime = viewModel.ExpiryTime
        };
        await SynapseAdmin.Admin.CreateRegistrationTokenAsync(request);
    }

    public async Task UpdateRegistrationTokenAsync(string token, RegistrationTokenViewModel viewModel)
    {
        if (SynapseAdmin == null) return;

        var request = new SynapseAdminRegistrationTokenUpdateRequest
        {
            UsesAllowed = viewModel.UsesAllowed,
            ExpiryTime = viewModel.ExpiryTime
        };
        await SynapseAdmin.Admin.UpdateRegistrationTokenAsync(token, request);
    }

    public async Task DeleteRegistrationTokenAsync(string token)
    {
        if (SynapseAdmin == null) return;
        await SynapseAdmin.Admin.DeleteRegistrationTokenAsync(token);
    }
}
