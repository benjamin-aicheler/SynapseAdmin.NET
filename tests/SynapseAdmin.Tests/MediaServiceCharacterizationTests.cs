using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using MudBlazor;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Interfaces.Gateways;
using SynapseAdmin.Models;
using SynapseAdmin.Models.Requests;
using SynapseAdmin.Models.Responses;
using SynapseAdmin.Resources;
using SynapseAdmin.Services;
using SynapseAdmin.Tests.Components;
using SynapseAdmin.Tests.TestSupport;
using Xunit;

namespace SynapseAdmin.Tests;

/// <summary>
/// Characterization of the result and logging contract of every <see cref="MediaService"/> method (P3-11a): no
/// session, blank MXC URI, cancellation, gateway failure, null gateway result and success.
/// </summary>
public sealed class MediaServiceCharacterizationTests : IDisposable
{
    // The line break proves that every log call sanitizes the MXC URI.
    private const string Mxc = "mxc://example.com/media\nid";
    private const string SanitizedMxc = "mxc://example.com/media id";
    private const long BeforeTs = 1_700_000_000_000;
    private const long SizeGt = 4096;
    private static readonly MediaByteRange ByteRange = new(10, 20);

    private sealed record Operation(
        string GatewayMethod,
        Func<MediaService, string, CancellationToken, Task<OperationResult>> Invoke,
        Func<CancellationToken, object?[]> GatewayArgs,
        string FailureKey,
        string ErrorTemplate,
        IReadOnlyDictionary<string, object?> ErrorArguments,
        string? SuccessKey = null,
        string? InfoTemplate = null);

    private static readonly Dictionary<string, object?> MxcArgument = new() { ["Mxc"] = SanitizedMxc };

    private static readonly Dictionary<string, Operation> Operations = new()
    {
        ["Open"] = new("OpenMediaAsync", async (s, mxc, ct) => await s.OpenMediaAsync(mxc, ByteRange, ct), ct => [Mxc, ByteRange, ct],
            "ErrorFetchingMedia", "Error fetching media for MXC: {Mxc}", MxcArgument),
        ["Metadata"] = new("GetMediaMetadataAsync", async (s, mxc, ct) => await s.GetMediaMetadataAsync(mxc, ct), ct => [Mxc, ct],
            "ErrorFetchingMedia", "Error fetching media metadata for MXC: {Mxc}", MxcArgument),
        ["Quarantine"] = new("QuarantineMediaAsync", (s, mxc, ct) => s.QuarantineMediaAsync(mxc, ct), ct => [Mxc, ct],
            "ErrorQuarantiningMedia", "Error quarantining media {Mxc}", MxcArgument,
            "MediaQuarantinedSuccessfully", "Successfully quarantined media {Mxc}"),
        ["Unquarantine"] = new("UnquarantineMediaAsync", (s, mxc, ct) => s.UnquarantineMediaAsync(mxc, ct), ct => [Mxc, ct],
            "ErrorUnquarantiningMedia", "Error unquarantining media {Mxc}", MxcArgument,
            "MediaUnquarantinedSuccessfully", "Successfully unquarantined media {Mxc}"),
        ["Delete"] = new("DeleteMediaAsync", (s, mxc, ct) => s.DeleteMediaAsync(mxc, ct), ct => [Mxc, ct],
            "ErrorDeletingMedia", "Error deleting media {Mxc}", MxcArgument,
            "MediaDeletedSuccessfully", "Successfully deleted media {Mxc}"),
        ["Protect"] = new("ProtectMediaAsync", (s, mxc, ct) => s.ProtectMediaAsync(mxc, ct), ct => [Mxc, ct],
            "ErrorProtectingMedia", "Error protecting media {Mxc}", MxcArgument,
            "MediaProtectedSuccessfully", "Successfully protected media {Mxc}"),
        ["Unprotect"] = new("UnprotectMediaAsync", (s, mxc, ct) => s.UnprotectMediaAsync(mxc, ct), ct => [Mxc, ct],
            "ErrorUnprotectingMedia", "Error unprotecting media {Mxc}", MxcArgument,
            "MediaUnprotectedSuccessfully", "Successfully unprotected media {Mxc}"),
        ["PurgeRemoteCache"] = new("PurgeRemoteMediaCacheAsync", async (s, _, ct) => await s.PurgeRemoteMediaCacheAsync(BeforeTs, ct), ct => [BeforeTs, ct],
            "ErrorPurgingMediaCache", "Error purging remote media cache (beforeTs: {BeforeTs})",
            new Dictionary<string, object?> { ["BeforeTs"] = BeforeTs }),
        ["DeleteLocal"] = new("DeleteLocalMediaAsync", async (s, _, ct) => await s.DeleteLocalMediaAsync(BeforeTs, SizeGt, true, ct), ct => [BeforeTs, SizeGt, true, ct],
            "ErrorDeletingLocalMedia", "Error deleting local media (beforeTs: {BeforeTs}, sizeGt: {SizeGt}, keepProfiles: {KeepProfiles})",
            new Dictionary<string, object?> { ["BeforeTs"] = BeforeTs, ["SizeGt"] = SizeGt, ["KeepProfiles"] = true }),
    };

    private static readonly string[] MxcOperationNames = ["Open", "Metadata", "Quarantine", "Unquarantine", "Delete", "Protect", "Unprotect"];

    public static TheoryData<string> AllOperations => Names(Operations.Keys);
    public static TheoryData<string> MxcOperations => Names(MxcOperationNames);
    public static TheoryData<string> ActionOperations => Names(Operations.Where(o => o.Value.SuccessKey != null).Select(o => o.Key));

    public static TheoryData<string, string> MxcOperationsWithBlankMxc
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var name in MxcOperationNames)
            {
                data.Add(name, "");
                data.Add(name, " \t");
            }
            return data;
        }
    }

    private readonly CancellationTokenSource _cts = new();
    private readonly RecordingLogger<MediaService> _logger = new();

    public void Dispose() => _cts.Dispose();

    [Theory, MemberData(nameof(AllOperations))]
    public async Task NoSession_FailsNotAuthenticated_WithoutLogging(string name)
    {
        var result = await Operations[name].Invoke(CreateService(gateway: null), Mxc, _cts.Token);

        AssertFailure(result, "NotAuthenticated");
        Assert.Empty(_logger.Entries);
    }

    [Theory, MemberData(nameof(MxcOperations))]
    public async Task NoSessionAndBlankMxc_FailsNotAuthenticated(string name)
    {
        var result = await Operations[name].Invoke(CreateService(gateway: null), " ", _cts.Token);

        AssertFailure(result, "NotAuthenticated");
    }

    [Theory, MemberData(nameof(MxcOperationsWithBlankMxc))]
    public async Task BlankMxc_FailsWithOperationKey_WithoutCallingGateway(string name, string mxc)
    {
        var gateway = Stub.Create<IMatrixGateway>(out var stub);

        var result = await Operations[name].Invoke(CreateService(gateway), mxc, _cts.Token);

        AssertFailure(result, Operations[name].FailureKey);
        Assert.Empty(stub.Calls);
        Assert.Empty(_logger.Entries);
    }

    [Theory, MemberData(nameof(AllOperations))]
    public async Task ForwardsArgumentsToGateway(string name)
    {
        var operation = Operations[name];
        var gateway = Stub.Create<IMatrixGateway>(out var stub);

        await operation.Invoke(CreateService(gateway), Mxc, _cts.Token);

        var call = Assert.Single(stub.Calls);
        Assert.Equal(operation.GatewayMethod, call.Method);
        Assert.Equal(operation.GatewayArgs(_cts.Token), call.Args);
    }

    [Theory, MemberData(nameof(AllOperations))]
    public async Task GatewayCancels_ReturnsSilentCancellation_WithoutLogging(string name)
    {
        var result = await Operations[name].Invoke(CreateService(FaultingGateway(name, new TaskCanceledException())), Mxc, _cts.Token);

        Assert.False(result.Success);
        Assert.Equal(Severity.Normal, result.Severity);
        Assert.Equal("Operation cancelled", result.Message);
        Assert.Empty(_logger.Entries);
    }

    [Theory, MemberData(nameof(AllOperations))]
    public async Task GatewayThrows_FailsWithOperationKey_AndLogsError(string name)
    {
        var operation = Operations[name];
        var error = new InvalidOperationException("boom");

        var result = await operation.Invoke(CreateService(FaultingGateway(name, error)), Mxc, _cts.Token);

        AssertFailure(result, operation.FailureKey);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Equal(operation.ErrorTemplate, entry.Template);
        Assert.Equal(operation.ErrorArguments, entry.Arguments);
        Assert.Same(error, entry.Exception);
    }

    [Theory]
    [InlineData("Metadata")]
    [InlineData("PurgeRemoteCache")]
    [InlineData("DeleteLocal")]
    public async Task NullGatewayResult_FailsWithOperationKey_WithoutLogging(string name)
    {
        var result = await Operations[name].Invoke(CreateService(Stub.Create<IMatrixGateway>()), Mxc, _cts.Token);

        AssertFailure(result, Operations[name].FailureKey);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task OpenMedia_NullGatewayResult_SucceedsWithoutData()
    {
        var result = await CreateService(Stub.Create<IMatrixGateway>()).OpenMediaAsync(Mxc, ByteRange, _cts.Token);

        AssertSuccess(result, "");
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task OpenMedia_Success_ReturnsTheDownload_WithoutLogging()
    {
        await using var download = new MediaDownload { Status = MediaDownloadStatus.Partial, Content = Stream.Null };
        var gateway = Stub.Create<IMatrixGateway>(s => s.On("OpenMediaAsync", _ => Task.FromResult(download)));

        var result = await CreateService(gateway).OpenMediaAsync(Mxc, ByteRange, _cts.Token);

        AssertSuccess(result, "");
        Assert.Same(download, result.Data);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task Metadata_Success_ReturnsTheMetadata_WithoutLogging()
    {
        var meta = new SynapseAdminMediaMetadataResponse.MediaInfo { MediaOrigin = "local" };
        var gateway = Stub.Create<IMatrixGateway>(s => s.On("GetMediaMetadataAsync", _ => Task.FromResult<SynapseAdminMediaMetadataResponse.MediaInfo?>(meta)));

        var result = await CreateService(gateway).GetMediaMetadataAsync(Mxc, _cts.Token);

        AssertSuccess(result, "");
        Assert.Same(meta, result.Data);
        Assert.Empty(_logger.Entries);
    }

    [Theory, MemberData(nameof(ActionOperations))]
    public async Task Action_Success_ReturnsLocalizedMessage_AndLogsInformation(string name)
    {
        var operation = Operations[name];

        var result = await operation.Invoke(CreateService(Stub.Create<IMatrixGateway>()), Mxc, _cts.Token);

        AssertSuccess(result, operation.SuccessKey!);
        var entry = Assert.Single(_logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(operation.InfoTemplate, entry.Template);
        Assert.Equal(MxcArgument, entry.Arguments);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public async Task PurgeRemoteCache_Success_FormatsTheDeletedCount_WithoutLogging()
    {
        var response = new SynapseAdminPurgeMediaCacheResponse { Deleted = 7 };
        var gateway = Stub.Create<IMatrixGateway>(s => s.On("PurgeRemoteMediaCacheAsync", _ => Task.FromResult<SynapseAdminPurgeMediaCacheResponse?>(response)));

        var result = await CreateService(gateway).PurgeRemoteMediaCacheAsync(BeforeTs, _cts.Token);

        AssertSuccess(result, "PurgedCacheSuccess: 7");
        Assert.Same(response, result.Data);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task DeleteLocal_Success_ReturnsTheResponse_WithoutLogging()
    {
        var response = new SynapseAdminDeleteMediaResponse { Total = 2, DeletedMedia = ["a", "b"] };
        var gateway = Stub.Create<IMatrixGateway>(s => s.On("DeleteLocalMediaAsync", _ => Task.FromResult<SynapseAdminDeleteMediaResponse?>(response)));

        var result = await CreateService(gateway).DeleteLocalMediaAsync(BeforeTs, SizeGt, true, _cts.Token);

        AssertSuccess(result, "DeletedLocalMediaSuccess");
        Assert.Same(response, result.Data);
        Assert.Empty(_logger.Entries);
    }

    private MediaService CreateService(IMatrixGateway? gateway)
    {
        var session = Stub.Create<IMatrixSessionService>(s => s.On("get_Gateway", _ => gateway));
        // Returns the key, except for the one message that formats a number into its localized text.
        var localizer = Stub.Create<IStringLocalizer<SharedResources>>(s => s.On("get_Item", args =>
        {
            var key = (string)args[0]!;
            return new LocalizedString(key, key == "PurgedCacheSuccess" ? "PurgedCacheSuccess: {0}" : key);
        }));
        return new MediaService(session, _logger, localizer);
    }

    /// <summary>A gateway whose <paramref name="name"/> operation returns a task faulted with <paramref name="error"/>.</summary>
    private static IMatrixGateway FaultingGateway(string name, Exception error)
    {
        var method = Operations[name].GatewayMethod;
        return Stub.Create<IMatrixGateway>(s => s.On(method, args =>
        {
            var returnType = typeof(IMatrixGateway).GetMethods()
                .Single(m => m.Name == method && m.GetParameters().Length == args.Length).ReturnType;
            if (returnType == typeof(Task)) return Task.FromException(error);
            return typeof(Task).GetMethods().Single(m => m.Name == nameof(Task.FromException) && m.IsGenericMethod)
                .MakeGenericMethod(returnType.GetGenericArguments()[0]).Invoke(null, [error]);
        }));
    }

    private static TheoryData<string> Names(IEnumerable<string> names)
    {
        var data = new TheoryData<string>();
        foreach (var name in names) data.Add(name);
        return data;
    }

    private static void AssertFailure(OperationResult result, string message)
    {
        Assert.False(result.Success);
        Assert.Equal(message, result.Message);
        Assert.Equal(Severity.Error, result.Severity);
    }

    private static void AssertSuccess(OperationResult result, string message)
    {
        Assert.True(result.Success);
        Assert.Equal(message, result.Message);
        Assert.Equal(Severity.Success, result.Severity);
    }
}
