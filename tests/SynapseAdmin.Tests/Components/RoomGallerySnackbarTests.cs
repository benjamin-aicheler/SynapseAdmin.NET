using Bunit;
using Microsoft.Extensions.DependencyInjection;
using SynapseAdmin.Components.Shared;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models;
using SynapseAdmin.Models.ViewModels;
using Xunit;

namespace SynapseAdmin.Tests.Components;

/// <summary>
/// Which results the room gallery reports in a snackbar: the media actions and the room load report every result
/// except a silent cancellation, the metadata load reports only failures.
/// </summary>
public class RoomGallerySnackbarTests : DetailPageTestBase
{
    private const string RoomId = "!a:hs";
    private const string Mxc = "mxc://hs/a";

    private readonly Stub _rooms;
    private OperationResult _result = new();
    private OperationResult<RoomDetailViewModel> _room = OperationResult<RoomDetailViewModel>.Ok(new RoomDetailViewModel
    {
        RoomId = RoomId, Media = new RoomMediaViewModel { Local = [new() { MediaId = Mxc }] }
    });
    private OperationResult<List<RoomMediaItemViewModel>> _metadata = OperationResult<List<RoomMediaItemViewModel>>.Ok([new() { MediaId = Mxc }]);

    public RoomGallerySnackbarTests()
    {
        Services.AddSingleton(Stub.Create<IRoomService>(out _rooms));
        _rooms
            .On(nameof(IRoomService.GetRoomDetailsAsync), _ => Task.FromResult(_room))
            .On(nameof(IRoomService.GetMediaMetadataBatchAsync), _ => Task.FromResult(_metadata));
        Services.AddSingleton(Stub.Create<IMediaService>(s =>
        {
            foreach (var method in typeof(IMediaService).GetMethods().Where(m => m.ReturnType == typeof(Task<OperationResult>)))
            {
                s.On(method.Name, _ => Task.FromResult(_result));
            }
        }));
    }

    /// <summary>Renders the grid without media, so it loads the room itself, and waits for that load.</summary>
    private IRenderedComponent<RoomGalleryGrid> RenderGrid()
    {
        var cut = Render<RoomGalleryGrid>(p => p.Add(x => x.RoomId, RoomId));
        cut.WaitForAssertion(() => Assert.Single(_rooms.CallsTo(nameof(IRoomService.GetRoomDetailsAsync))));
        return cut;
    }

    public static TheoryData<string, string> Actions()
    {
        var data = new TheoryData<string, string>();
        foreach (var handler in new[] { "QuarantineSingleMedia", "UnquarantineSingleMedia", "DeleteSingleMedia", "ToggleMediaProtection" })
        {
            foreach (var kind in new[] { "success", "failure", "cancelled" }) data.Add(handler, kind);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Action_ReportsEveryResultExceptACancellation(string handler, string kind)
    {
        _result = Make<OperationResult>(kind);
        var cut = RenderGrid();

        var action = InvokeHandler(cut, handler, new RoomMediaItemViewModel { MediaId = Mxc });
        if (handler != "ToggleMediaProtection")
        {
            cut.WaitForAssertion(() => Assert.Single(Confirmations));
            Confirmations[0].Result.SetResult(true);
        }
        await action;

        AssertSnackbars(kind == "cancelled" ? [] : [_result]);
    }

    [Theory]
    [InlineData("failure", true)]
    [InlineData("cancelled", false)]
    [InlineData("success", true)]
    public void RoomLoad_ReportsEveryResultWithoutARoomExceptACancellation(string kind, bool shown)
    {
        _room = Make<OperationResult<RoomDetailViewModel>>(kind);

        RenderGrid();

        AssertSnackbars(shown ? [_room] : []);
    }

    [Theory]
    [InlineData("failure", true)]
    [InlineData("cancelled", false)]
    [InlineData("success", false)]
    public void MetadataLoad_ReportsOnlyFailures(string kind, bool shown)
    {
        _metadata = Make<OperationResult<List<RoomMediaItemViewModel>>>(kind);
        var cut = RenderGrid();

        cut.WaitForAssertion(() => Assert.Single(_rooms.CallsTo(nameof(IRoomService.GetMediaMetadataBatchAsync))));
        AssertSnackbars(shown ? [_metadata] : []);
    }
}
