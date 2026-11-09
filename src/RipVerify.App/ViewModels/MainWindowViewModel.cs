using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RipVerify.Core.Abstractions;
using RipVerify.Core.Models;
using RipVerify.Core.Services;
using Serilog;

namespace RipVerify.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly ILocalizationService _localizationService;
    private readonly ISettingsService _settingsService;
    private readonly IFileValidator _fileValidator;
    private readonly IBatchProcessor _batchProcessor;
    private readonly AlbumDetailViewModel _albumDetailViewModel;

    private static readonly HashSet<string> SupportedAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".flac",
        ".wav",
        ".ape",
        ".wv"
    };

    private CancellationTokenSource? _batchCts;
    private Task? _batchRunTask;
    private readonly Dictionary<string, CancellationTokenSource> _itemCancellationTokens =
        new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private string _title = "RipVerify";

    [ObservableProperty]
    private string _dragDropHint = string.Empty;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private string _dropZoneTitle = string.Empty;

    [ObservableProperty]
    private string _dropZoneSubtitle = string.Empty;

    [ObservableProperty]
    private string _queueTitle = string.Empty;

    [ObservableProperty]
    private string _queueEmptyText = string.Empty;

    [ObservableProperty]
    private bool _hasItems;

    [ObservableProperty]
    private string _itemsReceivedText = string.Empty;

    [ObservableProperty]
    private string _openSettingsText = string.Empty;

    [ObservableProperty]
    private string _degradedModeText = string.Empty;

    [ObservableProperty]
    private string _cancelBatchText = string.Empty;

    [ObservableProperty]
    private string _startBatchText = string.Empty;

    [ObservableProperty]
    private string _detailsText = string.Empty;

    [ObservableProperty]
    private string _clearQueueText = string.Empty;

    [ObservableProperty]
    private string _clearQueueToolTip = string.Empty;

    [ObservableProperty]
    private string _removeItemText = string.Empty;

    [ObservableProperty]
    private string _removeItemToolTip = string.Empty;

    [ObservableProperty]
    private bool _canEditQueue;

    [ObservableProperty]
    private string _openFolderText = string.Empty;

    [ObservableProperty]
    private string _batchListAutomationName = string.Empty;

    [ObservableProperty]
    private string _openFolderToolTip = string.Empty;

    [ObservableProperty]
    private string _settingsToolTip = string.Empty;

    [ObservableProperty]
    private string _startBatchToolTip = string.Empty;

    [ObservableProperty]
    private string _cancelBatchToolTip = string.Empty;

    [ObservableProperty]
    private string _detailsToolTip = string.Empty;

    [ObservableProperty]
    private string _verifyToolTip = string.Empty;

    [ObservableProperty]
    private string _backToolTip = string.Empty;

    [ObservableProperty]
    private bool _isDegradedMode;

    [ObservableProperty]
    private ObservableCollection<BatchItemViewModel> _items = new();

    [ObservableProperty]
    private bool _isBatchRunning;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private int _batchTotal;

    [ObservableProperty]
    private int _batchCompleted;

    [ObservableProperty]
    private int _batchFailed;

    [ObservableProperty]
    private int _batchCancelled;

    [ObservableProperty]
    private AlbumDetailViewModel? _detail;

    [ObservableProperty]
    private bool _isDetailVisible;

    [ObservableProperty]
    private BatchItemViewModel? _selectedItem;

    partial void OnIsBatchRunningChanged(bool value) => RefreshQueueState();

    public MainWindowViewModel(
        ILocalizationService localizationService,
        ISettingsService settingsService,
        IFileValidator fileValidator,
        IBatchProcessor batchProcessor,
        AlbumDetailViewModel albumDetailViewModel)
    {
        _localizationService = localizationService;
        _settingsService = settingsService;
        _fileValidator = fileValidator;
        _batchProcessor = batchProcessor;
        _albumDetailViewModel = albumDetailViewModel;

        _albumDetailViewModel.CloseRequested += (_, _) => IsDetailVisible = false;

        UpdateStrings();
        _localizationService.LanguageChanged += (_, _) => UpdateStrings();
    }

    public void UpdateStrings()
    {
        Title = _localizationService.GetString("AppTitle");
        DragDropHint = _localizationService.GetString("MainDragDropHint");
        Subtitle = _localizationService.GetString("MainSubtitle");
        DropZoneTitle = _localizationService.GetString("MainDropZoneTitle");
        DropZoneSubtitle = _localizationService.GetString("MainDropZoneSubtitle");
        QueueTitle = _localizationService.GetString("MainQueueTitle");
        QueueEmptyText = _localizationService.GetString("MainQueueEmpty");
        OpenSettingsText = _localizationService.GetString("MainOpenSettings");
        DegradedModeText = _localizationService.GetString("MainDegradedMode");
        CancelBatchText = _localizationService.GetString("Batch_Cancel");
        StartBatchText = _localizationService.GetString("Batch_Start");
        DetailsText = _localizationService.GetString("Batch_Details");
        ClearQueueText = _localizationService.GetString("Batch_ClearAll");
        ClearQueueToolTip = _localizationService.GetString("Tooltip_ClearQueue");
        RemoveItemText = _localizationService.GetString("Batch_Remove");
        RemoveItemToolTip = _localizationService.GetString("Tooltip_RemoveItem");
        OpenFolderText = _localizationService.GetString("Main_AddItems");
        BatchListAutomationName = _localizationService.GetString("Accessibility_BatchList");
        OpenFolderToolTip = _localizationService.GetString("Tooltip_OpenFolder");
        SettingsToolTip = _localizationService.GetString("Tooltip_OpenSettings");
        StartBatchToolTip = _localizationService.GetString("Tooltip_StartBatch");
        CancelBatchToolTip = _localizationService.GetString("Tooltip_CancelBatch");
        DetailsToolTip = _localizationService.GetString("Tooltip_Details");
        VerifyToolTip = _localizationService.GetString("Tooltip_Verify");
        BackToolTip = _localizationService.GetString("Tooltip_Back");

        foreach (var item in Items)
        {
            item.DetailsText = DetailsText;
            item.DetailsToolTip = DetailsToolTip;
            item.RemoveText = RemoveItemText;
            item.RemoveToolTip = RemoveItemToolTip;
            item.StateText = _localizationService.GetString($"Batch_State_{item.State}");
            item.ResultStatusText = item.Result is null
                ? string.Empty
                : FormatResultStatus(item.Result);
        }

        IsDegradedMode = !_fileValidator.IsCueToolsAvailable(_settingsService.Current.CueToolsPath);
        if (!IsDegradedMode)
        {
            DegradedModeText = string.Empty;
        }
    }

    private string FormatResultStatus(VerificationResult result)
    {
        var text = _localizationService.GetString($"Status_{result.Status}");
        return result.Status == VerificationStatus.VerifiedWithWarnings
            ? $"⚠ {text}"
            : text;
    }

    public void AddDroppedPaths(IReadOnlyList<string> paths)
    {
        var added = 0;

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var isFolder = Directory.Exists(path);
            var isAudioFile = File.Exists(path) && SupportedAudioExtensions.Contains(Path.GetExtension(path));
            if (!isFolder && !isAudioFile)
            {
                continue;
            }

            if (Items.Any(i => string.Equals(i.InputPath, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var displayName = isFolder
                ? new DirectoryInfo(path).Name
                : Path.GetFileNameWithoutExtension(path);

            Items.Add(new BatchItemViewModel
            {
                InputPath = path,
                DisplayName = displayName,
                DetailsText = DetailsText,
                DetailsToolTip = DetailsToolTip,
                RemoveText = RemoveItemText,
                RemoveToolTip = RemoveItemToolTip,
                IsRemovable = !IsBatchRunning,
                StateText = _localizationService.GetString("Batch_State_Pending"),
                OpenDetailsCommand = OpenDetailCommand,
                RemoveCommand = RemoveItemCommand
            });

            added++;
        }

        RefreshQueueState();

        if (added > 0)
        {
            OnItemsDropped(added);
        }
    }

    public void OnItemsDropped(int count)
    {
        ItemsReceivedText = string.Format(CultureInfo.CurrentCulture, _localizationService.GetString("MainItemsReceived"), count);
    }

    [RelayCommand(CanExecute = nameof(CanClearQueue))]
    private void ClearQueue()
    {
        if (IsBatchRunning)
        {
            return;
        }

        Items.Clear();
        SelectedItem = null;
        ItemsReceivedText = string.Empty;
        StatusText = string.Empty;
        BatchTotal = 0;
        BatchCompleted = 0;
        BatchFailed = 0;
        BatchCancelled = 0;
        RefreshQueueState();
    }

    private bool CanClearQueue() => !IsBatchRunning && Items.Count > 0;

    [RelayCommand(CanExecute = nameof(CanRemoveItem))]
    private void RemoveItem(BatchItemViewModel? item)
    {
        if (item is null || item.IsCancelling || !Items.Contains(item))
        {
            return;
        }

        if (!IsBatchRunning || item.State is BatchItemState.Completed or BatchItemState.Failed or BatchItemState.Cancelled)
        {
            RemoveItemFromList(item);
            RefreshQueueState();
            return;
        }

        if (item.State == BatchItemState.Pending)
        {
            if (_itemCancellationTokens.TryGetValue(item.InputPath, out var pendingCts))
            {
                pendingCts.Cancel();
            }

            RemoveItemFromList(item);
            RefreshQueueState();
            return;
        }

        if (_itemCancellationTokens.TryGetValue(item.InputPath, out var activeCts))
        {
            item.IsCancelling = true;
            item.IsRemovable = false;
            item.StateText = _localizationService.GetString("Batch_State_Cancelling");
            activeCts.Cancel();
            RefreshQueueState();
        }
    }

    private bool CanRemoveItem(BatchItemViewModel? item)
        => item is not null && Items.Contains(item) && !item.IsCancelling;

    private void RemoveItemFromList(BatchItemViewModel item)
    {
        if (!Items.Remove(item))
        {
            return;
        }

        if (ReferenceEquals(SelectedItem, item))
        {
            SelectedItem = null;
        }

        if (Items.Count == 0)
        {
            ItemsReceivedText = string.Empty;
        }
    }

    private bool CanStartBatch()
        => !IsBatchRunning && Items.Any(item => item.State != BatchItemState.Completed);

    private void RefreshQueueState()
    {
        HasItems = Items.Count > 0;
        CanEditQueue = HasItems && !IsBatchRunning;

        foreach (var item in Items)
        {
            item.IsRemovable = !item.IsCancelling;
        }

        StartBatchCommand.NotifyCanExecuteChanged();
        ClearQueueCommand.NotifyCanExecuteChanged();
        RemoveItemCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void OpenDetail(BatchItemViewModel? item)
    {
        if (item?.Result is null)
        {
            return;
        }

        _albumDetailViewModel.Load(
            item.Result,
            item.Album ?? new AlbumMetadata(),
            item.CueSheet ?? new CueSheet(),
            item.CueWarnings);
        Detail = _albumDetailViewModel;
        IsDetailVisible = true;
    }

    [RelayCommand(CanExecute = nameof(CanStartBatch))]
    public async Task StartBatchAsync()
    {
        var itemsToProcess = Items
            .Where(item => item.State != BatchItemState.Completed)
            .ToList();

        if (IsBatchRunning || itemsToProcess.Count == 0)
        {
            return;
        }

        foreach (var item in Items)
        {
            item.IsCancelling = false;
        }

        _itemCancellationTokens.Clear();
        foreach (var item in itemsToProcess)
        {
            _itemCancellationTokens[item.InputPath] = new CancellationTokenSource();
        }

        var itemTokens = _itemCancellationTokens.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Token,
            StringComparer.OrdinalIgnoreCase);

        IsBatchRunning = true;
        StatusText = _localizationService.GetString("Batch_Running");
        BatchCompleted = 0;
        BatchFailed = 0;
        BatchCancelled = 0;
        BatchTotal = itemsToProcess.Count;

        var inputs = itemsToProcess
            .Select(item => new ScanInput { Paths = new List<string> { item.InputPath } })
            .ToList();
        var progress = new Progress<BatchProgress>(p =>
        {
            BatchTotal = p.Total;
            BatchCompleted = p.Completed;
            BatchFailed = p.Failed;
            BatchCancelled = p.Cancelled;
            StatusText = string.Format(CultureInfo.CurrentCulture, _localizationService.GetString("Batch_Summary"), p.Completed, p.Failed, p.Cancelled);

            var toRemove = new List<BatchItemViewModel>();
            foreach (var itemViewModel in Items)
            {
                var batchItem = p.Items.FirstOrDefault(i => i.InputPath == itemViewModel.InputPath);
                if (batchItem != null)
                {
                    itemViewModel.State = batchItem.State;
                    itemViewModel.StateText = itemViewModel.IsCancelling &&
                                              batchItem.State is not (BatchItemState.Completed or BatchItemState.Failed or BatchItemState.Cancelled)
                        ? _localizationService.GetString("Batch_State_Cancelling")
                        : _localizationService.GetString($"Batch_State_{batchItem.State}");
                    itemViewModel.Result = batchItem.Result;
                    itemViewModel.ResultStatusText = batchItem.Result is null
                        ? string.Empty
                        : FormatResultStatus(batchItem.Result);
                    itemViewModel.Album = batchItem.Scan?.Album;
                    itemViewModel.CueSheet = batchItem.CueFix?.Cue;
                    itemViewModel.CueWarnings = batchItem.CueFix?.Warnings ?? Array.Empty<string>();
                    itemViewModel.SetErrors(batchItem.Errors);

                    if (itemViewModel.IsCancelling &&
                        batchItem.State is BatchItemState.Completed or BatchItemState.Failed or BatchItemState.Cancelled)
                    {
                        toRemove.Add(itemViewModel);
                    }
                }
            }

            foreach (var item in toRemove)
            {
                RemoveItemFromList(item);
            }

            if (toRemove.Count > 0)
            {
                RefreshQueueState();
            }
        });

        _batchCts = new CancellationTokenSource();
        var batchToken = _batchCts.Token;
        var runTask = Task.Run(
            () => _batchProcessor.RunAsync(inputs, progress, batchToken, itemTokens),
            batchToken);
        _batchRunTask = runTask;

        try
        {
            await runTask;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_batchRunTask, runTask))
            {
                _batchRunTask = null;
            }

            foreach (var cts in _itemCancellationTokens.Values)
            {
                cts.Dispose();
            }
            _itemCancellationTokens.Clear();

            var staleCancellations = Items.Where(item => item.IsCancelling).ToList();
            foreach (var item in staleCancellations)
            {
                RemoveItemFromList(item);
            }

            foreach (var item in Items)
            {
                item.IsCancelling = false;
            }

            IsBatchRunning = false;
            StatusText = string.Format(CultureInfo.CurrentCulture, _localizationService.GetString("Batch_Summary"), BatchCompleted, BatchFailed, BatchCancelled);
            _batchCts.Dispose();
            _batchCts = null;
            RefreshQueueState();
        }
    }

    [RelayCommand]
    public void CancelBatch()
    {
        _batchCts?.Cancel();
    }

    [RelayCommand]
    private async Task ReverifySelectedAsync()
    {
        if (SelectedItem?.Result is null)
        {
            return;
        }

        OpenDetail(SelectedItem);
        await _albumDetailViewModel.VerifyAgainAsync(CancellationToken.None).ConfigureAwait(false);
    }

    [RelayCommand]
    private void BackToDashboard()
    {
        IsDetailVisible = false;
    }

    public bool HasRunningWork =>
        IsBatchRunning ||
        _batchRunTask is not null ||
        _albumDetailViewModel.IsBusy;

    public async Task StopAsync()
    {
        try
        {
            _batchCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        var batchTask = _batchRunTask;
        var detailStopTask = _albumDetailViewModel.StopAsync();

        if (batchTask is not null)
        {
            await WaitForStopAsync(batchTask, "batch");
        }

        await detailStopTask;
    }

    private static async Task WaitForStopAsync(Task task, string operation)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            Log.Warning("Timed out waiting for {Operation} to stop during shutdown", operation);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed while stopping {Operation} during shutdown", operation);
        }
    }

    public void Dispose()
    {
        try
        {
            _batchCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _batchCts?.Dispose();
        _batchCts = null;
        _albumDetailViewModel.CancelRunningVerification();
        GC.SuppressFinalize(this);
    }
}

public partial class BatchItemViewModel : ViewModelBase
{
    public string InputPath { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    [ObservableProperty]
    private BatchItemState _state;

    [ObservableProperty]
    private VerificationResult? _result;

    [ObservableProperty]
    private bool _hasResult;

    partial void OnResultChanged(VerificationResult? value)
    {
        HasResult = value is not null;
    }

    partial void OnStateChanged(BatchItemState value) =>
        OnPropertyChanged(nameof(CanOpenDetails));

    partial void OnHasResultChanged(bool value) =>
        OnPropertyChanged(nameof(CanOpenDetails));

    public bool CanOpenDetails => HasResult && State == BatchItemState.Completed;

    [ObservableProperty]
    private AlbumMetadata? _album;

    [ObservableProperty]
    private CueSheet? _cueSheet;

    public IReadOnlyList<string> CueWarnings { get; set; } = Array.Empty<string>();

    [ObservableProperty]
    private string _detailsText = string.Empty;

    [ObservableProperty]
    private string _detailsToolTip = string.Empty;

    [ObservableProperty]
    private string _removeText = string.Empty;

    [ObservableProperty]
    private string _removeToolTip = string.Empty;

    [ObservableProperty]
    private bool _isRemovable;

    [ObservableProperty]
    private bool _isCancelling;

    [ObservableProperty]
    private string _stateText = string.Empty;

    [ObservableProperty]
    private string _resultStatusText = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    public IReadOnlyList<string> Errors { get; private set; } = Array.Empty<string>();

    public void SetErrors(IReadOnlyList<string> errors)
    {
        Errors = errors ?? Array.Empty<string>();
        ErrorMessage = string.Join(Environment.NewLine, Errors);
        HasError = Errors.Count > 0;
    }

    public IRelayCommand? OpenDetailsCommand { get; set; }

    public IRelayCommand<BatchItemViewModel?>? RemoveCommand { get; set; }
}
