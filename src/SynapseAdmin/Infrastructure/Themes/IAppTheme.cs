using MudBlazor;

namespace SynapseAdmin.Infrastructure.Themes;

public interface IAppTheme
{
    string Id { get; }
    string Name { get; }
    MudTheme Theme { get; }
}
