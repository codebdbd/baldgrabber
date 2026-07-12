using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using BaldGrabber.Models;
using BaldGrabber.ViewModels;

namespace BaldGrabber;

public sealed partial class MainPage : Page
{
    private readonly ViewModels.Localization _loc;

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
        FolderLabel.Text = _loc.FolderLabel;
        BrowseButtonText.Text = _loc.BrowseButton;
        QualityLabel.Text = _loc.QualityLabel;
        DownloadButtonLabel.Text = _loc.DownloadButton;
        CancelDownloadButtonText.Text = _loc.CancelButton;
        SupportAuthorText.Text = _loc.SupportAuthor;
        OpenFolderButtonText.Text = _loc.OpenFolderButton;
        AudioTabText.Text = _loc.AudioTab;
        VideoTabText.Text = _loc.VideoTab;

        ApplyModeStyle(vm.SelectedMode);

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsDownloading))
            {
                if (vm.IsDownloading)
                {
                    StatusPulseStoryboard.Begin();
                    SetOtherTabEnabled(vm.SelectedMode, false);
                }
                else
                {
                    StatusPulseStoryboard.Stop();
                    StatusText.Opacity = 1;
                    SetOtherTabEnabled(vm.SelectedMode, true);
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
            }
            else if (e.PropertyName == nameof(vm.HasFragment))
            {
                UpdateFragmentButtonStyle(vm);
            }
        };

        UpdateFragmentButtonStyle(vm);
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
        NeonProgressBar.AccentColor = accentColor;
        AudioQualityComboBox.Visibility = isAudio ? Visibility.Visible : Visibility.Collapsed;
        VideoQualityComboBox.Visibility = isAudio ? Visibility.Collapsed : Visibility.Visible;

        QualityLabel.Text = isAudio ? _loc.QualityLabel : _loc.VideoQualityLabel;
        DownloadButtonLabel.Text = isAudio ? _loc.DownloadButton : _loc.VideoDownloadButton;

        if (DataContext is ViewModels.MainViewModel vm)
            UpdateFragmentButtonStyle(vm);
    }

    private void SetOtherTabEnabled(DownloadMode currentMode, bool enabled)
    {
        var otherTabButton = currentMode == DownloadMode.Audio ? VideoTabButton : AudioTabButton;
        var otherTabBorder = currentMode == DownloadMode.Audio ? VideoTabBorder : AudioTabBorder;
        var otherTabText = currentMode == DownloadMode.Audio ? VideoTabText : AudioTabText;
        var otherTabIcon = FindChild<FontIcon>(otherTabButton);

        otherTabButton.IsEnabled = enabled;

        if (enabled)
        {
            otherTabBorder.Opacity = 1.0;
            otherTabText.Foreground = new SolidColorBrush(Color.FromArgb(255, 195, 198, 212));
            if (otherTabIcon != null)
                otherTabIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 195, 198, 212));
            ToolTipService.SetToolTip(otherTabButton, null);
        }
        else
        {
            otherTabBorder.Opacity = 0.4;
            var disabledBrush = new SolidColorBrush(Color.FromArgb(255, 100, 100, 100));
            otherTabText.Foreground = disabledBrush;
            if (otherTabIcon != null)
                otherTabIcon.Foreground = disabledBrush;
            ToolTipService.SetToolTip(otherTabButton, _loc.TabDisabledTooltip);
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
        if (!AudioTabButton.IsEnabled) { ShowDownloadInProgressToast(); return; }
        vm.SelectedMode = DownloadMode.Audio;
    }

    private void VideoTab_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ViewModels.MainViewModel vm) return;
        if (!VideoTabButton.IsEnabled) { ShowDownloadInProgressToast(); return; }
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

    private void FolderTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
            vm.OutputFolder = FolderTextBox.Text;
    }

    private async void FragmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        var startBox = CreateFragmentTextBox(vm.TimeFrom);
        var stopBox = CreateFragmentTextBox(vm.TimeTo);
        var startError = CreateFragmentErrorTextBlock();
        var stopError = CreateFragmentErrorTextBlock();
        var isAudio = vm.SelectedMode == DownloadMode.Audio;
        var activeColor = isAudio ? Color.FromArgb(255, 139, 92, 246) : Color.FromArgb(255, 6, 182, 212);
        var activeBrush = new SolidColorBrush(activeColor);
        var neutralBrush = new SolidColorBrush(Color.FromArgb(255, 59, 66, 86));

        var content = new StackPanel
        {
            Spacing = 8,
            Width = 304
        };
        content.Children.Add(new TextBlock
        {
            Text = _loc.FragmentHintText,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 195, 198, 212)),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13
        });

        content.Children.Add(CreateFragmentField(_loc.FragmentStartLabel, startBox, startError));
        content.Children.Add(CreateFragmentField(_loc.FragmentStopLabel, stopBox, stopError));

        content.Children.Add(new TextBlock
        {
            Text = _loc.FragmentFormatHint,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 107, 114, 128)),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 0)
        });

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = _loc.FragmentDialogTitle,
            Content = content,
            Background = new SolidColorBrush(Color.FromArgb(255, 31, 36, 52)),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
            Padding = new Thickness(24)
        };

        var saveButton = CreateFragmentDialogButton(_loc.FragmentSaveButton, activeBrush);
        var clearButton = CreateFragmentDialogButton(_loc.FragmentClearButton, neutralBrush);
        var cancelButton = CreateFragmentDialogButton(_loc.FragmentCancelButton, neutralBrush);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        buttons.Children.Add(saveButton);
        buttons.Children.Add(clearButton);
        buttons.Children.Add(cancelButton);
        content.Children.Add(buttons);

        saveButton.Click += (_, _) =>
        {
            startError.Text = "";
            stopError.Text = "";

            var isStartValid = IsValidFragmentTime(startBox.Text);
            var isStopValid = IsValidFragmentTime(stopBox.Text);

            if (!isStartValid)
                startError.Text = _loc.FragmentErrorText;
            if (!isStopValid)
                stopError.Text = _loc.FragmentErrorText;

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
        };

        cancelButton.Click += (_, _) => dialog.Hide();

        await dialog.ShowAsync();
    }

    private static StackPanel CreateFragmentField(string label, TextBox textBox, TextBlock errorText)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(textBox);
        panel.Children.Add(errorText);
        return panel;
    }

    private static TextBox CreateFragmentTextBox(string value)
    {
        var textBox = new TextBox
        {
            Text = value,
            PlaceholderText = "M:SS или HH:MM:SS",
            Background = new SolidColorBrush(Color.FromArgb(255, 24, 29, 44)),
            Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
            PlaceholderForeground = new SolidColorBrush(Color.FromArgb(255, 107, 114, 128)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 59, 66, 86)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6, 10, 6),
            FontSize = 15
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
        TextWrapping = TextWrapping.Wrap
    };

    private static Button CreateFragmentDialogButton(string text, Brush background) => new()
    {
        Content = text,
        Background = background,
        Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(255, 75, 85, 110)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6),
        MinWidth = 92,
        MinHeight = 32,
        Padding = new Thickness(12, 6, 12, 6),
        FontSize = 14
    };

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
        var isAudio = vm.SelectedMode == DownloadMode.Audio;
        var activeColor = isAudio ? Color.FromArgb(255, 139, 92, 246) : Color.FromArgb(255, 6, 182, 212);
        var inactiveColor = Color.FromArgb(255, 195, 198, 212);
        FragmentButtonIcon.Foreground = new SolidColorBrush(vm.HasFragment ? activeColor : inactiveColor);
    }
}
