using MudBlazor;
using SynapseAdmin.Components.Shared;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary><see cref="DialogPresets"/>: each preset has its exact options and returns a new instance per call.</summary>
public sealed class DialogPresetsTests
{
    [Theory]
    [InlineData(nameof(DialogPresets.Small))]
    [InlineData(nameof(DialogPresets.Medium))]
    [InlineData(nameof(DialogPresets.SmallWithCloseButton))]
    [InlineData(nameof(DialogPresets.MediumWithCloseButton))]
    public void Preset_ReturnsANewInstance_WithItsOptions(string preset)
    {
        Func<DialogOptions> create = preset switch
        {
            nameof(DialogPresets.Small) => DialogPresets.Small,
            nameof(DialogPresets.Medium) => DialogPresets.Medium,
            nameof(DialogPresets.SmallWithCloseButton) => DialogPresets.SmallWithCloseButton,
            _ => DialogPresets.MediumWithCloseButton,
        };
        var expected = preset switch
        {
            nameof(DialogPresets.Small) => new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Small, FullWidth = true },
            nameof(DialogPresets.Medium) => new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Medium, FullWidth = true },
            nameof(DialogPresets.SmallWithCloseButton) => new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Small, FullWidth = true },
            _ => new DialogOptions { CloseButton = true, MaxWidth = MaxWidth.Medium, FullWidth = true },
        };

        var first = create();
        var second = create();

        Assert.NotSame(first, second);
        Assert.Equivalent(expected, first, strict: true);
        Assert.Equivalent(expected, second, strict: true);
    }
}
