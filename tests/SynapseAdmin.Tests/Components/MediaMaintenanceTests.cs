using System.Globalization;
using System.Net;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using MudBlazor;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Resources;
using SynapseAdmin.Services;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// The Delete Local Media card describes the operation as Synapse runs it (#542, AC-15): the age field is the last
/// access, the confirmation names the size (none for 0 MB) and what happens to avatars, and the success message names
/// the count the homeserver reports. The page runs against the real <see cref="MediaService"/> and a stub gateway.
/// </summary>
public class MediaMaintenanceTests : DetailPageTestBase
{
    private readonly Stub _gateway;
    private Func<object?[], object?> _deleteLocalMedia = _ =>
        Task.FromResult<SynapseAdminDeleteMediaResponse?>(new SynapseAdminDeleteMediaResponse { Total = 2, DeletedMedia = ["a", "b"] });

    public MediaMaintenanceTests()
    {
        var gateway = Stub.Create<IMatrixGateway>(out _gateway);
        _gateway
            .On("get_SupportsAdminApi", _ => true)
            .On(nameof(IMatrixGateway.DeleteLocalMediaAsync), args => _deleteLocalMedia(args));
        Services.AddSingleton(Stub.Create<IMatrixSessionService>(s => s
            .On("get_Gateway", _ => gateway)
            .On("get_IsLoggedIn", _ => true)));
        Services.AddSingleton<IMediaService, MediaService>();
    }

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    [Fact]
    public void Card_LabelsTheAgeAsLastAccess()
    {
        var cut = Render<MediaMaintenance>();

        var markup = WebUtility.HtmlDecode(cut.Markup);
        // The purge card and the delete card both select by last access.
        var labels = cut.FindAll("label").Select(label => label.TextContent.Trim()).ToList();
        Assert.Equal(2, labels.Count(label => label == L["LastAccessedBeforeDays"]));
        Assert.Contains(L["DeleteLocalMediaDescription"], markup);
        Assert.Contains(L["KeepProfiles"], markup);
    }

    [Fact]
    public async Task Confirmation_NamesTheSizeAndKeepsAvatars_ThenReportsTheDeletedCount()
    {
        var cut = Render<MediaMaintenance>();

        await ConfirmDeleteAsync(cut);

        Assert.Equal(
            $"{string.Format(L["DeleteLocalMediaConfirmation"], 90, 10)} {L["DeleteLocalMediaKeepsAvatars"]} {L["DeleteLocalMediaIrreversible"]}",
            Confirmations[0].Message);
        var call = Assert.Single(_gateway.CallsTo(nameof(IMatrixGateway.DeleteLocalMediaAsync)));
        Assert.Equal((10L * 1024 * 1024, true), ((long)call[1]!, (bool)call[2]!));
        AssertSnackbars(OperationResult.Ok(string.Format(L["DeletedLocalMediaSuccess"], 2)));
    }

    [Fact]
    public async Task Confirmation_WithoutMinimumSize_SaysAnySize_AndThatAvatarsAreDeleted()
    {
        var cut = Render<MediaMaintenance>();
        await cut.InvokeAsync(() => typeof(MediaMaintenance)
            .GetMethod("OnDeleteLocalSizeMbChanged", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(cut.Instance, [0]));
        cut.Render();
        Assert.Contains(L["DeleteAllSizesWarning"], WebUtility.HtmlDecode(cut.Markup));
        SetField(cut.Instance, "keepProfiles", false);
        SetField(cut.Instance, "confirmUnboundedSize", true);

        await ConfirmDeleteAsync(cut);

        Assert.Equal(
            $"{string.Format(L["DeleteLocalMediaConfirmationAnySize"], 90)} {L["DeleteLocalMediaDeletesAvatars"]} {L["DeleteLocalMediaIrreversible"]}",
            Confirmations[0].Message);
        var call = Assert.Single(_gateway.CallsTo(nameof(IMatrixGateway.DeleteLocalMediaAsync)));
        Assert.Equal((0L, false), ((long)call[1]!, (bool)call[2]!));
    }

    /// <summary>A gateway that refuses the deletion (Tuwunel) shows the "not supported" warning after confirmation (AC-14).</summary>
    [Fact]
    public async Task RefusedDeletion_ShowsTheNotSupportedWarning()
    {
        _deleteLocalMedia = _ => Task.FromException<SynapseAdminDeleteMediaResponse?>(
            new OperationNotSupportedByHomeserverException(nameof(IMatrixGateway.DeleteLocalMediaAsync)));
        var cut = Render<MediaMaintenance>();

        await ConfirmDeleteAsync(cut);

        AssertSnackbars(OperationResult.Failure(L["OperationNotSupportedByHomeserver"], Severity.Warning));
    }

    /// <summary>The card's texts and the placeholders each needs, in every UI language.</summary>
    public static TheoryData<string, string[]> CardTexts => new()
    {
        { "DeleteLocalMediaDescription", [] },
        { "KeepProfiles", [] },
        { "DeleteAllSizesWarning", [] },
        { "ConfirmDeleteAllSizes", [] },
        { "DeleteLocalMediaConfirmation", ["{0}", "{1}"] },
        { "DeleteLocalMediaConfirmationAnySize", ["{0}"] },
        { "DeleteLocalMediaKeepsAvatars", [] },
        { "DeleteLocalMediaDeletesAvatars", [] },
        { "DeleteLocalMediaIrreversible", [] },
        { "DeletedLocalMediaSuccess", ["{0}"] },
    };

    [Theory]
    [MemberData(nameof(CardTexts))]
    public void CardText_IsTranslated_WithExactlyItsPlaceholders(string key, string[] placeholders)
    {
        var resources = new ResourceManager(typeof(SharedResources).FullName!, typeof(SharedResources).Assembly);
        var english = resources.GetString(key, new CultureInfo("en"));

        foreach (var culture in new[] { "en", "de", "fr" })
        {
            var text = resources.GetString(key, new CultureInfo(culture));
            Assert.False(string.IsNullOrWhiteSpace(text), $"{key} ({culture})");
            if (culture != "en") Assert.NotEqual(english, text);
            Assert.Equal(placeholders, Regex.Matches(text!, @"\{\d+\}").Select(m => m.Value).Distinct().Order());
        }
    }

    private async Task ConfirmDeleteAsync(IRenderedComponent<MediaMaintenance> cut)
    {
        var action = InvokeHandler(cut, "ConfirmDeleteLocalMedia");
        cut.WaitForAssertion(() => Assert.Single(Confirmations));
        Confirmations[0].Result.SetResult(true);
        await action;
    }

    private static void SetField(object instance, string name, object? value) =>
        (instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
         ?? throw new MissingFieldException(instance.GetType().Name, name)).SetValue(instance, value);
}
