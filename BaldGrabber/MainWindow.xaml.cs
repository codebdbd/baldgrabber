using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace BaldGrabber;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        appWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 620, Height = 680 });

        var presenter = appWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
        {
            presenter.IsMaximizable = false;
            presenter.IsResizable = false;
        }

        // Dark title bar
        var titleBar = appWindow.TitleBar;
        titleBar.BackgroundColor = ColorHelper.FromArgb(255, 0x12, 0x18, 0x26);
        titleBar.ForegroundColor = Colors.White;
        titleBar.ButtonBackgroundColor = ColorHelper.FromArgb(255, 0x12, 0x18, 0x26);
        titleBar.ButtonForegroundColor = Colors.White;
        titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(255, 0x2A, 0x2F, 0x42);
        titleBar.InactiveBackgroundColor = ColorHelper.FromArgb(255, 0x12, 0x18, 0x26);
        titleBar.InactiveForegroundColor = Colors.Gray;
        titleBar.ButtonInactiveBackgroundColor = ColorHelper.FromArgb(255, 0x12, 0x18, 0x26);
        titleBar.ButtonInactiveForegroundColor = Colors.Gray;

        // Set app icon in title bar
        var iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "AppIcon.ico");
        if (System.IO.File.Exists(iconPath))
        {
            appWindow.SetIcon(iconPath);
        }

        // Extend content into title bar for seamless look
        titleBar.ExtendsContentIntoTitleBar = true;

        RootFrame.Navigate(typeof(MainPage));
    }
}
