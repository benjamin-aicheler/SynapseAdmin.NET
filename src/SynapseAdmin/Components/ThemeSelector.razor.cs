using Microsoft.AspNetCore.Components;
using SynapseAdmin.Interfaces;

namespace SynapseAdmin.Components;

public partial class ThemeSelector : ComponentBase
{
    [Inject]
    public IThemeService ThemeService { get; set; } = default!;
}
