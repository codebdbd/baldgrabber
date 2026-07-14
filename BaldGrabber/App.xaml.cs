using System;
using System.IO;
using System.Threading;
using Serilog;
using Microsoft.UI.Xaml;
using Windows.Storage;
using Microsoft.UI.Dispatching;

namespace BaldGrabber;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Local\BaldGrabber.SingleInstance";
    private Window? _window;
    public static Window? MainWindow { get; private set; }
    private Mutex? _mutex;

    public App()
    {
        // Определяем папку для логов: портабл или AppData
        var portableDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "Data");
        var logDir = Directory.Exists(portableDataPath)
            ? Path.Combine(portableDataPath, "Logs")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AudioGrabber", "Logs");
        if (!Directory.Exists(logDir))
            Directory.CreateDirectory(logDir);
            
        var logPath = Path.Combine(logDir, "log-.txt");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 10,
                fileSizeLimitBytes: 5 * 1024 * 1024,
                rollOnFileSizeLimit: true)
            .CreateLogger();

        // Глобальная обработка исключений
        this.UnhandledException += (s, e) =>
        {
            Log.Error(e.Exception, "Необработанное исключение");
            Log.CloseAndFlush();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Log.CloseAndFlush();

        try
        {
            InitializeComponent();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка инициализации компонента");
            Log.CloseAndFlush();
            throw;
        }
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            _mutex = new Mutex(true, SingleInstanceMutexName, out bool createdNew);
            if (!createdNew)
            {
                Log.Warning("Приложение уже запущено");
                Log.CloseAndFlush();
                _mutex.Dispose();
                _mutex = null;
                Environment.Exit(0);
                return;
            }

            Log.Information("Приложение запущено");
            _window = new MainWindow();
            MainWindow = _window;
            _window.Activate();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка при запуске окна");
            Log.CloseAndFlush();
            throw;
        }
    }
}
