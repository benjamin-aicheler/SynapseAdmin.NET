using SynapseAdmin.Models.ViewModels;
using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;

namespace SynapseAdmin.Extensions.Mapping;

public static class RegistrationTokenMappingExtensions
{
    public static RegistrationTokenViewModel ToViewModel(this SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken token)
    {
        return new RegistrationTokenViewModel
        {
            Token = token.Token,
            UsesAllowed = token.UsesAllowed,
            Pending = token.Pending,
            Completed = token.Completed,
            ExpiryTime = token.ExpiryTime
        };
    }

    public static List<RegistrationTokenViewModel> ToViewModels(this IEnumerable<SynapseAdminRegistrationTokenListResult.SynapseAdminRegistrationTokenListResultToken> tokens)
    {
        return tokens.Select(ToViewModel).ToList();
    }
}
