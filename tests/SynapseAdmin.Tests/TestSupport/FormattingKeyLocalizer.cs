using Microsoft.Extensions.Localization;
using SynapseAdmin.Resources;

namespace SynapseAdmin.Tests.TestSupport;

/// <summary>
/// Localizer double that returns the resource key as the value, with the format arguments applied to the key
/// through <see cref="string.Format(string, object?[])"/>.
/// </summary>
internal sealed class FormattingKeyLocalizer : IStringLocalizer<SharedResources>
{
    public LocalizedString this[string name] => new(name, name, resourceNotFound: false);

    public LocalizedString this[string name, params object[] arguments] =>
        new(name, string.Format(name, arguments), resourceNotFound: false);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}
