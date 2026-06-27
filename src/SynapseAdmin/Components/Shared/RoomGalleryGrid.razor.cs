using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using SynapseAdmin.Components.Pages;
using SynapseAdmin.Interfaces;
using SynapseAdmin.Models.ViewModels;

namespace SynapseAdmin.Components.Shared
{
    public partial class RoomGalleryGrid : IAsyncDisposable
    {
        [Inject] public IRoomService RoomService { get; set; } = null!;
        [Inject] public IMediaService MediaService { get; set; } = null!;
        [Inject] public ISnackbar Snackbar { get; set; } = null!;
        [Inject] public IDialogService DialogService { get; set; } = null!;
        [Inject] public IJSRuntime JSRuntime { get; set; } = null!;

        [Parameter] public string RoomId { get; set; } = string.Empty;
        [Parameter] public RoomMediaViewModel? Media { get; set; }

        private ElementReference _sentinelElement;
        private IJSObjectReference? _jsModule;
        private IJSObjectReference? _scrollObserver;
        private DotNetObjectReference<RoomGalleryGrid>? _dotnetRef;

        private bool _isLoadingInitial = true;
        private bool _isBatchLoading = false;
        private bool _hasMoreToLoad = false;
        private const int PageSize = 20;

        private string _searchTerm = string.Empty;
        private string _sourceFilter = "local"; // all, local, remote
        private string _typeFilter = "all";   // all, image, video, audio, file

        private readonly List<RoomMediaItemViewModel> _allMedia = [];
        private List<RoomMediaItemViewModel> _filteredMedia = [];
        private List<RoomMediaItemViewModel> _displayedMedia = [];
        private readonly HashSet<string> _loadedMetadataMxcs = [];
        private readonly CancellationTokenSource _cts = new();

        protected override async Task OnParametersSetAsync()
        {
            await LoadRoomMedia();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender && !_isLoadingInitial)
            {
                await SetupInfiniteScroll();
            }
        }

        private async Task SetupInfiniteScroll()
        {
            try
            {
                _dotnetRef ??= DotNetObjectReference.Create(this);
                _jsModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/gallery-interop.js");
                if (_jsModule != null && _scrollObserver == null)
                {
                    _scrollObserver = await _jsModule.InvokeAsync<IJSObjectReference>("initInfiniteScroll", _sentinelElement, _dotnetRef);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error setting up infinite scroll JS interop: {ex.Message}");
            }
        }

        private async Task LoadRoomMedia()
        {
            _isLoadingInitial = true;
            StateHasChanged();

            _allMedia.Clear();
            _loadedMetadataMxcs.Clear();

            RoomMediaViewModel? mediaModel = Media;
            if (mediaModel == null && !string.IsNullOrEmpty(RoomId))
            {
                var roomResult = await RoomService.GetRoomDetailsAsync(RoomId, _cts.Token);
                if (roomResult.Success && roomResult.Data != null)
                {
                    mediaModel = roomResult.Data.Media;
                }
            }

            if (mediaModel != null)
            {
                if (mediaModel.Local != null)
                {
                    foreach (var item in mediaModel.Local)
                    {
                        item.IsLocal = true;
                        _allMedia.Add(item);
                    }
                }
                if (mediaModel.Remote != null)
                {
                    foreach (var item in mediaModel.Remote)
                    {
                        item.IsLocal = false;
                        _allMedia.Add(item);
                    }
                }
            }

            ApplyFiltersAndResetDisplay();
            _isLoadingInitial = false;
            StateHasChanged();

            await LoadNextBatch();

            // Setup infinite scroll if not setup already
            if (_scrollObserver == null)
            {
                await SetupInfiniteScroll();
            }
        }

        private void ApplyFiltersAndResetDisplay()
        {
            IEnumerable<RoomMediaItemViewModel> query = _allMedia;

            if (_sourceFilter == "local")
                query = query.Where(x => x.IsLocal);
            else if (_sourceFilter == "remote")
                query = query.Where(x => !x.IsLocal);

            if (_typeFilter == "image")
                query = query.Where(x => IsImageType(x.MediaType));
            else if (_typeFilter == "video")
                query = query.Where(x => IsVideoType(x.MediaType));
            else if (_typeFilter == "audio")
                query = query.Where(x => IsAudioType(x.MediaType));
            else if (_typeFilter == "file")
                query = query.Where(x => !IsImageType(x.MediaType) && !IsVideoType(x.MediaType) && !IsAudioType(x.MediaType));

            if (!string.IsNullOrWhiteSpace(_searchTerm))
            {
                var term = _searchTerm.Trim();
                query = query.Where(x => (x.UploadName != null && x.UploadName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                                         x.MediaId.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            _filteredMedia = query.ToList();
            _displayedMedia = [];
            _hasMoreToLoad = _filteredMedia.Count > 0;
        }

        private async Task LoadNextBatch()
        {
            if (_isBatchLoading || !_hasMoreToLoad) return;

            _isBatchLoading = true;
            var currentCount = _displayedMedia.Count;
            var nextItems = _filteredMedia.Skip(currentCount).Take(PageSize).ToList();

            if (nextItems.Count == 0)
            {
                _hasMoreToLoad = false;
                _isBatchLoading = false;
                StateHasChanged();
                return;
            }

            _displayedMedia.AddRange(nextItems);
            _hasMoreToLoad = _displayedMedia.Count < _filteredMedia.Count;
            StateHasChanged();

            // Fetch metadata for unhydrated items in parallel/batch
            var unhydrated = nextItems.Where(x => !_loadedMetadataMxcs.Contains(x.MediaId)).Select(x => x.MediaId).ToList();
            if (unhydrated.Count > 0)
            {
                var metaResult = await RoomService.GetMediaMetadataBatchAsync(unhydrated, _cts.Token);
                if (metaResult.Success && metaResult.Data != null)
                {
                    var metaDict = metaResult.Data.ToDictionary(m => m.MediaId, m => m);
                    foreach (var item in _displayedMedia)
                    {
                        if (metaDict.TryGetValue(item.MediaId, out var metaItem))
                        {
                            item.UploadName = metaItem.UploadName;
                            item.MediaType = metaItem.MediaType;
                            item.MediaLength = metaItem.MediaLength;
                            item.CreatedTimestamp = metaItem.CreatedTimestamp;
                            item.QuarantinedBy = metaItem.QuarantinedBy;
                            item.SafeFromQuarantine = metaItem.SafeFromQuarantine;
                            _loadedMetadataMxcs.Add(item.MediaId);
                        }
                    }
                    StateHasChanged();
                }
            }

            _isBatchLoading = false;
        }

        [JSInvokable]
        public async Task OnSentinelIntersected()
        {
            if (_hasMoreToLoad && !_isBatchLoading && !_isLoadingInitial)
            {
                await LoadNextBatch();
            }
        }

        private async Task OnSearchTermChanged(string val)
        {
            _searchTerm = val;
            ApplyFiltersAndResetDisplay();
            await LoadNextBatch();
        }

        private async Task OnSourceFilterChanged(string val)
        {
            _sourceFilter = string.IsNullOrEmpty(val) ? "all" : val;
            ApplyFiltersAndResetDisplay();
            await LoadNextBatch();
        }

        private async Task OnTypeFilterChanged(string val)
        {
            _typeFilter = string.IsNullOrEmpty(val) ? "all" : val;
            ApplyFiltersAndResetDisplay();
            await LoadNextBatch();
        }

        private static string GetPreviewUrl(RoomMediaItemViewModel item)
        {
            return $"/Media/Preview?mxc={Uri.EscapeDataString(item.MediaId)}&mimeType={Uri.EscapeDataString(item.MediaType ?? "")}";
        }

        private static bool IsImageType(string? mimeType)
        {
            if (string.IsNullOrEmpty(mimeType)) return false;
            return mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsVideoType(string? mimeType)
        {
            if (string.IsNullOrEmpty(mimeType)) return false;
            return mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAudioType(string? mimeType)
        {
            if (string.IsNullOrEmpty(mimeType)) return false;
            return mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetFileIcon(string? mimeType)
        {
            if (string.IsNullOrEmpty(mimeType)) return Icons.Material.Filled.InsertDriveFile;
            if (mimeType.Contains("pdf")) return Icons.Material.Filled.PictureAsPdf;
            if (mimeType.Contains("zip") || mimeType.Contains("tar") || mimeType.Contains("archive")) return Icons.Material.Filled.FolderZip;
            if (mimeType.Contains("text") || mimeType.Contains("json")) return Icons.Material.Filled.Description;
            return Icons.Material.Filled.InsertDriveFile;
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }

        private async Task ShowPreview(RoomMediaItemViewModel media)
        {
            var previewUrl = GetPreviewUrl(media);
            var options = new DialogOptions { CloseOnEscapeKey = true, MaxWidth = MaxWidth.Medium, FullWidth = true };
            var parameters = new DialogParameters
            {
                { "Title", media.UploadName ?? media.MediaId },
                { "PreviewUrl", previewUrl },
                { "MediaType", media.MediaType }
            };

            await DialogService.ShowAsync<MediaPreviewDialog>(media.UploadName ?? L["MediaPreview"], parameters, options);
        }

        private async Task QuarantineSingleMedia(RoomMediaItemViewModel media)
        {
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L["QuarantineMediaTitle"],
                L["QuarantineMediaConfirmation"],
                yesText: L["Quarantine"], cancelText: L["Cancel"]);

            if (confirmed == true)
            {
                var result = await MediaService.QuarantineMediaAsync(media.MediaId, _cts.Token);
                Snackbar.Add(result.Message, result.Severity);
                if (result.Success)
                {
                    media.QuarantinedBy = "Admin";
                    StateHasChanged();
                }
            }
        }

        private async Task UnquarantineSingleMedia(RoomMediaItemViewModel media)
        {
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L["UnquarantineMediaTitle"],
                L["UnquarantineMediaConfirmation"],
                yesText: L["Unquarantine"], cancelText: L["Cancel"]);

            if (confirmed == true)
            {
                var result = await MediaService.UnquarantineMediaAsync(media.MediaId, _cts.Token);
                Snackbar.Add(result.Message, result.Severity);
                if (result.Success)
                {
                    media.QuarantinedBy = null;
                    StateHasChanged();
                }
            }
        }

        private async Task DeleteSingleMedia(RoomMediaItemViewModel media)
        {
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                L["DeleteMediaTitle"],
                L["DeleteMediaConfirmation"],
                yesText: L["Delete"], cancelText: L["Cancel"]);

            if (confirmed == true)
            {
                var result = await MediaService.DeleteMediaAsync(media.MediaId, _cts.Token);
                Snackbar.Add(result.Message, result.Severity);
                if (result.Success)
                {
                    _allMedia.Remove(media);
                    _filteredMedia.Remove(media);
                    _displayedMedia.Remove(media);
                    StateHasChanged();
                }
            }
        }

        private async Task ToggleMediaProtection(RoomMediaItemViewModel media)
        {
            var result = media.SafeFromQuarantine
                ? await MediaService.UnprotectMediaAsync(media.MediaId, _cts.Token)
                : await MediaService.ProtectMediaAsync(media.MediaId, _cts.Token);

            Snackbar.Add(result.Message, result.Severity);

            if (result.Success)
            {
                media.SafeFromQuarantine = !media.SafeFromQuarantine;
                StateHasChanged();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _cts.Dispose();

            if (_scrollObserver != null)
            {
                try
                {
                    await _scrollObserver.InvokeVoidAsync("dispose");
                    await _scrollObserver.DisposeAsync();
                }
                catch { }
            }

            if (_jsModule != null)
            {
                try
                {
                    await _jsModule.DisposeAsync();
                }
                catch { }
            }

            _dotnetRef?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
