using LibMatrix.Homeservers;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Requests;

namespace SynapseAdmin.Services;

public class RegistrationTokenService(MatrixSessionService sessionService)
{
    private AuthenticatedHomeserverSynapse? SynapseAdmin => sessionService.AuthenticatedHomeserver as AuthenticatedHomeserverSynapse;

    public async Task<List<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken>> GetRegistrationTokensAsync()
    {
        if (SynapseAdmin == null) return [];
        return await SynapseAdmin.Admin.GetRegistrationTokensAsync();
    }

    public async Task CreateRegistrationTokenAsync(SynapseAdminRegistrationTokenCreateRequest request)
    {
        if (SynapseAdmin == null) return;
        await SynapseAdmin.Admin.CreateRegistrationTokenAsync(request);
    }

    public async Task UpdateRegistrationTokenAsync(string token, SynapseAdminRegistrationTokenUpdateRequest request)
    {
        if (SynapseAdmin == null) return;
        await SynapseAdmin.Admin.UpdateRegistrationTokenAsync(token, request);
    }

    public async Task DeleteRegistrationTokenAsync(string token)
    {
        if (SynapseAdmin == null) return;
        await SynapseAdmin.Admin.DeleteRegistrationTokenAsync(token);
    }
}
