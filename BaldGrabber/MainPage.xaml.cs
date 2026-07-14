using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using BaldGrabber.Models;
using BaldGrabber.Services;
using BaldGrabber.ViewModels;
using Serilog;
using System.Runtime.InteropServices;

namespace BaldGrabber;

public sealed partial class MainPage : Page
{
    private const uint CfUnicodeText = 13;
    private readonly ViewModels.Localization _loc;
    private readonly DispatcherTimer _clipboardTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private uint _lastClipboardSequenceNumber = uint.MaxValue;

    public MainPage()
    {
        InitializeComponent();
        _loc = ViewModels.Localization.Create();
        var vm = new ViewModels.MainViewModel();
        DataContext = vm;

        TitleText.Text = _loc.AppTitle;
        SubtitleText.Text = _loc.AppSubtitle;
        UrlLabel.Text = _loc.UrlLabel;
        UrlTextBox.PlaceholderText = _loc.UrlPlaceholder;
        ToolTipService.SetToolTip(PasteUrlButton, _loc.PasteUrlToolTip);
        FolderLabel.Text = _loc.FolderLabel;
        BrowseButtonText.Text = _loc.BrowseButton;
        QualityLabel.Text = _loc.QualityLabel;
        DownloadButtonLabel.Text = _loc.DownloadButton;
        CancelDownloadButtonText.Text = _loc.CancelButton;
        SupportAuthorText.Text = _loc.SupportAuthor;
        OpenFolderButtonText.Text = _loc.OpenFolderButton;
        AudioTabText.Text = _loc.AudioTab;
        VideoTabText.Text = _loc.VideoTab;

        Loaded += MainPage_Loaded;
        Unloaded += MainPage_Unloaded;
        _clipboardTimer.Tick += ClipboardTimer_Tick;

        ApplyModeStyle(vm.SelectedMode);

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsDownloading))
            {
                if (vm.IsDownloading)
                {
                    StatusPulseStoryboard.Begin();
                    UpdateTabAvailability(vm);
                    UpdateCancelButtonStyle(true);
                }
                else
                {
                    StatusPulseStoryboard.Stop();
                    StatusText.Opacity = 1;
                    UpdateTabAvailability(vm);
                    UpdateCancelButtonStyle(false);
                }
            }
            else if (e.PropertyName == nameof(vm.IsCheckingFormats))
            {
                if (vm.IsCheckingFormats)
                    CheckingFormatsStoryboard.Begin();
                else
                {
                    CheckingFormatsStoryboard.Stop();
                    VideoQualityComboBox.Opacity = 1;
                }
            }
            else if (e.PropertyName == nameof(vm.SelectedMode))
            {
                ApplyModeStyle(vm.SelectedMode);
                UpdateTabAvailability(vm);
            }
            else if (e.PropertyName == nameof(vm.CurrentSource))
            {
                UpdateTabAvailability(vm);
            }
            else if (e.PropertyName == nameof(vm.HasFragment))
            {
                UpdateFragmentButtonStyle(vm);
            }
            else if (e.PropertyName == nameof(vm.DownloadedFilePath))
            {
                UpdateOpenFolderButtonStyle(!string.IsNullOrWhiteSpace(vm.DownloadedFilePath));
            }
        };

        UpdateFragmentButtonStyle(vm);
        UpdateCancelButtonStyle(vm.IsDownloading);
        UpdateOpenFolderButtonStyle(!string.IsNullOrWhiteSpace(vm.DownloadedFilePath));
        UpdateTabAvailability(vm);
    }

    private void UpdateCancelButtonStyle(bool isActive)
    {
        CancelDownloadButton.Background = new SolidColorBrush(isActive
            ? Color.FromArgb(255, 71, 45, 53)
            : Color.FromArgb(255, 42, 47, 66));

        CancelDownloadButtonIcon.Foreground = new SolidColorBrush(isActive
            ? Color.FromArgb(255, 255, 122, 138)
            : Color.FromArgb(255, 125, 132, 152));

        CancelDownloadButtonText.Foreground = new SolidColorBrush(isActive
            ? Color.FromArgb(255, 199, 170, 176)
            : Color.FromArgb(255, 125, 132, 152));
    }

    private void UpdateOpenFolderButtonStyle(bool isActive)
    {
        OpenFolderButtonInline.Background = new SolidColorBrush(isActive
            ? Color.FromArgb(255, 38, 61, 58)
            : Color.FromArgb(255, 42, 47, 66));

        OpenFolderButtonIcon.Foreground = new SolidColorBrush(isActive
            ? Color.FromArgb(255, 125, 226, 195)
            : Color.FromArgb(255, 125, 132, 152));

        OpenFolderButtonText.Foreground = new SolidColorBrush(isActive
            ? Color.FromArgb(255, 169, 201, 192)
            : Color.FromArgb(255, 125, 132, 152));
    }

    // Helper method to find a child of a specific type in the visual tree
    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent == null) return null;

        int childrenCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childrenCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t)
                return t;

            var result = FindChild<T>(child);
            if (result != null)
                return result;
        }
        return null;
    }

    private void VideoQualityComboBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            // Find the ContentPresenter inside the ComboBox
            var contentPresenter = FindChild<ContentPresenter>(comboBox);
            if (contentPresenter != null)
            {
                // Set the ContentPresenter's ContentTemplate to our custom template
                contentPresenter.ContentTemplate = comboBox.Resources["VideoQualityItemTemplate"] as DataTemplate;
            }
        }
    }

    private void ApplyModeStyle(DownloadMode mode)
    {
        var isAudio = mode == DownloadMode.Audio;

        var accentColor = isAudio ? Color.FromArgb(255, 139, 92, 246) : Color.FromArgb(255, 6, 182, 212);
        var accentBrush = new SolidColorBrush(accentColor);
        var inactiveBrush = new SolidColorBrush(Color.FromArgb(255, 42, 47, 66));
        var inactiveTextBrush = new SolidColorBrush(Color.FromArgb(255, 195, 198, 212));
        var whiteBrush = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));

        AudioTabBorder.Background = isAudio ? accentBrush : inactiveBrush;
        AudioTabText.Foreground = isAudio ? whiteBrush : inactiveTextBrush;

        VideoTabBorder.Background = !isAudio ? accentBrush : inactiveBrush;
        VideoTabText.Foreground = !isAudio ? whiteBrush : inactiveTextBrush;

        UrlInputBorder.BorderBrush = accentBrush;
        DownloadButton.Background = accentBrush;
        ((SolidColorBrush)DownloadButton.Resources["ButtonBackgroundPointerOver"]).Color = accentColor;
        ((SolidColorBrush)DownloadButton.Resources["ButtonBackgroundPressed"]).Color = isAudio
            ? Color.FromArgb(255, 124, 79, 224)
            : Color.FromArgb(255, 5, 157, 181);
        ProgressTextBlock.Foreground = accentBrush;
        NeonProgressBar.AccentColor = accentColor;
        AudioQualityComboBox.Visibility = isAudio ? Visibility.Visible : Visibility.Collapsed;
        VideoQualityComboBox.Visibility = isAudio ? Visibility.Collapsed : Visibility.Visible;

        QualityLabel.Text = isAudio ? _loc.QualityLabel : _loc.VideoQualityLabel;
        DownloadButtonLabel.Text = isAudio ? _loc.DownloadButton : _loc.VideoDownloadButton;

        if (DataContext is ViewModels.MainViewModel vm)
            UpdateFragmentButtonStyle(vm);
    }

    private void UpdateTabAvailability(MainViewModel vm)
    {
        var audioEnabled = vm.IsDownloading
            ? vm.SelectedMode == DownloadMode.Audio
            : vm.CurrentSource is not (DownloadSource.TikTok or DownloadSource.Facebook or DownloadSource.Instagram or
                DownloadSource.Twitter or DownloadSource.Reddit or DownloadSource.Vimeo or DownloadSource.Twitch or
                DownloadSource.VkVideo);
        var videoEnabled = vm.IsDownloading
            ? vm.SelectedMode == DownloadMode.Video
            : vm.CurrentSource is not (DownloadSource.SoundCloud or DownloadSource.Bandcamp or
                DownloadSource.Mixcloud or DownloadSource.BandLab or DownloadSource.HearThisAt);

        SetTabEnabled(AudioTabButton, AudioTabBorder, AudioTabText, audioEnabled,
            vm.SelectedMode == DownloadMode.Audio, vm.IsDownloading);
        SetTabEnabled(VideoTabButton, VideoTabBorder, VideoTabText, videoEnabled,
            vm.SelectedMode == DownloadMode.Video, vm.IsDownloading);
    }

    private void SetTabEnabled(
        Button tabButton,
        Border tabBorder,
        TextBlock tabText,
        bool enabled,
        bool isActive,
        bool isDownloading)
    {
        var tabIcon = FindChild<FontIcon>(tabButton);
        tabButton.IsEnabled = enabled;

        if (enabled)
        {
            tabBorder.Opacity = 1.0;
            var enabledBrush = new SolidColorBrush(isActive
                ? Color.FromArgb(255, 255, 255, 255)
                : Color.FromArgb(255, 195, 198, 212));
            tabText.Foreground = enabledBrush;
            if (tabIcon != null)
                tabIcon.Foreground = enabledBrush;
            ToolTipService.SetToolTip(tabButton, null);
        }
        else
        {
            tabBorder.Opacity = 0.4;
            var disabledBrush = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100));
            tabText.Foreground = disabledBrush;
            if (tabIcon != null)
                tabIcon.Foreground = disabledBrush;
            ToolTipService.SetToolTip(tabButton, isDownloading ? _loc.TabDisabledTooltip : null);
        }
    }

    private void ShowDownloadInProgressToast()
    {
        var toast = new InfoBar
        {
            IsOpen = true,
            IsClosable = true,
            Severity = InfoBarSeverity.Informational,
            Title = _loc.TabDisabledToast,
            Margin = new Thickness(0, 8, 0, 0)
        };
        toast.Closed += (_, _) => { if (MainPageRoot.Children.Contains(toast)) MainPageRoot.Children.Remove(toast); };
        MainPageRoot.Children.Add(toast);
        _ = Task.Delay(3000).ContinueWith(_ => DispatcherQueue.TryEnqueue(() =>
        {
            if (MainPageRoot.Children.Contains(toast))
            {
                toast.IsOpen = false;
                MainPageRoot.Children.Remove(toast);
            }
        }));
    }

    private void AudioTab_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel vm) return;
        if (!AudioTabButton.IsEnabled)
        {
            if (vm.IsDownloading) ShowDownloadInProgressToast();
            return;
        }
        vm.SelectedMode = DownloadMode.Audio;
    }

    private void VideoTab_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel vm) return;
        if (!VideoTabButton.IsEnabled)
        {
            if (vm.IsDownloading) ShowDownloadInProgressToast();
            return;
        }
        vm.SelectedMode = DownloadMode.Video;
    }

    private void UrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
            vm.Url = UrlTextBox.Text;
    }

    private void ClearUrlButton_Click(object sender, RoutedEventArgs e)
    {
        UrlTextBox.Text = string.Empty;
        if (DataContext is ViewModels.MainViewModel vm)
            vm.ResetState();
    }

    private void PasteUrlButton_Click(object sender, RoutedEventArgs e)
    {
        var text = GetValidClipboardUrl();
        if (text == null)
        {
            if (DataContext is ViewModels.MainViewModel vm)
                vm.Status = _loc.StatusClipboardEmpty;
            return;
        }

        UrlTextBox.Text = text;
        UrlTextBox.Focus(FocusState.Programmatic);
        UrlTextBox.Select(text.Length, 0);
    }

    private void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        UpdatePasteButtonState();
        _clipboardTimer.Start();
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _clipboardTimer.Stop();
        if (DataContext is MainViewModel vm)
            vm.Shutdown();
    }

    private void ClipboardTimer_Tick(object? sender, object e)
    {
        var sequenceNumber = GetClipboardSequenceNumber();
        if (sequenceNumber == _lastClipboardSequenceNumber)
            return;

        _lastClipboardSequenceNumber = sequenceNumber;
        UpdatePasteButtonState();
    }

    private void UpdatePasteButtonState()
    {
        try
        {
            var isAvailable = GetValidClipboardUrl() != null;
            PasteUrlButton.IsEnabled = isAvailable;
            ToolTipService.SetToolTip(PasteUrlButton,
                isAvailable ? _loc.PasteUrlToolTip : _loc.PasteUrlUnavailableToolTip);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка обновления состояния кнопки вставки");
            PasteUrlButton.IsEnabled = false;
        }
    }

    private string? GetValidClipboardUrl()
    {
        var text = GetClipboardText()?.Trim();
        return text != null && Services.DownloadService.IsValidSupportedUrl(text) ? text : null;
    }

    private static string? GetClipboardText()
    {
        if (!IsClipboardFormatAvailable(CfUnicodeText) || !OpenClipboard(IntPtr.Zero))
            return null;

        IntPtr textHandle = IntPtr.Zero;
        IntPtr textPointer = IntPtr.Zero;
        try
        {
            textHandle = GetClipboardData(CfUnicodeText);
            if (textHandle == IntPtr.Zero)
                return null;

            textPointer = GlobalLock(textHandle);
            return textPointer == IntPtr.Zero ? null : Marshal.PtrToStringUni(textPointer);
        }
        finally
        {
            if (textPointer != IntPtr.Zero)
                GlobalUnlock(textHandle);
            CloseClipboard();
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr newOwner);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);

    private void FolderTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
            vm.OutputFolder = FolderTextBox.Text;
    }

    private async void FragmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        var startBox = CreateFragmentTextBox(vm.TimeFrom, _loc.FragmentStartPlaceholder);
        var stopBox = CreateFragmentTextBox(vm.TimeTo, _loc.FragmentStopPlaceholder);
        var startError = CreateFragmentErrorTextBlock();
        var stopError = CreateFragmentErrorTextBlock();
        var isAudio = vm.SelectedMode == DownloadMode.Audio;
        var activeColor = isAudio ? Color.FromArgb(255, 139, 92, 246) : Color.FromArgb(255, 6, 182, 212);
        var activeBrush = new SolidColorBrush(activeColor);
        var neutralBrush = new SolidColorBrush(Color.FromArgb(255, 59, 66, 86));
        var neutralHoverBrush = new SolidColorBrush(Color.FromArgb(255, 48, 54, 74));
        var neutralPressedBrush = new SolidColorBrush(Color.FromArgb(255, 36, 42, 58));
        var activePressedBrush = new SolidColorBrush(isAudio
            ? Color.FromArgb(255, 124, 79, 224)
            : Color.FromArgb(255, 8, 145, 178));

        var content = new StackPanel
        {
            Spacing = 0,
            Width = 436
        };
        content.Children.Add(new TextBlock
        {
            Text = _loc.FragmentHintText,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 174, 181, 197)),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 18)
        });

        content.Children.Add(CreateFragmentField(
            _loc.FragmentStartLabel, startBox, startError, _loc.FragmentStartHint, activeBrush));
        content.Children.Add(CreateFragmentField(
            _loc.FragmentStopLabel, stopBox, stopError, _loc.FragmentStopHint, activeBrush));

        var titleContent = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleContent.Children.Add(new FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            Glyph = "\uE916",
            FontSize = 18,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 175, 255)),
            VerticalAlignment = VerticalAlignment.Center
        });
        titleContent.Children.Add(new TextBlock
        {
            Text = _loc.FragmentDialogTitle,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center
        });

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = titleContent,
            Content = content,
            Background = new SolidColorBrush(Color.FromArgb(255, 31, 36, 52)),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 59, 66, 86)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(22)
        };

        var whiteBrush = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));
        var saveButton = CreateFragmentDialogButton(
            _loc.FragmentSaveButton, "\uE73E", activeBrush, whiteBrush, whiteBrush,
            activeBrush, activePressedBrush);
        var clearButton = CreateFragmentDialogButton(
            _loc.FragmentClearButton, "\uE777", neutralBrush,
            new SolidColorBrush(Color.FromArgb(255, 120, 175, 255)),
            new SolidColorBrush(Color.FromArgb(255, 174, 191, 216)),
            neutralHoverBrush, neutralPressedBrush);
        var cancelButton = CreateFragmentDialogButton(
            _loc.FragmentCancelButton, "\uE711", neutralBrush,
            new SolidColorBrush(Color.FromArgb(255, 156, 163, 181)),
            new SolidColorBrush(Color.FromArgb(255, 205, 211, 223)),
            neutralHoverBrush, neutralPressedBrush);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        buttons.Children.Add(saveButton);
        buttons.Children.Add(clearButton);
        buttons.Children.Add(cancelButton);
        content.Children.Add(buttons);

        saveButton.Click += (_, _) =>
        {
            startError.Text = "";
            stopError.Text = "";
            startError.Visibility = Visibility.Collapsed;
            stopError.Visibility = Visibility.Collapsed;

            var isStartValid = IsValidFragmentTime(startBox.Text);
            var isStopValid = IsValidFragmentTime(stopBox.Text);

            if (!isStartValid)
            {
                startError.Text = _loc.FragmentErrorText;
                startError.Visibility = Visibility.Visible;
            }
            if (!isStopValid)
            {
                stopError.Text = _loc.FragmentErrorText;
                stopError.Visibility = Visibility.Visible;
            }

            if (!isStartValid || !isStopValid)
                return;

            vm.TimeFrom = startBox.Text.Trim();
            vm.TimeTo = stopBox.Text.Trim();
            dialog.Hide();
        };

        clearButton.Click += (_, _) =>
        {
            startBox.Text = "";
            stopBox.Text = "";
            startError.Text = "";
            stopError.Text = "";
            startError.Visibility = Visibility.Collapsed;
            stopError.Visibility = Visibility.Collapsed;
        };

        cancelButton.Click += (_, _) => dialog.Hide();

        await dialog.ShowAsync(ContentDialogPlacement.Popup);
    }

    private static StackPanel CreateFragmentField(
        string label,
        TextBox textBox,
        TextBlock errorText,
        string hint,
        Brush activeBrush)
    {
        var normalBorderBrush = new SolidColorBrush(Color.FromArgb(255, 37, 43, 58));
        var placeholder = new TextBlock
        {
            Text = textBox.PlaceholderText,
            Foreground = textBox.PlaceholderForeground,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Visibility = string.IsNullOrEmpty(textBox.Text) ? Visibility.Visible : Visibility.Collapsed
        };
        textBox.PlaceholderText = "";
        textBox.TextChanged += (_, _) =>
        {
            placeholder.Visibility = string.IsNullOrEmpty(textBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        };
        var inputContent = new Grid();
        inputContent.Children.Add(placeholder);
        inputContent.Children.Add(textBox);
        var inputBorder = new Border
        {
            Height = 42,
            Background = new SolidColorBrush(Color.FromArgb(255, 24, 29, 44)),
            BorderBrush = normalBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 0, 14, 0),
            Child = inputContent
        };
        textBox.GotFocus += (_, _) =>
        {
            inputBorder.BorderBrush = activeBrush;
            inputBorder.BorderThickness = new Thickness(2);
        };
        textBox.LostFocus += (_, _) =>
        {
            inputBorder.BorderBrush = normalBorderBrush;
            inputBorder.BorderThickness = new Thickness(1);
        };

        var panel = new StackPanel { Spacing = 0, Margin = new Thickness(0, 0, 0, 16) };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 174, 181, 197)),
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        panel.Children.Add(inputBorder);
        panel.Children.Add(errorText);
        panel.Children.Add(new TextBlock
        {
            Text = hint,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 143, 151, 170)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        });
        return panel;
    }

    private TextBox CreateFragmentTextBox(string value, string placeholder)
    {
        var textBox = new TextBox
        {
            Text = value,
            PlaceholderText = placeholder,
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 205, 211, 223)),
            PlaceholderForeground = new SolidColorBrush(Color.FromArgb(255, 139, 147, 167)),
            BorderThickness = new Thickness(0),
            Height = 40,
            MinHeight = 0,
            Padding = new Thickness(0),
            FontSize = 15,
            VerticalContentAlignment = VerticalAlignment.Center,
            Template = FolderTextBox.Template
        };
        textBox.BeforeTextChanging += (_, args) =>
        {
            args.Cancel = !IsTimeInputCandidate(args.NewText);
        };
        return textBox;
    }

    private static TextBlock CreateFragmentErrorTextBlock() => new()
    {
        Foreground = new SolidColorBrush(Color.FromArgb(255, 239, 68, 68)),
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 4, 0, 0),
        Visibility = Visibility.Collapsed
    };

    private static Button CreateFragmentDialogButton(
        string text,
        string glyph,
        Brush background,
        Brush iconForeground,
        Brush textForeground,
        Brush hoverBackground,
        Brush pressedBackground)
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(new FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            Glyph = glyph,
            FontSize = 16,
            Foreground = iconForeground,
            VerticalAlignment = VerticalAlignment.Center
        });
        content.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 14,
            Foreground = textForeground,
            VerticalAlignment = VerticalAlignment.Center
        });

        var button = new Button
        {
            Content = content,
            Background = background,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Width = 138,
            Height = 40,
            Padding = new Thickness(8, 0, 8, 0),
            UseSystemFocusVisuals = false,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        button.Resources["ButtonBackgroundPointerOver"] = hoverBackground;
        button.Resources["ButtonBackgroundPressed"] = pressedBackground;
        return button;
    }

    private static bool IsTimeInputCandidate(string value)
    {
        foreach (var ch in value)
        {
            if (!char.IsDigit(ch) && ch != ':')
                return false;
        }

        return value.Count(c => c == ':') <= 2;
    }

    private static bool IsValidFragmentTime(string value)
    {
        var text = value.Trim();
        if (text.Length == 0)
            return true;

        var parts = text.Split(':');
        if (parts.Length is not (2 or 3))
            return false;

        if (parts.Any(p => p.Length == 0 || !p.All(char.IsDigit)))
            return false;

        if (parts.Length == 2)
            return parts[1].Length == 2 && int.Parse(parts[1]) <= 59;

        return parts[1].Length == 2 &&
               parts[2].Length == 2 &&
               int.Parse(parts[1]) <= 59 &&
               int.Parse(parts[2]) <= 59;
    }

    private void UpdateFragmentButtonStyle(MainViewModel vm)
    {
        FragmentButtonIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 175, 255));
    }
}
