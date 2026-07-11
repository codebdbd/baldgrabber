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
                UpdateCanDownload();
                if (SelectedMode == DownloadMode.Video)
                    _ = FetchAvailableFormatsAsync();
            }
        }
    }

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
            if (SetProperty(ref _selectedMode, value))
            {
                UpdateButtonLabel();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanSelectVideoQuality)));
                _settings.SelectedMode = value.ToString();
                _ = _settingsService.SaveSettingsAsync(_settings);

                if (value == DownloadMode.Video)
                {
                    ResetVideoQualitySelection();
                    _ = FetchAvailableFormatsAsync();
                }
                else
                {
                    _formatCancellationTokenSource?.Cancel();
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
                if (value != null && value.Id != string.Empty)
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

    public bool CanSelectVideoQuality => HasCheckedVideoFormats && !IsCheckingFormats;

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
    private VideoQualityOption VideoPlaceholderQuality { get; } = new() { Id = "", Name = "Выберите качество видео", Description = "" };
    private VideoQualityOption CheckingFormatsQuality { get; } = new() { Id = "", Name = "Идет проверка доступных форматов", Description = "" };

    private string _downloadButtonText = "";
    public string DownloadButtonText { get => _downloadButtonText; set => SetProperty(ref _downloadButtonText, value); }

    public ICommand DownloadCommand { get; }
    public ICommand BrowseFolderCommand { get; }
    public ICommand OpenFolderCommand { get; }

    private readonly List<VideoQualityOption> _videoQualities = new()
    {
        new() { Id = "2160p", Name = "MP4 2160p 4K", Description = "Ultra HD" },
        new() { Id = "1440p", Name = "MP4 1440p", Description = "Quad HD" },
        new() { Id = "1080p", Name = "MP4 1080p", Description = "Отличное качество" },
        new() { Id = "720p", Name = "MP4 720p", Description = "Хорошее качество" },
        new() { Id = "480p", Name = "MP4 480p", Description = "Стандартное качество" },
        new() { Id = "360p", Name = "MP4 360p", Description = "Маленький размер файла" },
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
        BrowseFolderCommand = new AsyncRelayCommand(BrowseFolderAsync);
        OpenFolderCommand = new RelayCommand(OpenFolder);

        Status = _loc.StatusWaiting;
        OutputFolder = _settings.LastFolder ?? string.Empty;
        foreach (var q in _audioQualities)
            AudioQualities.Add(q);

        var savedMode = Enum.TryParse<DownloadMode>(_settings.SelectedMode, out var m) ? m : DownloadMode.Video;
        _selectedMode = savedMode;
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
        VideoQualities = new ObservableCollection<VideoQualityOption> { VideoPlaceholderQuality };
        SelectedVideoQuality = VideoPlaceholderQuality;
    }

    private AudioQuality? GetDefaultAudioQuality() =>
        AudioQualities.FirstOrDefault(q => q.Id == (string.IsNullOrEmpty(_settings.SelectedAudioQuality) ? "m4a" : _settings.SelectedAudioQuality) && q.IsAvailable)
        ?? AudioQualities.FirstOrDefault(q => q.IsAvailable);

    private void ShowCheckingFormatsQuality()
    {
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

    private void UpdateButtonLabel() => DownloadButtonText = SelectedMode == DownloadMode.Video ? "Скачать видео" : "Скачать аудио";

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
        _formatCancellationTokenSource?.Cancel();
        _formatCancellationTokenSource?.Dispose();

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

        try
        {
            _formatCancellationTokenSource = new CancellationTokenSource();
            var ct = _formatCancellationTokenSource.Token;

            HasCheckedVideoFormats = false;
            ShowCheckingFormatsQuality();
            IsCheckingFormats = true;
            FormatsStatus = "";
            await Task.Delay(300, ct);

            var formats = await _downloadService.GetAvailableFormatsAsync(Url, DownloadMode.Video, ct);

            if (SelectedMode != DownloadMode.Video)
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
            IsCheckingFormats = false;
        }
    }

    private void UpdateCanDownload()
    {
        var hasSelectedQuality = SelectedMode == DownloadMode.Audio
            ? SelectedAudioQuality is { Id.Length: > 0, IsAvailable: true }
            : SelectedVideoQuality is { Id.Length: > 0, IsAvailable: true } && HasCheckedVideoFormats && !IsCheckingFormats;

        CanDownload = !string.IsNullOrWhiteSpace(Url) &&
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
        if (!Services.DownloadService.IsValidYouTubeUrl(Url))
        {
            Status = _loc.StatusInvalidUrl;
            return;
        }
        if (Services.DownloadService.IsPlaylistUrl(Url))
        {
            Status = _loc.StatusPlaylist;
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

            _settings.SelectedMode = SelectedMode.ToString();
            if (SelectedMode == DownloadMode.Audio && !string.IsNullOrEmpty(SelectedAudioQuality?.Id))
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
                Status = SelectedMode == DownloadMode.Video ? "Выберите доступное качество видео" : "Выберите качество аудио";
                return;
            }

            var timeFrom = string.IsNullOrWhiteSpace(TimeFrom) ? null : TimeFrom;
            var timeTo = string.IsNullOrWhiteSpace(TimeTo) ? null : TimeTo;
            var (filePath, title, actualQuality) = await _downloadService.DownloadAsync(
                SelectedMode, Url, qualityId, OutputFolder, timeFrom, timeTo, progress, _cancellationTokenSource.Token);

            DownloadedFilePath = filePath;
            IsIndeterminate = false;
            ProgressText = "100%";
            Status = string.IsNullOrEmpty(actualQuality)
                ? string.Format(_loc.StatusCompleted, title)
                : $"{string.Format(_loc.StatusCompleted, title)} ({actualQuality})";
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
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private void OpenFolder()
    {
        try
        {
            if (!string.IsNullOrEmpty(DownloadedFilePath) && System.IO.File.Exists(DownloadedFilePath))
            {
                var safePath = DownloadedFilePath.Replace("\"", "\\\"");
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{safePath}\"", UseShellExecute = true });
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
    public string SupportAuthor { get; private set; } = "";
    public string OpenFolderButton { get; private set; } = "";
    public string StatusWaiting { get; private set; } = "";
    public string StatusFolderError { get; private set; } = "";
    public string StatusEnterUrl { get; private set; } = "";
    public string StatusInvalidUrl { get; private set; } = "";
    public string StatusPlaylist { get; private set; } = "";
    public string StatusSelectFolder { get; private set; } = "";
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
    public string Quality320Desc { get; private set; } = "";
    public string Quality256Desc { get; private set; } = "";
    public string Quality192Desc { get; private set; } = "";
    public string Quality128Desc { get; private set; } = "";
    public string Quality96Desc { get; private set; } = "";
    public string VideoQualityLabel { get; private set; } = "";
    public string VideoDownloadButton { get; private set; } = "";

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
        UrlPlaceholder = "Paste YouTube video link",
        FolderLabel = "Save folder",
        BrowseButton = "Browse...",
        QualityLabel = "Audio quality",
        FragmentLabel = "Audio fragment",
        TimeFromLabel = "Start",
        TimeToLabel = "Stop",
        TimeFromPlaceholder = "0:00",
        TimeToPlaceholder = "end",
        FragmentHint = "Leave empty to download full audio. Format: M:SS or HH:MM:SS",
        DownloadButton = "Download audio",
        SupportAuthor = "Support the author",
        OpenFolderButton = "Open folder",
        StatusWaiting = "Waiting",
        StatusFolderError = "Folder selection error",
        StatusEnterUrl = "Enter YouTube link",
        StatusInvalidUrl = "Enter valid YouTube link",
        StatusPlaylist = "Playlist links are not supported",
        StatusSelectFolder = "Select save folder",
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
        Quality320Desc = "Maximum MP3 quality",
        Quality256Desc = "Good balance of quality and size",
        Quality192Desc = "Standard quality",
        Quality128Desc = "Small file size",
        Quality96Desc = "Very small file size",
        VideoQualityLabel = "Video quality",
        VideoDownloadButton = "Download video",
        EmbedThumbnailLabel = "Embed thumbnail"
    };

    private static Localization CreateRu() => new()
    {
        AppTitle = "BalDGrabber",
        AppSubtitle = "Вставь ссылку YouTube и качай!",
        UrlLabel = "Ссылка на YouTube",
        UrlPlaceholder = "Вставьте ссылку на YouTube видео",
        FolderLabel = "Папка для сохранения",
        BrowseButton = "Обзор...",
        QualityLabel = "Качество аудио",
        FragmentLabel = "Фрагмент аудио",
        TimeFromLabel = "Старт",
        TimeToLabel = "Стоп",
        TimeFromPlaceholder = "0:00",
        TimeToPlaceholder = "конец",
        FragmentHint = "Оставьте пустым для скачивания полного аудио. Формат: M:SS или HH:MM:SS",
        DownloadButton = "Скачать аудио",
        SupportAuthor = "Поблагодарить автора",
        OpenFolderButton = "Открыть папку",
        StatusWaiting = "Ожидание",
        StatusFolderError = "Ошибка при выборе папки",
        StatusEnterUrl = "Введите ссылку на YouTube",
        StatusInvalidUrl = "Введите корректную ссылку на YouTube",
        StatusPlaylist = "Ссылки на плейлисты не поддерживаются",
        StatusSelectFolder = "Выберите папку для сохранения",
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
        Quality320Desc = "Максимальное качество MP3",
        Quality256Desc = "Хороший баланс качества и размера",
        Quality192Desc = "Стандартное качество",
        Quality128Desc = "Маленький размер файла",
        Quality96Desc = "Очень маленький размер файла",
        VideoQualityLabel = "Качество видео",
        VideoDownloadButton = "Скачать видео",
        EmbedThumbnailLabel = "Встроить обложку"
    };

    private static Localization CreateUk() => new()
    {
        AppTitle = "BalDGrabber",
        AppSubtitle = "Встав посилання YouTube і качай!",
        UrlLabel = "Посилання на YouTube",
        UrlPlaceholder = "Вставте посилання на YouTube відео",
        FolderLabel = "Папка для збереження",
        BrowseButton = "Огляд...",
        QualityLabel = "Якість аудіо",
        FragmentLabel = "Фрагмент аудіо",
        TimeFromLabel = "Старт",
        TimeToLabel = "Стоп",
        TimeFromPlaceholder = "0:00",
        TimeToPlaceholder = "кінець",
        FragmentHint = "Залиште порожнім для завантаження повного аудіо. Формат: M:SS або HH:MM:SS",
        DownloadButton = "Завантажити аудіо",
        SupportAuthor = "Поблагодарити автора",
        OpenFolderButton = "Відкрити папку",
        StatusWaiting = "Очікування",
        StatusFolderError = "Помилка при виборі папки",
        StatusEnterUrl = "Введіть посилання на YouTube",
        StatusInvalidUrl = "Введіть коректне посилання на YouTube",
        StatusPlaylist = "Посилання на плейлисти не підтримуються",
        StatusSelectFolder = "Оберіть папку для збереження",
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
        Quality320Desc = "Максимальна якість MP3",
        Quality256Desc = "Гарний баланс якості та розміру",
        Quality192Desc = "Стандартна якість",
        Quality128Desc = "Малий розмір файлу",
        Quality96Desc = "Дуже малий розмір файлу",
        VideoQualityLabel = "Якість відео",
        VideoDownloadButton = "Завантажити відео",
        EmbedThumbnailLabel = "Вбудувати обкладинку"
    };
}
