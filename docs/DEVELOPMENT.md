# Разработка и сборка

## Архитектура

```
Интерфейс (WinUI 3) → MainViewModel → DownloadService → yt-dlp → FFmpeg → файл
                                                  ↓
                                         SettingsService → settings.json
```

Приложение следует паттерну MVVM:
- **View** — `MainPage.xaml` + `MainWindow.xaml`
- **ViewModel** — `MainViewModel.cs` (вся бизнес-логика)
- **Model** — `Settings.cs`, `DownloadMode.cs`, `VideoQualityOption.cs`, `AudioQuality.cs`
- **Service** — `DownloadService.cs` (взаимодействие с yt-dlp/FFmpeg), `SettingsService.cs` (хранилище настроек)
- **Converter** — конвертеры для привязок данных в XAML
- **Control** — `NeonProgressBar` (кастомный прогресс-бар)

## Структура проекта

```
BaldGrabber/
├── App.xaml / App.xaml.cs          — точка входа, логирование, одиночный запуск
├── MainWindow.xaml / .cs           — главное окно
├── MainPage.xaml / .cs             — основной интерфейс
├── ViewModels/
│   └── MainViewModel.cs            — логика загрузки, UI-состояния, локализация
├── Models/
│   ├── Settings.cs                 — модель настроек (JSON)
│   ├── DownloadMode.cs             — enum Video/Audio
│   ├── VideoQualityOption.cs       — вариант качества видео
│   └── AudioQuality.cs             — вариант качества аудио
├── Services/
│   ├── DownloadService.cs          — загрузка через yt-dlp, обрезка через FFmpeg
│   └── SettingsService.cs          — чтение/запись settings.json
├── Controls/
│   └── NeonProgressBar.xaml/.cs    — прогресс-бар
├── Converters/                     — конвертеры XAML-привязок
├── Assets/                         — иконки, шрифты
├── Tools/                          — yt-dlp.exe, ffmpeg.exe (не в репозитории)
└── BaldGrabber.csproj
```

## Требования для сборки

- Visual Studio 2022 (17.8+)
- .NET 9 SDK
- Windows 10 SDK (10.0.19041.0+)
- Windows App SDK 2.2.0 (подтягивается автоматически через NuGet)
- Платформа: x64 (рекомендуется), x86 или ARM64

## Команды сборки

```bash
# Через dotnet CLI
dotnet build BaldGrabber/BaldGrabber.csproj -c Release -r win-x64

# Через dotnet CLI (публикация)
dotnet publish BaldGrabber/BaldGrabber.csproj -c Release -r win-x64 --self-contained true

# Через Visual Studio
# Build → Publish → Folder Profile → win-x64 → Self-Contained
```

После публикации файлы приложения будут в `bin/Release/net9.0-windows10.0.19041.0/win-x64/publish/`.

## Интеграция yt-dlp и FFmpeg

Оба инструмента размещаются в папке `Tools/` рядом с `BaldGrabber.exe`. Пути определяются относительно `AppDomain.CurrentDomain.BaseDirectory`:

```csharp
_ytDlpPath = Path.Combine(basePath, "Tools", "yt-dlp.exe");
_ffmpegPath = Path.Combine(basePath, "Tools", "ffmpeg.exe");
```

## Передача аргументов внешним процессам

Аргументы передаются через `ProcessStartInfo.ArgumentList` (не через строку аргументов — это безопаснее):

```csharp
var process = new Process { StartInfo = new ProcessStartInfo { FileName = _ytDlpPath, ... } };
process.StartInfo.ArgumentList.Add("--encoding");
process.StartInfo.ArgumentList.Add("utf-8");
// ...
```

## Обработка прогресса и ошибок

Прогресс yt-dlp парсится из stdout/stderr с помощью регулярных выражений:
- `[ExtractAudio]` → конвертация
- `[Merger]`, `[FixupM3u8]` → склейка
- `XX%` → процент загрузки
- `at XXX.XX/s` → скорость
- `ETA HH:MM:SS` → оставшееся время

Отрицательные значения прогресса кодируют промежуточные этапы:
- `-1` — конвертация в MP3
- `-2` — склейка файлов
- `-3` — очистка
- `-4` — обрезка
- `-5` — встраивание обложки

## Расположение настроек и журналов

Портативная версия (если рядом с `BaldGrabber.exe` существует `../../Data/`):
- Настройки: `Data/settings.json`
- Журналы: `Data/Logs/log-*.txt`
- Временные файлы: `Data/Temp/`

Установочная версия:
- Настройки: `%APPDATA%/BaldGrabber/settings.json`
- Журналы: `%APPDATA%/AudioGrabber/Logs/log-*.txt`
- Временные файлы: `%TEMP%/BaldGrabber/`

Логирование через Serilog: ротация по дням, максимум 10 файлов, лимит 5 МБ на файл.

## Обновление зависимостей

```bash
dotnet list BaldGrabber/BaldGrabber.csproj package --outdated
dotnet add BaldGrabber/BaldGrabber.csproj package <PackageReference> --version <new>
```

Для обновления yt-dlp и FFmpeg — замените файлы в папке `Tools/` вручную.

## Выпуск новой версии

1. Обновите `AppVersion` в `BaldGrabber_Installer.iss`
2. Соберите проект в Release
3. Скопируйте publish-файлы в `BaldGrabber_Release/`
4. Соберите установщик через Inno Setup: `BaldGrabber_Installer.iss`
5. Проверьте установку и запуск на чистой системе

## Добавление языка

Добавьте метод `CreateXx()` в класс `Localization` в `MainViewModel.cs` и добавьте case в `Localization.Create()`:
```csharp
"xx" => CreateXx(),
```
