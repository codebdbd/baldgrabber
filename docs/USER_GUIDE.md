# BaldGrabber User Guide

## Pasting a Link

Copy a YouTube video or playlist link from your browser and paste it into the "YouTube Link" field (Ctrl+V).

Supported link formats:
- `https://www.youtube.com/watch?v=...`
- `https://youtube.com/watch?v=...`
- `https://youtu.be/...`
- `https://m.youtube.com/...`
- `https://music.youtube.com/...`
- Playlist links (`list=...`)

Links from other sites are not recognized.

Channel links are also supported:
- `https://www.youtube.com/@channelname`
- `https://www.youtube.com/c/channelname`
- `https://www.youtube.com/user/username`

When a channel link is pasted, all its videos are downloaded.

## Selecting a Mode

Switch between the "Audio" or "Video" tabs at the top of the window. The last selected mode is remembered.

## Selecting Quality

### Video

After pasting a link, the app automatically checks available formats. The list shows only resolutions available for the given video. Unavailable options are marked as "unavailable."

Available resolutions: 2160p (4K), 1440p, 1080p, 720p, 480p, 360p, 240p. The app automatically marks unavailable options.

The final format is MP4. The app automatically merges video and audio tracks when YouTube serves them separately.

### Audio

| Format | Bitrate | Description |
|--------|---------|-------------|
| Opus | original | Minimal loss, YouTube's native codec |
| M4A | original | AAC, good player compatibility |
| MP3 128 | 128 kbps | Works on any device |
| MP3 96 | 96 kbps | Minimal size, for voice recordings and podcasts |

MP3 conversion does not improve quality — it is determined by what YouTube provides for a specific video. MP3 128 will not make the sound better than M4A if the source was 64 kbps.

## Choosing a Folder

Click "Browse" next to the "Save Folder" field and select any folder on disk. You can also type the path manually. The last selected folder is remembered.

## Starting a Download

Click the "Download Video" or "Download Audio" button. During download, the following are displayed:
- progress bar
- current download percentage
- download speed
- remaining time
- status (conversion, merging, trimming, cover embedding)

### Cancellation

Close the app window or start a new download — the current one will be cancelled automatically. Temporary files are deleted.

## Trimming

Click the trim button (scissors icon) next to the quality selector. Specify:
- **Start** — beginning of the fragment (format `M:SS` or `HH:MM:SS`)
- **End** — end of the fragment

Both fields can be left empty to download the full file. Trimming is performed by FFmpeg without re-encoding (stream copy), so it takes minimal time.

Format examples:
- `1:30` — from 1 minute 30 seconds
- `01:02:30` — from 1 hour 2 minutes 30 seconds
- `3:00` — from 3 minutes

## Playlists

When a playlist link is pasted, the app:
1. Determines the number of tracks
2. Creates a folder with the playlist name
3. Downloads tracks one by one
4. Files are numbered: `01 - Title.mp3`, `02 - Title.mp3`...

Tracks are downloaded one at a time. If an error occurs, the track is retried automatically.

## Channels

When a YouTube channel link is pasted, the app downloads all its videos as a playlist:
- `https://www.youtube.com/@channelname`
- `https://www.youtube.com/c/channelname`
- `https://www.youtube.com/user/username`

A folder with the channel name is created, videos are numbered and downloaded one by one.

## Cover Art (Thumbnail)

For audio, the app automatically downloads the video's thumbnail and embeds it as album art in the file. This works for all audio formats (Opus, M4A, MP3).

## Opening the Output Folder

The "Open" button next to the folder field becomes available after the download is complete. Click it to open the folder with the downloaded file in Explorer.

## Settings

The app saves:
- last selected mode (Audio / Video)
- last selected quality
- last save folder

Settings are stored in `settings.json` and restored on the next launch.

---

# Руководство пользователя BaldGrabber

## Вставка ссылки

Скопируйте ссылку на YouTube-видео или плейлист из браузера и вставьте в поле «Ссылка на YouTube» (Ctrl+V).

Поддерживаются форматы ссылок:
- `https://www.youtube.com/watch?v=...`
- `https://youtube.com/watch?v=...`
- `https://youtu.be/...`
- `https://m.youtube.com/...`
- `https://music.youtube.com/...`
- Ссылки на плейлисты (`list=...`)

Ссылки с других сайтов не распознаются.

Кроме того, поддерживаются ссылки на каналы:
- `https://www.youtube.com/@channelname`
- `https://www.youtube.com/c/channelname`
- `https://www.youtube.com/user/username`

При вставке ссылки на канал скачиваются все его видео.

## Выбор режима

Переключите вкладки «Аудио» или «Видео» в верхней части окна. Последний выбранный режим запоминается.

## Выбор качества

### Видео

После вставки ссылки программа автоматически проверяет доступные форматы. В списке будут только разрешения, доступные для данного видео. Недоступные варианты помечены как «недоступно».

Доступные разрешения: 2160p (4K), 1440p, 1080p, 720p, 480p, 360p, 240p. Программа автоматически отмечает недоступные варианты.

Финальный формат — MP4. Программа автоматически объединяет видео и аудиодорожки, если YouTube раздаёт их отдельно.

### Аудио

| Формат | Битрейт | Описание |
|--------|---------|----------|
| Opus | оригинальный | Минимальные потери, оригинальный кодек YouTube |
| M4A | оригинальный | AAC, хорошая совместимость с плеерами |
| MP3 128 | 128 kbps | Работает на любых устройствах |
| MP3 96 | 96 kbps | Минимальный размер, для голосовых записей и подкастов |

Конвертация в MP3 не улучшает качество — оно определяется тем, что доступно на YouTube для конкретного видео. MP3 128 не сделает звук лучше, чем M4A, если исходник был в 64 kbps.

## Выбор папки

Нажмите «Выбрать» рядом с полем «Папка для сохранения» и укажите любую папку на диске. Путь можно ввести вручную. Последняя выбранная папка запоминается.

## Запуск загрузки

Нажмите кнопку «Скачать видео» или «Скачать аудио». В процессе загрузки отображаются:
- прогресс-бар
- текущий процент загрузки
- скорость загрузки
- оставшееся время
- статус (конвертация, склейка, обрезка, встраивание обложки)

### Отмена

Закройте окно программы или начните новую загрузку — текущая отменится автоматически. Временные файлы удаляются.

## Фрагмент (обрезка)

Нажмите кнопку фрагмента (иконка с ножницами) рядом с выбором качества. Укажите:
- **Старт** — начало фрагмента (формат `M:SS` или `HH:MM:SS`)
- **Стоп** — конец фрагмента

Оба поля можно оставить пустыми для скачивания полного файла. Обрезка выполняется FFmpeg без перекодирования (потоковое копирование), поэтому занимает минимум времени.

Примеры формата:
- `1:30` — с 1 минуты 30 секунд
- `01:02:30` — с 1 часа 2 минут 30 секунд
- `3:00` — с 3 минут

## Плейлисты

При вставке ссылки на плейлист программа:
1. Определяет количество треков
2. Создаёт папку с названием плейлиста
3. Скачивает треки по очереди
4. Файлы нумеруются: `01 - Название.mp3`, `02 - Название.mp3`...

Скачивание идёт по одному треку. При ошибке трек повторяется автоматически.

## Каналы

При вставке ссылки на YouTube-канал программа скачивает все его видео как плейлист:
- `https://www.youtube.com/@channelname`
- `https://www.youtube.com/c/channelname`
- `https://www.youtube.com/user/username`

Создаётся папка с названием канала, видео нумеруются и скачиваются по очереди.

## Обложка (thumbnail)

Для аудио программа автоматически скачивает обложку видео и встраивает её в файл как обложку альбома. Это работает для всех аудиоформатов (Opus, M4A, MP3).

## Открытие папки с результатом

Кнопка «Открыть» рядом с полем папки становится доступной после завершения загрузки. Нажмите её, чтобы открыть папку с скачанным файлом в Проводнике.

## Настройки

Программа сохраняет:
- последний выбранный режим (Аудио / Видео)
- последнее выбранное качество
- последнюю папку сохранения

Настройки хранятся в `settings.json` и восстанавливаются при следующем запуске.
