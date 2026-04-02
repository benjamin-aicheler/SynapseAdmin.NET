using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Interfaces;

public interface IRegistrationTokenService
{
    Task<OperationResult<List<RegistrationTokenViewModel>>> GetRegistrationTokensAsync();
    Task<OperationResult> CreateRegistrationTokenAsync(RegistrationTokenViewModel viewModel);
    Task<OperationResult> UpdateRegistrationTokenAsync(string token, RegistrationTokenViewModel viewModel);
    Task<OperationResult> DeleteRegistrationTokenAsync(string token);
}
