# Privacy

## What data is stored locally

The app stores the following data on your computer:

**Settings** (`settings.json`):
- last selected mode (Audio / Video)
- last selected quality
- path to the last save folder

**Logs** (`log-*.txt` files):
- URL of downloaded videos (domain and path, without authentication parameters)
- operation status (download, conversion, errors)
- error messages from yt-dlp and FFmpeg
- technical information (file sizes, versions, execution time)

## Does the app send telemetry

No. BaldGrabber does not collect or send any telemetry, analytics, or usage statistics. There are no servers that the app sends data to.

## Where the app sends network requests

BaldGrabber sends requests only through yt-dlp:
- YouTube API for fetching video information and available format list
- YouTube CDN servers for downloading media files

There are no other network requests. The app does not access third-party servers, does not send system data, and does not perform background requests.

## Deleting settings and logs

Delete manually:
- **Settings:** `%APPDATA%\BaldGrabber\settings.json` (installer version) or `Data\settings.json` in the portable folder root — two levels above `BaldGrabber.exe` (portable version)
- **Logs:** `%APPDATA%\AudioGrabber\Logs\` (installer version) or `Data\Logs\` in the portable folder root (portable version)
- **Temp files:** `%TEMP%\BaldGrabber\` — automatically deleted after download, but may remain after a crash

## Prohibition on storing private data in logs

The app does not store the following in logs:
- cookies
- authentication tokens
- private links (with parameters `sig=`, `token=`, `key=`, `session=`)

URLs in logs are truncated to `https://www.youtube.com/watch` — domain and path without query parameters (video ID and other parameters are removed).

---

# Конфиденциальность

## Какие данные хранятся локально

Программа хранит следующие данные на вашем компьютере:

**Настройки** (`settings.json`):
- последний выбранный режим (Аудио / Видео)
- последнее выбранное качество
- путь к последней папке сохранения

**Журналы** (файлы `log-*.txt`):
- URL загружаемых видео (домен и путь, без параметров аутентификации)
- статус операций (загрузка, конвертация, ошибки)
- сообщения об ошибках от yt-dlp и FFmpeg
- техническая информация (размеры файлов, версии, время выполнения)

## Отправляет ли программа телеметрию

Нет. BaldGrabber не собирает и не отправляет никакую телеметрию, аналитику или статистику использования. Нет никаких серверов, на которые программа отправляет данные.

## Куда программа отправляет сетевые запросы

BaldGrabber отправляет запросы только через yt-dlp:
- YouTube API для получения информации о видео и списка доступных форматов
- Серверы CDN YouTube для загрузки медиафайлов

Нет никаких других сетевых запросов. Программа не обращается к сторонним серверам, не отправляет данные о системе и не выполняет фоновых запросов.

## Удаление настроек и журналов

Удалите вручную:
- **Настройки:** `%APPDATA%\BaldGrabber\settings.json` (установочная версия) или `Data\settings.json` в корне портативной папки — на два уровня выше от `BaldGrabber.exe` (портативная версия)
- **Журналы:** `%APPDATA%\AudioGrabber\Logs\` (установочная версия) или `Data\Logs\` в корне портативной папки (портативная версия)
- **Временные файлы:** `%TEMP%\BaldGrabber\` — удаляются автоматически после загрузки, но могут оставаться при аварийном завершении

## Запрет сохранения приватных данных в журналах

Программа не сохраняет в журналах:
- cookie
- токены аутентификации
- приватные ссылки (с параметрами `sig=`, `token=`, `key=`, `session=`)

URL в логах обрезается до вида `https://www.youtube.com/watch` — домен и путь без параметров запроса (video ID и прочие параметры удаляются).
