using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using Microsoft.Extensions.Localization;
using SynapseAdmin.Extensions;
using SynapseAdmin.Extensions.Mapping;
using SynapseAdmin.Models.Requests;

namespace SynapseAdmin.Services;

public class RegistrationTokenService(IMatrixSessionService sessionService, ILogger<RegistrationTokenService> logger, IStringLocalizer<SharedResources> L) : IRegistrationTokenService
{
    public Task<OperationResult<List<RegistrationTokenViewModel>>> GetRegistrationTokensAsync(CancellationToken cancellationToken = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorLoadingTokens",
            ex => logger.LogError(ex, "Error fetching registration tokens"),
            async gateway =>
            {
                var sdkTokens = await gateway.GetRegistrationTokensAsync(cancellationToken);

                var vms = sdkTokens.ToViewModels();

                return OperationResult<List<RegistrationTokenViewModel>>.Ok(vms);
            });

    public Task<OperationResult> CreateRegistrationTokenAsync(RegistrationTokenViewModel viewModel, CancellationToken cancellationToken = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorCreatingToken",
            ex => logger.LogError(ex, "Error creating registration token"),
            async gateway =>
            {
                var req = new SynapseAdminRegistrationTokenCreateRequest
                {
                    Token = string.IsNullOrWhiteSpace(viewModel.Token) ? null : viewModel.Token,
                    UsesAllowed = viewModel.UsesAllowed,
                    ExpiryTime = viewModel.ExpiryTime
                };

                var result = await gateway.CreateRegistrationTokenAsync(req, cancellationToken);

                if (result == null) return OperationResult.Failure(L["ErrorCreatingToken"]);

                logger.LogInformation("Successfully created registration token");
                return OperationResult.Ok(L["TokenCreatedSuccessfully"]);
            });

    public Task<OperationResult> UpdateRegistrationTokenAsync(string token, RegistrationTokenViewModel viewModel, CancellationToken cancellationToken = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorUpdatingToken",
            ex => logger.LogError(ex, "Error updating registration token"),
            async gateway =>
            {
                var req = new SynapseAdminRegistrationTokenUpdateRequest
                {
                    UsesAllowed = viewModel.UsesAllowed,
                    ExpiryTime = viewModel.ExpiryTime
                };
                await gateway.UpdateRegistrationTokenAsync(token, req, cancellationToken: cancellationToken);
                logger.LogInformation("Successfully updated registration token");
                return OperationResult.Ok(L["TokenUpdatedSuccessfully"]);
            });

    public Task<OperationResult> DeleteRegistrationTokenAsync(string token, CancellationToken cancellationToken = default) =>
        sessionService.RunGatewayCallAsync(L, "ErrorDeletingToken",
            ex => logger.LogError(ex, "Error deleting registration token"),
            async gateway =>
            {
                await gateway.DeleteRegistrationTokenAsync(token, cancellationToken);
                logger.LogInformation("Successfully deleted registration token");
                return OperationResult.Ok(L["TokenDeletedSuccessfully"]);
            });
}
