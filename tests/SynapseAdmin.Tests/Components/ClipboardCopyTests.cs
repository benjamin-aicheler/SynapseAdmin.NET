using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Extensions;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using SynapseAdmin.Resources;
using SynapseAdmin.Tests.TestSupport;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// The three clipboard copies: what each page writes, and which snackbar it shows when the browser accepts or refuses
/// the write, and the shared helper behind them. Any exception counts as refused.
/// </summary>
public class ClipboardCopyTests : DetailPageTestBase
{
    private const string WriteText = "navigator.clipboard.writeText";

    private IStringLocalizer<SharedResources> L => Services.GetRequiredService<IStringLocalizer<SharedResources>>();

    /// <summary>Sets up the clipboard write: <c>null</c> succeeds, <c>"js"</c> and <c>"other"</c> throw.</summary>
    private void SetupClipboard(string? failure)
    {
        var handler = JSInterop.SetupVoid(WriteText, _ => true);
        switch (failure)
        {
            case "js": handler.SetException(new JSException("denied")); break;
            case "other": handler.SetException(new InvalidOperationException("circuit gone")); break;
            default: handler.SetVoidResult(); break;
        }
    }

    private void ClickButtonTitled<T>(IRenderedComponent<T> cut, string titleKey) where T : IComponent =>
        cut.FindAll("button").Single(b => b.GetAttribute("title") == L[titleKey].Value).Click();

    /// <summary>Records what the page of type <typeparamref name="T"/> logs.</summary>
    private RecordingLogger<T> PageLogs<T>()
    {
        var logger = new RecordingLogger<T>();
        Services.AddSingleton<ILogger<T>>(logger);
        return logger;
    }

    /// <summary>
    /// A failed write is logged once at Debug with its exception, but without the copied text, which can be an access
    /// token. A successful write isn't logged.
    /// </summary>
    private static void AssertClipboardLog<T>(RecordingLogger<T> logger, string? failure)
    {
        if (failure == null)
        {
            Assert.Empty(logger.Entries);
            return;
        }
        var entry = Assert.Single(logger.Entries);
        Assert.Equal((LogLevel.Debug, "Clipboard write failed"), (entry.Level, entry.Template));
        Assert.Empty(entry.Arguments);
        Assert.IsType(failure == "js" ? typeof(JSException) : typeof(InvalidOperationException), entry.Exception);
    }

    /// <summary>Asserts the single clipboard write and the single snackbar.</summary>
    private void AssertCopied(string text, string message, Severity severity)
    {
        Assert.Equal(text, Assert.Single(JSInterop.VerifyInvoke(WriteText).Arguments));
        var snackbar = Assert.Single(SnackbarStub.CallsTo(nameof(ISnackbar.Add)));
        Assert.Equal((message, severity), ((string?)snackbar[0], (Severity)snackbar[1]!));
    }

    [Theory]
    [InlineData(null, "TokenCopiedToClipboard", Severity.Success)]
    [InlineData("js", "ErrorCopyingToClipboard", Severity.Error)]
    [InlineData("other", "ErrorCopyingToClipboard", Severity.Error)]
    public void RegistrationTokens_CopyButton(string? failure, string messageKey, Severity severity)
    {
        SetupClipboard(failure);
        var logger = PageLogs<RegistrationTokens>();
        Services.AddSingleton(Stub.Create<IRegistrationTokenService>(s => s.On(nameof(IRegistrationTokenService.GetRegistrationTokensAsync), _ =>
            Task.FromResult(OperationResult<List<RegistrationTokenViewModel>>.Ok([new() { Token = "tok-123" }])))));
        var cut = Render<RegistrationTokens>();
        cut.WaitForAssertion(() => Assert.Contains("tok-123", cut.Markup));

        ClickButtonTitled(cut, "CopyToClipboard");

        cut.WaitForAssertion(() => AssertCopied("tok-123", L[messageKey], severity));
        AssertClipboardLog(logger, failure);
    }

    [Theory]
    [InlineData(null, "JsonCopiedToClipboard", Severity.Success)]
    [InlineData("js", "ErrorCopyingToClipboard", Severity.Error)]
    [InlineData("other", "ErrorCopyingToClipboard", Severity.Error)]
    public void EventForensics_CopyJsonButton(string? failure, string messageKey, Severity severity)
    {
        SetupClipboard(failure);
        var logger = PageLogs<EventForensics>();
        Services.AddSingleton(Stub.Create<IEventForensicsService>(s => s.On(nameof(IEventForensicsService.FetchEventAsync), _ =>
            Task.FromResult(OperationResult<EventForensicsViewModel>.Ok(
                new EventForensicsViewModel { EventId = "$e", Type = "m.room.message", Sender = "@a:hs", RawJson = """{"a":1}""" })))));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/event-forensics?eventId=%24e");
        var cut = Render<EventForensics>();
        cut.WaitForAssertion(() => Assert.Contains("m.room.message", cut.Markup));

        ClickButtonTitled(cut, "CopyJson");

        cut.WaitForAssertion(() => AssertCopied("""{"a":1}""", L[messageKey], severity));
        AssertClipboardLog(logger, failure);
    }

    /// <summary>
    /// The snackbar keeps the login result's own severity and drops the "copied" note when the write fails. The token
    /// dialog follows either way.
    /// </summary>
    [Theory]
    [InlineData(null, true)]
    [InlineData("js", false)]
    [InlineData("other", false)]
    public async Task UserDetails_LoginAsUser(string? failure, bool copied)
    {
        SetupClipboard(failure);
        var logger = PageLogs<UserDetails>();
        Services.AddSingleton(Stub.Create<IUserService>(s => s
            .On(nameof(IUserService.GetUserDetailsAsync), _ => Task.FromResult(OperationResult<UserDetailViewModel>.Ok(
                new UserDetailViewModel { UserId = "@alice:hs", DisplayName = "Alice Example" })))
            .On(nameof(IUserService.LoginAsUserAsync), _ => Task.FromResult(new OperationResult<string>
            {
                Success = true, Data = "syt_token", Message = "Logged in", Severity = Severity.Info
            }))));
        var cut = Render<UserDetails>(p => p.Add(x => x.UserId, "@alice:hs"));
        cut.WaitForAssertion(() => Assert.Contains("Alice Example", cut.Markup));

        var login = InvokeHandler(cut, "LoginAsUser");

        cut.WaitForAssertion(() => Assert.Single(Confirmations));
        AssertCopied("syt_token", copied ? "Logged in " + L["TokenCopiedToClipboard"] : "Logged in", Severity.Info);
        AssertClipboardLog(logger, failure);
        Confirmations[0].Result.SetResult(true);
        await login;
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("js", false)]
    [InlineData("other", false)]
    public async Task TryCopyToClipboardAsync_ReportsWhetherTheWriteSucceeded(string? failure, bool expected)
    {
        SetupClipboard(failure);

        Assert.Equal(expected, await JSInterop.JSRuntime.TryCopyToClipboardAsync("text"));
        Assert.Equal("text", Assert.Single(JSInterop.VerifyInvoke(WriteText).Arguments));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("js")]
    [InlineData("other")]
    public async Task TryCopyToClipboardAsync_LogsAFailureAtDebug_WithoutTheText(string? failure)
    {
        SetupClipboard(failure);
        var logger = new RecordingLogger<ClipboardCopyTests>();

        await JSInterop.JSRuntime.TryCopyToClipboardAsync("syt_token", logger);

        AssertClipboardLog(logger, failure);
    }
}
