using LibMatrix.Homeservers.ImplementationDetails.Synapse.Models.Responses;

namespace SynapseAdmin.Models.ViewModels;

public class UserDetailViewModel
{
    public SynapseAdminUserListResult.SynapseAdminUserListResultUser Details { get; set; } = null!;
    public SynapseAdminUserMediaResult? Media { get; set; }
}
