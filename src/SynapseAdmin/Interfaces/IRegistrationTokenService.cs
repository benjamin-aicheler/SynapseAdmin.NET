using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Interfaces;

public interface IRegistrationTokenService
{
    Task<OperationResult<List<RegistrationTokenViewModel>>> GetRegistrationTokensAsync(CancellationToken cancellationToken = default);
    Task<OperationResult> CreateRegistrationTokenAsync(RegistrationTokenViewModel viewModel, CancellationToken cancellationToken = default);
    Task<OperationResult> UpdateRegistrationTokenAsync(string token, RegistrationTokenViewModel viewModel, CancellationToken cancellationToken = default);
    Task<OperationResult> DeleteRegistrationTokenAsync(string token, CancellationToken cancellationToken = default);
}
