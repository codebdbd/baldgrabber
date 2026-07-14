using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using BaldGrabber.Models;
using BaldGrabber.Services;
using Microsoft.UI.Dispatching;
using Serilog;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace BaldGrabber.ViewModels;

public partial class MainViewModel : INotifyPropertyChanged
{
    private readonly SettingsService _settingsService;
    private readonly DownloadService _downloadService;
    private readonly Localization _loc;
    private Settings _settings;
    private CancellationTokenSource? _cancellationTokenSource;
    private CancellationTokenSource? _formatCancellationTokenSource;
    private DownloadMode _youtubeMode = DownloadMode.Video;
    private bool _isApplyingSourceRoute;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    private string _url = string.Empty;
    public string Url
    {
        get => _url;
        set
        {
            if (SetProperty(ref _url, value))
            {
                ApplySourceRouting(DownloadService.GetDownloadSource(value));
                UpdateCanDownload();
                if (SelectedMode == DownloadMode.Video && CurrentSource == DownloadSource.YouTube)
                    _ = FetchAvailableFormatsAsync();
            }
        }
    }

    private DownloadSource _currentSource = DownloadSource.Unsupported;
    public DownloadSource CurrentSource
    {
        get => _currentSource;
        private set
        {
            if (SetProperty(ref _currentSource, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAudioQualitySelectionEnabled)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSelectVideoQuality)));
            }
        }
    }

    public bool IsAudioQualitySelectionEnabled => CurrentSource != DownloadSource.SoundCloud;

    private string _outputFolder = string.Empty;
    public string OutputFolder
    {
        get => _outputFolder;
        set { if (SetProperty(ref _outputFolder, value)) UpdateCanDownload(); }
    }

    private DownloadMode _selectedMode = DownloadMode.Video;
    public DownloadMode SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (!_isApplyingSourceRoute &&
                ((CurrentSource == DownloadSource.SoundCloud && value != DownloadMode.Audio) ||
                 (IsExternalVideoSource(CurrentSource) && value != DownloadMode.Video)))
            {
                return;
            }

            if (SetProperty(ref _selectedMode, value))
            {
                UpdateButtonLabel();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSelectVideoQuality)));

                if (!_isApplyingSourceRoute &&
                    CurrentSource is DownloadSource.YouTube or DownloadSource.Unsupported)
                {
                    _youtubeMode = value;
                    _settings.SelectedMode = value.ToString();
                    _ = _settingsService.SaveSettingsAsync(_settings);
                }

                if (value == DownloadMode.Video)
                {
                    if (IsExternalVideoSource(CurrentSource))
                    {
                        SetExternalVideoQuality();
                    }
                    else
                    {
                        ResetVideoQualitySelection();
                        if (CurrentSource == DownloadSource.YouTube)
                            _ = FetchAvailableFormatsAsync();
                    }
                }
                else
                {
                    CancelFormatCheck();
                    IsCheckingFormats = false;
                    FormatsStatus = "";
                }

                UpdateCanDownload();
            }
        }
    }

    private AudioQuality? _selectedAudioQuality;
    public AudioQuality? SelectedAudioQuality
    {
        get => _selectedAudioQuality;
        set
        {
            if (SetProperty(ref _selectedAudioQuality, value))
            {
                if (value != null && value.Id != string.Empty &&
                    CurrentSource is DownloadSource.YouTube or DownloadSource.Unsupported)
                {
                    _settings.SelectedAudioQuality = value.Id;
                    _settings.SelectedQuality = value.Id;
                    _ = _settingsService.SaveSettingsAsync(_settings);
                }

                UpdateCanDownload();
            }
        }
    }

    private VideoQualityOption? _selectedVideoQuality;
    public VideoQualityOption? SelectedVideoQuality
    {
        get => _selectedVideoQuality;
        set
        {
            if (SetProperty(ref _selectedVideoQuality, value))
                UpdateCanDownload();
        }
    }

    private string _timeFrom = string.Empty;
    public string TimeFrom
    {
        get => _timeFrom;
        set
        {
            if (SetProperty(ref _timeFrom, value))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasFragment)));
        }
    }

    private string _timeTo = string.Empty;
    public string TimeTo
    {
        get => _timeTo;
        set
        {
            if (SetProperty(ref _timeTo, value))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasFragment)));
        }
    }

    public bool HasFragment => !string.IsNullOrWhiteSpace(TimeFrom) || !string.IsNullOrWhiteSpace(TimeTo);

    private double _progress;
    public double Progress
    {
        get => _progress;
        set
        {
            if (SetProperty(ref _progress, value))
                ProgressText = $"{value:P0}";
        }
    }

    private string _status = string.Empty;
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    private bool _isIndeterminate;
    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        set => SetProperty(ref _isIndeterminate, value);
    }

    private string _progressText = "0%";
    public string ProgressText
    {
        get => _progressText;
        set => SetProperty(ref _progressText, value);
    }

    private string _downloadSpeed = "";
    public string DownloadSpeed
    {
        get => _downloadSpeed;
        set => SetProperty(ref _downloadSpeed, value);
    }

    private string _timeRemaining = "";
    public string TimeRemaining
    {
        get => _timeRemaining;
        set => SetProperty(ref _timeRemaining, value);
    }

    private bool _isDownloading;
    public bool IsDownloading
    {
        get => _isDownloading;
        set { if (SetProperty(ref _isDownloading, value)) UpdateCanDownload(); }
    }

    private bool _isCheckingFormats;
    public bool IsCheckingFormats
    {
        get => _isCheckingFormats;
        set
        {
            if (SetProperty(ref _isCheckingFormats, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSelectVideoQuality)));
                UpdateCanDownload();
            }
        }
    }

    private bool _hasCheckedVideoFormats;
    private bool HasCheckedVideoFormats
    {
        get => _hasCheckedVideoFormats;
        set
        {
            if (SetProperty(ref _hasCheckedVideoFormats, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSelectVideoQuality)));
                UpdateCanDownload();
            }
        }
    }

    public bool CanSelectVideoQuality =>
        CurrentSource == DownloadSource.YouTube && HasCheckedVideoFormats && !IsCheckingFormats;

    private bool _canDownload;
    public bool CanDownload
    {
        get => _canDownload;
        set => SetProperty(ref _canDownload, value);
    }

    private string? _downloadedFilePath;
    public string? DownloadedFilePath
    {
        get => _downloadedFilePath;
        set => SetProperty(ref _downloadedFilePath, value);
    }

    public ObservableCollection<AudioQuality> AudioQualities { get; } = new();
    private ObservableCollection<VideoQualityOption> _videoQualitiesList = new();
    public ObservableCollection<VideoQualityOption> VideoQualities
    {
        get => _videoQualitiesList;
        set => SetProperty(ref _videoQualitiesList, value);
    }
    private VideoQualityOption VideoPlaceholderQuality { get; } = new() { Id = "", Name = "", Description = "" };
    private VideoQualityOption CheckingFormatsQuality { get; } = new() { Id = "", Name = "", Description = "" };
    private VideoQualityOption ExternalVideoQuality { get; } = new() { Id = "external_best", IsAvailable = true };
    private AudioQuality SoundCloudAudioQuality { get; } = new() { Id = "soundcloud_auto", IsAvailable = true };

    private string _downloadButtonText = "";
    public string DownloadButtonText { get => _downloadButtonText; set => SetProperty(ref _downloadButtonText, value); }

    public ICommand DownloadCommand { get; }
    public ICommand CancelDownloadCommand { get; }
    public ICommand BrowseFolderCommand { get; }
    public ICommand OpenFolderCommand { get; }

    private readonly List<VideoQualityOption> _videoQualities = new()
    {
        new() { Id = "2160p", Name = "MP4 2160p 4K", Description = "Максимальная детализация" },
        new() { Id = "1440p", Name = "MP4 1440p QHD", Description = "Очень высокое качество" },
        new() { Id = "1080p", Name = "MP4 1080p Full HD", Description = "Высокое качество" },
        new() { Id = "720p", Name = "MP4 720p HD", Description = "Хорошее качество" },
        new() { Id = "480p", Name = "MP4 480p SD", Description = "Стандартное качество" },
        new() { Id = "360p", Name = "MP4 360p", Description = "Небольшой размер файла" },
        new() { Id = "240p", Name = "MP4 240p", Description = "Минимальный размер файла" },
    };

    private readonly List<AudioQuality> _audioQualities = new()
    {
        new() { Id = "opus", Name = "Opus", Description = "Оригинал YouTube" },
        new() { Id = "m4a", Name = "M4A", Description = "Без потерь, хорошая совместимость" },
        new() { Id = "mp3_128", Name = "MP3 128 kbps", Description = "Работает везде" },
        new() { Id = "mp3_96", Name = "MP3 96 kbps", Description = "Блоги и подкасты" },
    };

    public MainViewModel()
    {
        _settingsService = new SettingsService();
        _downloadService = new DownloadService();
        _settings = _settingsService.LoadSettings();
        _loc = Localization.Create();

        DownloadCommand = new AsyncRelayCommand(DownloadAsync);
        CancelDownloadCommand = new RelayCommand(CancelDownload);
        BrowseFolderCommand = new AsyncRelayCommand(BrowseFolderAsync);
        OpenFolderCommand = new RelayCommand(OpenFolder);

        Status = _loc.StatusWaiting;
        OutputFolder = _settings.LastFolder ?? string.Empty;
        foreach (var q in _audioQualities)
            AudioQualities.Add(q);

        var savedMode = Enum.TryParse<DownloadMode>(_settings.SelectedMode, out var m) ? m : DownloadMode.Video;
        _selectedMode = savedMode;
        _youtubeMode = savedMode;
        ResetVideoQualitySelection();

        var savedQualityId = !string.IsNullOrEmpty(_settings.SelectedAudioQuality)
            ? _settings.SelectedAudioQuality
            : _settings.SelectedQuality;
        SelectedAudioQuality = AudioQualities.FirstOrDefault(q => q.Id == savedQualityId && q.IsAvailable)
                               ?? AudioQualities.FirstOrDefault(q => q.Id == "m4a")
                               ?? AudioQualities.FirstOrDefault();

        UpdateCanDownload();
        UpdateButtonLabel();
    }

    private void ResetVideoQualitySelection()
    {
        HasCheckedVideoFormats = false;
        VideoPlaceholderQuality.Name = _loc.VideoQualityPlaceholder;
        VideoQualities = new ObservableCollection<VideoQualityOption> { VideoPlaceholderQuality };
        SelectedVideoQuality = VideoPlaceholderQuality;
    }

    private void SetExternalVideoQuality()
    {
        CancelFormatCheck();
        IsCheckingFormats = false;
        FormatsStatus = "";
        ExternalVideoQuality.Name = _loc.AutomaticQualityName;
        ExternalVideoQuality.Description = _loc.QualityBestDesc;
        VideoQualities = new ObservableCollection<VideoQualityOption> { ExternalVideoQuality };
        SelectedVideoQuality = ExternalVideoQuality;
        HasCheckedVideoFormats = true;
    }

    private void SetSoundCloudAudioQuality()
    {
        SoundCloudAudioQuality.Name = _loc.AutomaticQualityName;
        SoundCloudAudioQuality.Description = _loc.QualityBestDesc;
        AudioQualities.Clear();
        AudioQualities.Add(SoundCloudAudioQuality);
        SelectedAudioQuality = SoundCloudAudioQuality;
    }

    private void RestoreYouTubeAudioQualities()
    {
        if (AudioQualities.Count == _audioQualities.Count &&
            AudioQualities.All(item => item.Id != "soundcloud_auto"))
        {
            return;
        }

        AudioQualities.Clear();
        foreach (var quality in _audioQualities)
            AudioQualities.Add(quality);

        SelectedAudioQuality = GetDefaultAudioQuality();
    }

    private void ApplySourceRouting(DownloadSource source)
    {
        if (CurrentSource == source)
            return;

        CurrentSource = source;
        if (source != DownloadSource.YouTube)
        {
            CancelFormatCheck();
            IsCheckingFormats = false;
            FormatsStatus = "";
        }

        _isApplyingSourceRoute = true;
        try
        {
            switch (source)
            {
                case DownloadSource.SoundCloud:
                    CancelFormatCheck();
                    SetSoundCloudAudioQuality();
                    SelectedMode = DownloadMode.Audio;
                    break;

                case DownloadSource.TikTok:
                case DownloadSource.Facebook:
                case DownloadSource.Instagram:
                    RestoreYouTubeAudioQualities();
                    SelectedMode = DownloadMode.Video;
                    SetExternalVideoQuality();
                    break;

                case DownloadSource.YouTube:
                case DownloadSource.Unsupported:
                    RestoreYouTubeAudioQualities();
                    SelectedMode = _youtubeMode;
                    if (_youtubeMode == DownloadMode.Video)
                    {
                        ResetVideoQualitySelection();
                    }
                    break;
            }
        }
        finally
        {
            _isApplyingSourceRoute = false;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAudioQualitySelectionEnabled)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSelectVideoQuality)));
        UpdateCanDownload();
    }

    private static bool IsExternalVideoSource(DownloadSource source) =>
        source is DownloadSource.TikTok or DownloadSource.Facebook or DownloadSource.Instagram;

    private AudioQuality? GetDefaultAudioQuality() =>
        AudioQualities.FirstOrDefault(q => q.Id == (string.IsNullOrEmpty(_settings.SelectedAudioQuality) ? "m4a" : _settings.SelectedAudioQuality) && q.IsAvailable)
        ?? AudioQualities.FirstOrDefault(q => q.IsAvailable);

    private void ShowCheckingFormatsQuality()
    {
        CheckingFormatsQuality.Name = _loc.StatusCheckingFormats;
        VideoQualities = new ObservableCollection<VideoQualityOption> { CheckingFormatsQuality };
        SelectedVideoQuality = CheckingFormatsQuality;
    }

    private void SetCheckedVideoQualities(List<string> availableFormats)
    {
        var available = new HashSet<string>(availableFormats, StringComparer.OrdinalIgnoreCase);
        var newQualities = new ObservableCollection<VideoQualityOption> { VideoPlaceholderQuality };

        foreach (var q in _videoQualities)
        {
            q.IsAvailable = available.Contains(q.Id);
            newQualities.Add(q);
        }

        VideoQualities = newQualities;
        SelectedVideoQuality = VideoPlaceholderQuality;
        HasCheckedVideoFormats = true;
    }

    private void UpdateButtonLabel() => DownloadButtonText = SelectedMode == DownloadMode.Video ? _loc.VideoDownloadButton : _loc.DownloadButton;

    public void ResetState()
    {
        TimeFrom = "";
        TimeTo = "";
        Status = _loc.StatusWaiting;
        Progress = 0;
        ProgressText = "0%";
        DownloadedFilePath = null;
        FormatsStatus = "";
    }

    private string _formatsStatus = "";
    public string FormatsStatus
    {
        get => _formatsStatus;
        set => SetProperty(ref _formatsStatus, value);
    }

    private async Task FetchAvailableFormatsAsync()
    {
        CancelFormatCheck();

        if (string.IsNullOrWhiteSpace(Url) || !DownloadService.IsValidYouTubeUrl(Url))
        {
            ResetVideoQualitySelection();
            FormatsStatus = "";
            IsCheckingFormats = false;
            return;
        }

        if (SelectedMode == DownloadMode.Audio)
        {
            FormatsStatus = "";
            IsCheckingFormats = false;
            return;
        }

        var cancellationTokenSource = new CancellationTokenSource();
        _formatCancellationTokenSource = cancellationTokenSource;

        try
        {
            var ct = cancellationTokenSource.Token;

            HasCheckedVideoFormats = false;
            ShowCheckingFormatsQuality();
            IsCheckingFormats = true;
            FormatsStatus = "";
            await Task.Delay(300, ct);

            var formats = await _downloadService.GetAvailableFormatsAsync(Url, DownloadMode.Video, ct);

            if (!ReferenceEquals(Volatile.Read(ref _formatCancellationTokenSource), cancellationTokenSource) ||
                SelectedMode != DownloadMode.Video)
                return;

            SetCheckedVideoQualities(formats);
            FormatsStatus = formats.Count > 0 ? $"Доступно: {string.Join(", ", formats)}" : "";
        }
        catch (OperationCanceledException) { FormatsStatus = ""; }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to fetch available formats");
            ResetVideoQualitySelection();
            FormatsStatus = "";
        }
        finally
        {
            var ownedCurrentCheck = ReferenceEquals(
                Interlocked.CompareExchange(ref _formatCancellationTokenSource, null, cancellationTokenSource),
                cancellationTokenSource);
            cancellationTokenSource.Dispose();
            if (ownedCurrentCheck)
                IsCheckingFormats = false;
        }
    }

    private void CancelFormatCheck()
    {
        var cancellationTokenSource = Interlocked.Exchange(ref _formatCancellationTokenSource, null);
        try { cancellationTokenSource?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void UpdateCanDownload()
    {
        var hasSelectedQuality = SelectedMode == DownloadMode.Audio
            ? SelectedAudioQuality is { Id.Length: > 0, IsAvailable: true }
            : SelectedVideoQuality is { Id.Length: > 0, IsAvailable: true } && HasCheckedVideoFormats && !IsCheckingFormats;

        CanDownload = DownloadService.IsValidSupportedUrl(Url) &&
                      !string.IsNullOrWhiteSpace(OutputFolder) &&
                      System.IO.Directory.Exists(OutputFolder) &&
                      !IsDownloading &&
                      hasSelectedQuality;
    }

    private async Task BrowseFolderAsync()
    {
        try
        {
            var window = App.MainWindow ?? throw new InvalidOperationException("Главное окно не найдено");
            var hwnd = WindowNative.GetWindowHandle(window);

            var folderPicker = new FolderPicker { SuggestedStartLocation = PickerLocationId.Desktop };
            InitializeWithWindow.Initialize(folderPicker, hwnd);
            folderPicker.FileTypeFilter.Add("*");

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder != null)
            {
                OutputFolder = folder.Path;
                _settings.LastFolder = folder.Path;
                await _settingsService.SaveSettingsAsync(_settings);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Folder selection error");
            Status = _loc.StatusFolderError;
        }
    }

    private async Task DownloadAsync()
    {
        if (IsDownloading) return;

        if (string.IsNullOrWhiteSpace(Url))
        {
            Status = _loc.StatusEnterUrl;
            return;
        }
        var source = Services.DownloadService.GetDownloadSource(Url);
        if (source == DownloadSource.Unsupported)
        {
            Status = _loc.StatusInvalidUrl;
            return;
        }
        if (string.IsNullOrWhiteSpace(OutputFolder) || !System.IO.Directory.Exists(OutputFolder))
        {
            Status = _loc.StatusSelectFolder;
            return;
        }

        try
        {
            IsDownloading = true;
            Status = _loc.StatusDownloading;
            Progress = 0;
            DownloadedFilePath = null;
            FormatsStatus = "";

            if (source == DownloadSource.YouTube)
                _settings.SelectedMode = SelectedMode.ToString();

            if (source == DownloadSource.YouTube &&
                SelectedMode == DownloadMode.Audio &&
                !string.IsNullOrEmpty(SelectedAudioQuality?.Id))
            {
                _settings.SelectedAudioQuality = SelectedAudioQuality.Id;
                _settings.SelectedQuality = SelectedAudioQuality.Id;
                try
                {
                    await _settingsService.SaveSettingsAsync(_settings);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Settings save failed");
                }
            }

            _cancellationTokenSource = new CancellationTokenSource();
            var progress = new Progress<double>(p =>
            {
                if (p < 0)
                {
                    IsIndeterminate = true;
                    Progress = 0;
                    ProgressText = "";
                    DownloadSpeed = "";
                    TimeRemaining = "";
                    Status = p switch
                    {
                        -1 => _loc.StatusConverting,
                        -2 => _loc.StatusMerging,
                        -3 => _loc.StatusCleaning,
                        -4 => _loc.StatusTrimming,
                        -5 => _loc.StatusEmbeddingThumbnail,
                        _ => Status
                    };
                }
                else
                {
                    IsIndeterminate = false;
                    Progress = p;
                }
            });

            var qualityId = SelectedMode == DownloadMode.Audio
                ? SelectedAudioQuality?.Id
                : SelectedVideoQuality?.Id;

            if (string.IsNullOrWhiteSpace(qualityId))
            {
                Status = SelectedMode == DownloadMode.Video ? _loc.StatusSelectVideoQuality : _loc.StatusSelectAudioQuality;
                return;
            }

            var timeFrom = string.IsNullOrWhiteSpace(TimeFrom) ? null : TimeFrom;
            var timeTo = string.IsNullOrWhiteSpace(TimeTo) ? null : TimeTo;

            var isPlaylist = Services.DownloadService.IsPlaylistUrl(Url);
            var isChannel = Services.DownloadService.IsChannelUrl(Url);
            var dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            Action<string, string> onSpeedEta = (speed, eta) =>
            {
                dispatcherQueue?.TryEnqueue(() =>
                {
                    DownloadSpeed = speed;
                    TimeRemaining = eta;
                });
            };

            if (source == DownloadSource.SoundCloud)
            {
                var result = await _downloadService.DownloadSoundCloudAsync(
                    Url, OutputFolder, timeFrom, timeTo, progress,
                    _cancellationTokenSource.Token, onSpeedEta);

                DownloadedFilePath = result.path;
                IsIndeterminate = false;
                ProgressText = "100%";
                DownloadSpeed = "";
                TimeRemaining = "";
                Status = result.isCollection
                    ? string.Format(_loc.StatusPlaylistCompleted, System.IO.Path.GetFileName(result.path))
                    : string.IsNullOrEmpty(result.actualQuality)
                        ? string.Format(_loc.StatusCompleted, result.title)
                        : $"{string.Format(_loc.StatusCompleted, result.title)} ({result.actualQuality})";
                return;
            }

            if (IsExternalVideoSource(source))
            {
                var result = source switch
                {
                    DownloadSource.TikTok => await _downloadService.DownloadTikTokAsync(
                        Url, OutputFolder, timeFrom, timeTo, progress,
                        _cancellationTokenSource.Token, onSpeedEta),
                    DownloadSource.Facebook => await _downloadService.DownloadFacebookAsync(
                        Url, OutputFolder, timeFrom, timeTo, progress,
                        _cancellationTokenSource.Token, onSpeedEta),
                    DownloadSource.Instagram => await _downloadService.DownloadInstagramAsync(
                        Url, OutputFolder, timeFrom, timeTo, progress,
                        _cancellationTokenSource.Token, onSpeedEta),
                    _ => throw new InvalidOperationException("Unsupported external video source")
                };

                DownloadedFilePath = result.filePath;
                IsIndeterminate = false;
                ProgressText = "100%";
                DownloadSpeed = "";
                TimeRemaining = "";
                Status = string.IsNullOrEmpty(result.actualQuality)
                    ? string.Format(_loc.StatusCompleted, result.title)
                    : $"{string.Format(_loc.StatusCompleted, result.title)} ({result.actualQuality})";
                return;
            }

            if (isPlaylist || isChannel)
            {
                var folderPath = await _downloadService.DownloadPlaylistAsync(
                    SelectedMode, Url, qualityId, OutputFolder, progress, _cancellationTokenSource.Token, onSpeedEta);

                DownloadedFilePath = folderPath;
                IsIndeterminate = false;
                ProgressText = "100%";
                DownloadSpeed = "";
                TimeRemaining = "";
                Status = string.Format(_loc.StatusPlaylistCompleted, System.IO.Path.GetFileName(folderPath));
            }
            else
            {
                var (filePath, title, actualQuality) = await _downloadService.DownloadAsync(
                    SelectedMode, Url, qualityId, OutputFolder, timeFrom, timeTo, progress, _cancellationTokenSource.Token, onSpeedEta);

                DownloadedFilePath = filePath;
                IsIndeterminate = false;
                ProgressText = "100%";
                DownloadSpeed = "";
                TimeRemaining = "";
                Status = string.IsNullOrEmpty(actualQuality)
                    ? string.Format(_loc.StatusCompleted, title)
                    : $"{string.Format(_loc.StatusCompleted, title)} ({actualQuality})";
            }
        }
        catch (OperationCanceledException)
        {
            IsIndeterminate = false;
            Status = _loc.StatusCancelled;
            Progress = 0;
            ProgressText = "0%";
        }
        catch (Exception ex)
        {
            IsIndeterminate = false;
            Log.Error(ex, "Download error");
            Status = string.Format(_loc.StatusError, ex.Message);
            Progress = 0;
            ProgressText = "0%";
        }
        finally
        {
            IsIndeterminate = false;
            IsDownloading = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private void CancelDownload()
    {
        try { _cancellationTokenSource?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    public void Shutdown()
    {
        CancelFormatCheck();
        _cancellationTokenSource?.Cancel();
    }

    private void OpenFolder()
    {
        try
        {
            if (!string.IsNullOrEmpty(DownloadedFilePath) && System.IO.File.Exists(DownloadedFilePath))
            {
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{DownloadedFilePath}\"", UseShellExecute = true });
            }
            else if (!string.IsNullOrEmpty(OutputFolder) && System.IO.Directory.Exists(OutputFolder))
            {
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = OutputFolder, UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open folder");
            Status = string.Format(_loc.StatusError, ex.Message);
        }
    }
}

public class Localization
{
    public string AppTitle { get; private set; } = "";
    public string AppSubtitle { get; private set; } = "";
    public string UrlLabel { get; private set; } = "";
    public string UrlPlaceholder { get; private set; } = "";
    public string PasteUrlToolTip { get; private set; } = "";
    public string PasteUrlUnavailableToolTip { get; private set; } = "";
    public string StatusClipboardEmpty { get; private set; } = "";
    public string FolderLabel { get; private set; } = "";
    public string BrowseButton { get; private set; } = "";
    public string QualityLabel { get; private set; } = "";
    public string FragmentLabel { get; private set; } = "";
    public string TimeFromLabel { get; private set; } = "";
    public string TimeToLabel { get; private set; } = "";
    public string TimeFromPlaceholder { get; private set; } = "";
    public string TimeToPlaceholder { get; private set; } = "";
    public string FragmentHint { get; private set; } = "";
    public string DownloadButton { get; private set; } = "";
    public string CancelButton { get; private set; } = "";
    public string SupportAuthor { get; private set; } = "";
    public string OpenFolderButton { get; private set; } = "";
    public string StatusWaiting { get; private set; } = "";
    public string StatusFolderError { get; private set; } = "";
    public string StatusEnterUrl { get; private set; } = "";
    public string StatusInvalidUrl { get; private set; } = "";
    public string StatusPlaylist { get; private set; } = "";
    public string StatusSelectFolder { get; private set; } = "";
    public string StatusSelectVideoQuality { get; private set; } = "";
    public string StatusSelectAudioQuality { get; private set; } = "";
    public string StatusDownloading { get; private set; } = "";
    public string StatusConverting { get; private set; } = "";
    public string StatusMerging { get; private set; } = "";
    public string StatusTrimming { get; private set; } = "";
    public string StatusCleaning { get; private set; } = "";
    public string StatusEmbeddingThumbnail { get; private set; } = "";
    public string StatusCompleted { get; private set; } = "";
    public string EmbedThumbnailLabel { get; private set; } = "";
    public string StatusCancelled { get; private set; } = "";
    public string StatusError { get; private set; } = "";
    public string QualityBestDesc { get; private set; } = "";
    public string AutomaticQualityName { get; private set; } = "";
    public string Quality320Desc { get; private set; } = "";
    public string Quality256Desc { get; private set; } = "";
    public string Quality192Desc { get; private set; } = "";
    public string Quality128Desc { get; private set; } = "";
    public string Quality96Desc { get; private set; } = "";
    public string VideoQualityLabel { get; private set; } = "";
    public string VideoQualityPlaceholder { get; private set; } = "";
    public string VideoDownloadButton { get; private set; } = "";
    public string AudioTab { get; private set; } = "";
    public string VideoTab { get; private set; } = "";
    public string FragmentDialogTitle { get; private set; } = "";
    public string FragmentHintText { get; private set; } = "";
    public string FragmentStartLabel { get; private set; } = "";
    public string FragmentStopLabel { get; private set; } = "";
    public string FragmentFormatHint { get; private set; } = "";
    public string FragmentSaveButton { get; private set; } = "";
    public string FragmentClearButton { get; private set; } = "";
    public string FragmentCancelButton { get; private set; } = "";
    public string FragmentErrorText { get; private set; } = "";
    public string StatusPlaylistCompleted { get; private set; } = "";
    public string StatusCheckingFormats { get; private set; } = "";
    public string TabDisabledTooltip { get; private set; } = "";
    public string TabDisabledToast { get; private set; } = "";

    public static Localization Create()
    {
        var lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return lang switch
        {
            "ru" => CreateRu(),
            "uk" => CreateUk(),
            _ => CreateEn()
        };
    }

    private static Localization CreateEn() => new()
    {
        AppTitle = "BalDGrabber",
        AppSubtitle = "Paste YouTube link and grab it!",
        UrlLabel = "YouTube Link",
        UrlPlaceholder = "Paste a YouTube link",
        PasteUrlToolTip = "Paste from clipboard",
        PasteUrlUnavailableToolTip = "Clipboard contains no supported link",
        StatusClipboardEmpty = "Clipboard contains no supported link",
        FolderLabel = "Save folder",
        BrowseButton = "Select",
        QualityLabel = "Audio quality",
        FragmentLabel = "Audio fragment",
        TimeFromLabel = "Start",
        TimeToLabel = "Stop",
        TimeFromPlaceholder = "0:00",
        TimeToPlaceholder = "end",
        FragmentHint = "Leave empty to download full audio. Format: M:SS or HH:MM:SS",
        DownloadButton = "Download audio",
        CancelButton = "Cancel",
        SupportAuthor = "Tip",
        OpenFolderButton = "Open",
        StatusWaiting = "Waiting",
        StatusFolderError = "Folder selection error",
        StatusEnterUrl = "Enter YouTube link",
        StatusInvalidUrl = "Enter valid YouTube link",
        StatusPlaylist = "Downloading playlist...",
        StatusSelectFolder = "Select save folder",
        StatusSelectVideoQuality = "Select video quality",
        StatusSelectAudioQuality = "Select audio quality",
        StatusDownloading = "Downloading...",
        StatusConverting = "Converting to MP3...",
        StatusMerging = "Merging files...",
        StatusTrimming = "Trimming audio...",
        StatusCleaning = "Cleaning up...",
        StatusEmbeddingThumbnail = "Embedding thumbnail...",
        StatusCompleted = "Completed: {0}",
        StatusCancelled = "Download cancelled",
        StatusError = "Error: {0}",
        QualityBestDesc = "Best available quality (original)",
        AutomaticQualityName = "Automatic",
        Quality320Desc = "Maximum MP3 quality",
        Quality256Desc = "Good balance of quality and size",
        Quality192Desc = "Standard quality",
        Quality128Desc = "Small file size",
        Quality96Desc = "Very small file size",
        VideoQualityLabel = "Video quality",
        VideoQualityPlaceholder = "Select video quality",
        VideoDownloadButton = "Download video",
        EmbedThumbnailLabel = "Embed thumbnail",
        AudioTab = "Audio",
        VideoTab = "Video",
        FragmentDialogTitle = "Fragment",
        FragmentHintText = "Set start, stop or both boundaries.\nEmpty start — from beginning. Empty stop — to end.",
        FragmentStartLabel = "Start",
        FragmentStopLabel = "Stop",
        FragmentFormatHint = "M — minutes, HH — hours, SS — seconds\nExample: 1:30 or 01:02:30",
        FragmentSaveButton = "Save",
        FragmentClearButton = "Clear",
        FragmentCancelButton = "Cancel",
        FragmentErrorText = "Enter time in M:SS or HH:MM:SS format",
        StatusPlaylistCompleted = "Playlist downloaded: {0}",
        StatusCheckingFormats = "Checking available formats...",
        TabDisabledTooltip = "Wait for download to finish",
        TabDisabledToast = "Finish current download first"
    };

    private static Localization CreateRu() => new()
    {
        AppTitle = "BalDGrabber",
        AppSubtitle = "Вставь ссылку YouTube и качай!",
        UrlLabel = "Ссылка на YouTube",
        UrlPlaceholder = "Вставьте ссылку YouTube",
        PasteUrlToolTip = "Вставить из буфера обмена",
        PasteUrlUnavailableToolTip = "В буфере нет поддерживаемой ссылки",
        StatusClipboardEmpty = "В буфере нет поддерживаемой ссылки",
        FolderLabel = "Папка для сохранения",
        BrowseButton = "Выбрать",
        QualityLabel = "Качество аудио",
        FragmentLabel = "Фрагмент аудио",
        TimeFromLabel = "Старт",
        TimeToLabel = "Стоп",
        TimeFromPlaceholder = "0:00",
        TimeToPlaceholder = "конец",
        FragmentHint = "Оставьте пустым для скачивания полного аудио. Формат: M:SS или HH:MM:SS",
        DownloadButton = "Скачать аудио",
        CancelButton = "Отмена",
        SupportAuthor = "Автору",
        OpenFolderButton = "Открыть",
        StatusWaiting = "Ожидание",
        StatusFolderError = "Ошибка при выборе папки",
        StatusEnterUrl = "Введите ссылку на YouTube",
        StatusInvalidUrl = "Введите корректную ссылку на YouTube",
        StatusPlaylist = "Загрузка плейлиста...",
        StatusSelectFolder = "Выберите папку для сохранения",
        StatusSelectVideoQuality = "Выберите доступное качество видео",
        StatusSelectAudioQuality = "Выберите качество аудио",
        StatusDownloading = "Загрузка...",
        StatusConverting = "Конвертация в MP3...",
        StatusMerging = "Склейка файлов...",
        StatusTrimming = "Обрезка аудио...",
        StatusCleaning = "Очистка...",
        StatusEmbeddingThumbnail = "Встраивание обложки...",
        StatusCompleted = "Завершено: {0}",
        StatusCancelled = "Загрузка отменена",
        StatusError = "Ошибка: {0}",
        QualityBestDesc = "Лучшее доступное качество",
        AutomaticQualityName = "Автоматически",
        Quality320Desc = "Максимальное качество MP3",
        Quality256Desc = "Хороший баланс качества и размера",
        Quality192Desc = "Стандартное качество",
        Quality128Desc = "Маленький размер файла",
        Quality96Desc = "Очень маленький размер файла",
        VideoQualityLabel = "Качество видео",
        VideoQualityPlaceholder = "Выберите качество видео",
        VideoDownloadButton = "Скачать видео",
        EmbedThumbnailLabel = "Встроить обложку",
        AudioTab = "Аудио",
        VideoTab = "Видео",
        FragmentDialogTitle = "Фрагмент",
        FragmentHintText = "Укажите старт, стоп или обе границы.\nПустой старт — с начала. Пустой стоп — до конца.",
        FragmentStartLabel = "Старт",
        FragmentStopLabel = "Стоп",
        FragmentFormatHint = "M — минуты, HH — часы, SS — секунды\nНапример: 1:30 или 01:02:30",
        FragmentSaveButton = "Сохранить",
        FragmentClearButton = "Очистить",
        FragmentCancelButton = "Отмена",
        FragmentErrorText = "Введите время в формате M:SS или HH:MM:SS",
        StatusPlaylistCompleted = "Плейлист скачан: {0}",
        StatusCheckingFormats = "Проверка доступных форматов...",
        TabDisabledTooltip = "Дождитесь завершения загрузки",
        TabDisabledToast = "Сначала докачайте текущий трек"
    };

    private static Localization CreateUk() => new()
    {
        AppTitle = "BalDGrabber",
        AppSubtitle = "Встав посилання YouTube і качай!",
        UrlLabel = "Посилання на YouTube",
        UrlPlaceholder = "Вставте посилання YouTube",
        PasteUrlToolTip = "Вставити з буфера обміну",
        PasteUrlUnavailableToolTip = "У буфері немає підтримуваного посилання",
        StatusClipboardEmpty = "У буфері немає підтримуваного посилання",
        FolderLabel = "Папка для збереження",
        BrowseButton = "Обрати",
        QualityLabel = "Якість аудіо",
        FragmentLabel = "Фрагмент аудіо",
        TimeFromLabel = "Старт",
        TimeToLabel = "Стоп",
        TimeFromPlaceholder = "0:00",
        TimeToPlaceholder = "кінець",
        FragmentHint = "Залиште порожнім для завантаження повного аудіо. Формат: M:SS або HH:MM:SS",
        DownloadButton = "Завантажити аудіо",
        CancelButton = "Скасувати",
        SupportAuthor = "Автору",
        OpenFolderButton = "Відкрити",
        StatusWaiting = "Очікування",
        StatusFolderError = "Помилка при виборі папки",
        StatusEnterUrl = "Введіть посилання на YouTube",
        StatusInvalidUrl = "Введіть коректне посилання на YouTube",
        StatusPlaylist = "Завантаження плейлиста...",
        StatusSelectFolder = "Оберіть папку для збереження",
        StatusSelectVideoQuality = "Оберіть якість відео",
        StatusSelectAudioQuality = "Оберіть якість аудіо",
        StatusDownloading = "Завантаження...",
        StatusConverting = "Конвертація в MP3...",
        StatusMerging = "З'єднання файлів...",
        StatusTrimming = "Обрізка аудіо...",
        StatusCleaning = "Очищення...",
        StatusEmbeddingThumbnail = "Вбудовування обкладинки...",
        StatusCompleted = "Завершено: {0}",
        StatusCancelled = "Завантаження скасовано",
        StatusError = "Помилка: {0}",
        QualityBestDesc = "Найкраща доступна якість (оригінал)",
        AutomaticQualityName = "Автоматично",
        Quality320Desc = "Максимальна якість MP3",
        Quality256Desc = "Гарний баланс якості та розміру",
        Quality192Desc = "Стандартна якість",
        Quality128Desc = "Малий розмір файлу",
        Quality96Desc = "Дуже малий розмір файлу",
        VideoQualityLabel = "Якість відео",
        VideoQualityPlaceholder = "Оберіть якість відео",
        VideoDownloadButton = "Завантажити відео",
        EmbedThumbnailLabel = "Вбудувати обкладинку",
        AudioTab = "Аудіо",
        VideoTab = "Відео",
        FragmentDialogTitle = "Фрагмент",
        FragmentHintText = "Вкажіть старт, стоп або обидві межі.\nПорожній старт — з початку. Порожній стоп — до кінця.",
        FragmentStartLabel = "Старт",
        FragmentStopLabel = "Стоп",
        FragmentFormatHint = "M — хвилини, HH — години, SS — секунди\nНаприклад: 1:30 або 01:02:30",
        FragmentSaveButton = "Зберегти",
        FragmentClearButton = "Очистити",
        FragmentCancelButton = "Скасувати",
        FragmentErrorText = "Введіть час у форматі M:SS або HH:MM:SS",
        StatusPlaylistCompleted = "Плейлист завантажено: {0}",
        StatusCheckingFormats = "Перевірка доступних форматів...",
        TabDisabledTooltip = "Дочекайтеся завершення завантаження",
        TabDisabledToast = "Спочатку докачайте поточний трек"
    };
}
