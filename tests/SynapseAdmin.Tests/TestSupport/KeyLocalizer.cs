using Microsoft.Extensions.Localization;
using SynapseAdmin.Resources;

namespace SynapseAdmin.Tests.TestSupport;

/// <summary>
/// Localizer double that returns the resource key as the value. Format arguments are ignored, so a message
/// built with <c>L[key, args]</c> equals its bare key.
/// </summary>
internal sealed class KeyLocalizer : IStringLocalizer<SharedResources>
{
    public LocalizedString this[string name] => new(name, name);
    public LocalizedString this[string name, params object[] arguments] => new(name, name);
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}
