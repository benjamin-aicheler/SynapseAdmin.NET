using Microsoft.AspNetCore.Components;

namespace SynapseAdmin.Components;

public partial class LanguageSelector : ComponentBase
{
    [Inject]
    public NavigationManager Navigation { get; set; } = default!;

    private void ChangeLanguage(string culture)
    {
        var uri = new Uri(Navigation.Uri)
            .GetComponents(UriComponents.PathAndQuery, UriFormat.Unescaped);
        
        var query = $"?culture={Uri.EscapeDataString(culture)}&redirectUri={Uri.EscapeDataString(uri)}";
        
        Navigation.NavigateTo("/Culture/Set" + query, forceLoad: true);
    }
}