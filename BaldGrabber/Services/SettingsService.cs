using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using BaldGrabber.Models;
using Serilog;

namespace BaldGrabber.Services;

public class SettingsService
{
    private readonly string _settingsPath;

    public SettingsService()
    {
        var portableDataPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "Data");

        if (Directory.Exists(portableDataPath))
        {
            _settingsPath = Path.Combine(portableDataPath, "settings.json");
        }
        else
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BaldGrabber");

            if (!Directory.Exists(appDataPath))
                Directory.CreateDirectory(appDataPath);

            _settingsPath = Path.Combine(appDataPath, "settings.json");
        }
    }

    public Settings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка при загрузке настроек");
        }

        return new Settings();
    }

    public async Task SaveSettingsAsync(Settings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(_settingsPath, json);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ошибка при сохранении настроек");
        }
    }
}
