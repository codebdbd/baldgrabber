# Changelog

## [1.1.0] - 2026-07-12

### Added

- Download all videos from a YouTube channel (`@`, `/c/`, `/user/` links)

## [1.0.0] - 2026-07-12

### Added

- Download YouTube videos as MP4 (240p — 4K)
- Download YouTube audio as Opus, M4A, MP3 (96/128 kbps)
- Playlist download with automatic track numbering
- Audio and video trimming by timestamps (fragment)
- Automatic cover art embedding in audio files
- Auto-detection of available formats for each video
- Download speed and remaining time display
- Localization: Russian, English, Ukrainian
- Installer (Inno Setup)
- Portable version
- Portable settings and log storage
- Logging via Serilog with file rotation
- Single app instance (mutex)
- Dark interface with neon progress bar

### Security

- Prohibition on storing cookies, tokens, and private parameters in logs
- URL truncation in logs to `https://domain/path`

---

# История изменений

## [1.1.0] - 2026-07-12

### Added

- Скачивание всех видео с YouTube-канала (ссылки `@`, `/c/`, `/user/`)

## [1.0.0] - 2026-07-12

### Added

- Скачивание видео YouTube в формате MP4 (240p — 4K)
- Скачивание аудио YouTube в Opus, M4A, MP3 (96/128 kbps)
- Скачивание плейлистов с автоматической нумерацией треков
- Обрезка аудио и видео по временным меткам (фрагмент)
- Автоматическое встраивание обложки в аудиофайлы
- Автоопределение доступных форматов для конкретного видео
- Отображение скорости загрузки и оставшегося времени
- Локализация: русский, английский, украинский
- Установщик (Inno Setup)
- Портативная версия
- Портативное хранение настроек и журналов
- Логирование через Serilog с ротацией файлов
- Одиночный экземпляр приложения (mutex)
- Тёмный интерфейс с неоновым прогресс-баром

### Security

- Запрет сохранения cookie, токенов и приватных параметров в журналах
- Обрезка URL в логах до вида `https://domain/path`
