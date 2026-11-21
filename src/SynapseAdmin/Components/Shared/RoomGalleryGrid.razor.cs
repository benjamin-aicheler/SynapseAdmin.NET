using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;
using SynapseAdmin.Extensions;
using SynapseAdmin.Infrastructure.Helpers;
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
        [Inject] public ILogger<RoomGalleryGrid> Logger { get; set; } = null!;

        [Parameter] public string RoomId { get; set; } = string.Empty;
        [Parameter] public RoomMediaViewModel? Media { get; set; }

        private ElementReference _sentinelElement;
        private IJSObjectReference? _jsModule;
        private IJSObjectReference? _scrollObserver;
        private DotNetObjectReference<RoomGalleryGrid>? _dotnetRef;

        private bool _isLoadingInitial = true;
        private bool _isBatchLoading;
        private bool _hasMoreToLoad;
        private int _batchGeneration;
        private const int PageSize = 20;
        // A search or type filter looks up the metadata of unchecked items in batches of PageSize, at most this many
        // batches per load; "Check more" or the next scroll continues (#523).
        private const int MaxLookupBatchesPerLoad = 5;
        private const string FilterAll = "all";
        private const string SourceFilterLocal = "local";
        private const string SourceFilterRemote = "remote";
        private const string TypeFilterImage = "image";
        private const string TypeFilterVideo = "video";
        private const string TypeFilterAudio = "audio";
        private const string TypeFilterFile = "file";

        private string _searchTerm = string.Empty;
        private string _sourceFilter = SourceFilterLocal; // all, local, remote
        private string _typeFilter = FilterAll; // all, image, video, audio, file

        private readonly List<RoomMediaItemViewModel> _allMedia = [];
        // The items of the selected source, in room order. The filter checks them in this order: `_classifyCursor`
        // is the first unchecked one, `_filteredMedia` holds the matches found so far, `_displayedMedia` the shown
        // ones (#523).
        private List<RoomMediaItemViewModel> _sourceMedia = [];
        private int _classifyCursor;
        private List<RoomMediaItemViewModel> _filteredMedia = [];
        private List<RoomMediaItemViewModel> _displayedMedia = [];
        // Checked items that a type filter hides because their lookup found no type (for example uncached remote media).
        private int _unknownTypeCount;
        // The last load stopped at MaxLookupBatchesPerLoad with items left to check.
        private bool _lookupCapReached;
        private readonly HashSet<string> _loadedMetadataMxcs = [];
        private readonly CancellationTokenSource _cts = new();
        private bool _hasLoaded;
        private string? _loadedRoomId;
        private RoomMediaViewModel? _loadedMedia;

        protected override async Task OnParametersSetAsync()
        {
            if (!_hasLoaded || _loadedRoomId != RoomId || !ReferenceEquals(_loadedMedia, Media))
            {
                _hasLoaded = true;
                _loadedRoomId = RoomId;
                _loadedMedia = Media;
                await LoadRoomMedia();
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
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
            catch (Exception ex) when (ex is JSDisconnectedException or OperationCanceledException)
            {
                Logger.LogDebug(ex, "Infinite scroll JS interop setup cancelled or circuit disconnected");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error setting up infinite scroll JS interop");
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
                else
                {
                    Snackbar.ShowResult(roomResult);
                }
            }

            if (mediaModel != null)
            {
                // A room can reference the same MXC URI several times (issue #384): show each once.
                var seen = new HashSet<string>(StringComparer.Ordinal);
                AddUnique(mediaModel.Local, isLocal: true, seen);
                AddUnique(mediaModel.Remote, isLocal: false, seen);
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

        /// <summary>
        /// Adds the items whose MXC URI is not in <paramref name="seen"/> yet (skipping items without one) and marks
        /// them local or remote.
        /// </summary>
        private void AddUnique(IEnumerable<RoomMediaItemViewModel>? items, bool isLocal, HashSet<string> seen)
        {
            if (items == null) return;
            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item.MediaId) || !seen.Add(item.MediaId)) continue;
                item.IsLocal = isLocal;
                _allMedia.Add(item);
            }
        }

        private void ApplyFiltersAndResetDisplay()
        {
            _batchGeneration++;
            _isBatchLoading = false;

            IEnumerable<RoomMediaItemViewModel> query = _allMedia;

            if (_sourceFilter == SourceFilterLocal)
                query = query.Where(x => x.IsLocal);
            else if (_sourceFilter == SourceFilterRemote)
                query = query.Where(x => !x.IsLocal);

            _sourceMedia = query.ToList();
            _classifyCursor = 0;
            _filteredMedia = [];
            _displayedMedia = [];
            _unknownTypeCount = 0;
            _lookupCapReached = false;

            // Without a search or type filter this checks every item at once, as before #523.
            ClassifyKnownItems();
            UpdateHasMoreToLoad();
        }

        /// <summary>How an item relates to the active search and type filters.</summary>
        private enum FilterMatch
        {
            Match,
            NoMatch,
            /// <summary>The filters need metadata the item hasn't been looked up for yet.</summary>
            Unknown,
            /// <summary>Looked up, but its type is still unknown: hidden by a type filter and counted.</summary>
            UnknownType
        }

        private FilterMatch Classify(RoomMediaItemViewModel item)
        {
            var lookedUp = _loadedMetadataMxcs.Contains(item.MediaId);

            var search = FilterMatch.Match;
            if (!string.IsNullOrWhiteSpace(_searchTerm))
            {
                var term = _searchTerm.Trim();
                if (!item.MediaId.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    if (item.UploadName != null)
                        search = item.UploadName.Contains(term, StringComparison.OrdinalIgnoreCase) ? FilterMatch.Match : FilterMatch.NoMatch;
                    else
                        search = lookedUp ? FilterMatch.NoMatch : FilterMatch.Unknown;
                }
            }
            if (search == FilterMatch.NoMatch) return FilterMatch.NoMatch;

            var type = FilterMatch.Match;
            if (_typeFilter != FilterAll)
            {
                if (string.IsNullOrEmpty(item.MediaType))
                    type = lookedUp ? FilterMatch.UnknownType : FilterMatch.Unknown;
                else if (!MatchesTypeFilter(item.MediaType))
                    type = FilterMatch.NoMatch;
            }
            if (type == FilterMatch.NoMatch) return FilterMatch.NoMatch;

            if (search == FilterMatch.Unknown || type == FilterMatch.Unknown) return FilterMatch.Unknown;
            return type;
        }

        /// <summary>Whether a known MIME type passes the type filter; Files means a known type that is none of the others.</summary>
        private bool MatchesTypeFilter(string mediaType) => _typeFilter switch
        {
            TypeFilterImage => IsImageType(mediaType),
            TypeFilterVideo => IsVideoType(mediaType),
            TypeFilterAudio => IsAudioType(mediaType),
            TypeFilterFile => !IsImageType(mediaType) && !IsVideoType(mediaType) && !IsAudioType(mediaType),
            _ => true
        };

        /// <summary>Checks items from the cursor until one needs a lookup, recording matches and unknown types.</summary>
        private void ClassifyKnownItems()
        {
            while (_classifyCursor < _sourceMedia.Count)
            {
                var result = Classify(_sourceMedia[_classifyCursor]);
                if (result == FilterMatch.Unknown) return;
                if (result == FilterMatch.Match) _filteredMedia.Add(_sourceMedia[_classifyCursor]);
                else if (result == FilterMatch.UnknownType) _unknownTypeCount++;
                _classifyCursor++;
            }
        }

        /// <summary>A search or type filter still has items to check, so its result is not complete yet.</summary>
        private bool IsChecking => _classifyCursor < _sourceMedia.Count;

        /// <summary>Continues checking after the lookup limit stopped the last load (#523).</summary>
        private Task CheckMore() => LoadNextBatch();

        private void UpdateHasMoreToLoad() =>
            _hasMoreToLoad = _displayedMedia.Count < _filteredMedia.Count || _classifyCursor < _sourceMedia.Count;

        private async Task LoadNextBatch()
        {
            if (_isBatchLoading || !_hasMoreToLoad) return;

            var currentGen = _batchGeneration;
            _isBatchLoading = true;
            _lookupCapReached = false;
            try
            {
                // Look up unchecked items until one more page of matches is ready, all items are checked, or the
                // batch limit is reached (#523).
                var lookupBatches = 0;
                ClassifyKnownItems();
                while (_filteredMedia.Count < _displayedMedia.Count + PageSize && _classifyCursor < _sourceMedia.Count)
                {
                    if (lookupBatches == MaxLookupBatchesPerLoad)
                    {
                        _lookupCapReached = true;
                        break;
                    }

                    var unknown = new List<string>();
                    for (var i = _classifyCursor; i < _sourceMedia.Count && unknown.Count < PageSize; i++)
                    {
                        if (Classify(_sourceMedia[i]) == FilterMatch.Unknown) unknown.Add(_sourceMedia[i].MediaId);
                    }
                    lookupBatches++;
                    StateHasChanged();

                    var looked = await LookUpMetadataAsync(unknown);
                    // Discard the result of a filter that is no longer active; its metadata is kept.
                    if (currentGen != _batchGeneration) return;
                    if (!looked) break;
                    ClassifyKnownItems();
                }

                var nextItems = _filteredMedia.Skip(_displayedMedia.Count).Take(PageSize).ToList();
                _displayedMedia.AddRange(nextItems);
                UpdateHasMoreToLoad();
                StateHasChanged();

                // Shown items may still lack metadata (all of them without a filter, URI matches with a search).
                var unhydrated = nextItems
                    .Select(x => x.MediaId)
                    .Where(id => !_loadedMetadataMxcs.Contains(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (unhydrated.Count > 0 && await LookUpMetadataAsync(unhydrated))
                {
                    StateHasChanged();
                }
            }
            finally
            {
                if (currentGen == _batchGeneration)
                {
                    _isBatchLoading = false;
                    // Also after a load started from JS (the sentinel), which renders nothing by itself: the status
                    // and the "Check more" button depend on _isBatchLoading.
                    StateHasChanged();
                }
            }
        }

        /// <summary>
        /// Looks up the metadata of <paramref name="mxcUris"/> and copies it into every matching item. Every requested
        /// URI counts as looked up afterwards, also when nothing was found, so it is not looked up again. Returns false
        /// (after reporting the failure) if the lookup failed.
        /// </summary>
        private async Task<bool> LookUpMetadataAsync(List<string> mxcUris)
        {
            var metaResult = await RoomService.GetMediaMetadataBatchAsync(mxcUris, _cts.Token);
            if (!metaResult.Success || metaResult.Data == null)
            {
                Snackbar.ShowFailure(metaResult);
                return false;
            }

            // Tolerate repeated result IDs: the first result for an MXC URI wins.
            var metaDict = new Dictionary<string, RoomMediaItemViewModel>(StringComparer.Ordinal);
            foreach (var meta in metaResult.Data)
            {
                if (!string.IsNullOrEmpty(meta.MediaId)) metaDict.TryAdd(meta.MediaId, meta);
            }
            foreach (var item in _allMedia)
            {
                if (metaDict.TryGetValue(item.MediaId, out var metaItem))
                {
                    item.CopyMetadataFrom(metaItem);
                }
            }
            _loadedMetadataMxcs.UnionWith(mxcUris);
            return true;
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
            _sourceFilter = string.IsNullOrEmpty(val) ? FilterAll : val;
            ApplyFiltersAndResetDisplay();
            await LoadNextBatch();
        }

        private async Task OnTypeFilterChanged(string val)
        {
            _typeFilter = string.IsNullOrEmpty(val) ? FilterAll : val;
            ApplyFiltersAndResetDisplay();
            await LoadNextBatch();
        }

        private static string GetPreviewUrl(RoomMediaItemViewModel item)
        {
            return MediaPreviewLauncher.BuildPreviewUrl(item.MediaId, item.MediaType);
        }

        private static bool IsImageType(string? mimeType)
        {
            if (string.IsNullOrEmpty(mimeType)) return false;
            return MediaHelper.IsImage(mimeType, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsVideoType(string? mimeType)
        {
            if (string.IsNullOrEmpty(mimeType)) return false;
            return MediaHelper.IsVideo(mimeType, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAudioType(string? mimeType)
        {
            if (string.IsNullOrEmpty(mimeType)) return false;
            return MediaHelper.IsAudio(mimeType, StringComparison.OrdinalIgnoreCase);
        }

        private static string GetFileIcon(string? mimeType)
        {
            if (string.IsNullOrEmpty(mimeType)) return Icons.Material.Filled.InsertDriveFile;
            if (mimeType.Contains("pdf")) return Icons.Material.Filled.PictureAsPdf;
            if (mimeType.Contains("zip") || mimeType.Contains("tar") || mimeType.Contains("archive")) return Icons.Material.Filled.FolderZip;
            if (mimeType.Contains("text") || mimeType.Contains("json")) return Icons.Material.Filled.Description;
            return Icons.Material.Filled.InsertDriveFile;
        }

        private async Task ShowPreview(RoomMediaItemViewModel media)
        {
            await DialogService.ShowMediaPreviewAsync(media.MediaId, media.UploadName, media.MediaId, media.MediaType, L["MediaPreview"]);
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
                Snackbar.ShowResult(result);
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
                Snackbar.ShowResult(result);
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
                Snackbar.ShowResult(result);
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

            Snackbar.ShowResult(result);

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
                catch (Exception ex)
                {
                    Logger.LogDebug(ex, "Infinite scroll observer disposal failed (circuit likely gone)");
                }
            }

            if (_jsModule != null)
            {
                try
                {
                    await _jsModule.DisposeAsync();
                }
                catch (Exception ex)
                {
                    Logger.LogDebug(ex, "Gallery JS module disposal failed (circuit likely gone)");
                }
            }

            _dotnetRef?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
