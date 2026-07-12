# Troubleshooting

## Link not recognized

**Cause:** The link does not point to YouTube or is in an incorrect format.

**Solution:**
- Make sure the link starts with `https://www.youtube.com/`, `https://youtube.com/`, `https://youtu.be/`, `https://m.youtube.com/` or `https://music.youtube.com/`
- Copy the link again from the browser address bar
- Do not use shortened links (bit.ly, etc.)

## Formats not loading / not showing in list

**Cause:** YouTube is temporarily unavailable, API limit has been reached, or the video has been deleted/made private.

**Solution:**
- Check if the video opens in a browser
- Wait a few minutes and try again
- If the video format list is empty, try a different format or select another video

## Download not starting

**Cause:** The "Download" button is inactive.

**Solution:**
- Check that the YouTube link is pasted correctly
- Check that the save folder is selected and exists
- In video mode, wait for format detection to complete (the quality list should load)
- Make sure a quality format is selected (the list should not show "...")

## yt-dlp not found

**Cause:** The `yt-dlp.exe` file is missing from the `Tools` folder next to the app.

**Solution:**
- Check that the `Tools` folder exists next to `BaldGrabber.exe`
- It should contain `yt-dlp.exe` and `ffmpeg.exe`
- If the files are missing — download the latest version of yt-dlp from https://github.com/yt-dlp/yt-dlp/releases and place `yt-dlp.exe` in the `Tools` folder

## FFmpeg not found

**Cause:** The `ffmpeg.exe` file is missing from the `Tools` folder next to the app.

**Solution:**
- Download a static build of FFmpeg from https://www.gyan.dev/ffmpeg/builds/
- Extract `ffmpeg.exe` and place it in the `Tools` folder next to `BaldGrabber.exe`
- Video downloads and MP3 conversion do not work without FFmpeg

## Video downloaded without sound

**Cause:** YouTube delivers video and audio as separate streams, and merging was not performed.

**Solution:**
- Make sure FFmpeg is in the `Tools` folder
- Try selecting a different video quality
- The log may show a `[Merger]` error — this indicates a problem with FFmpeg

## Audio and video merge error

**Cause:** Incompatible stream formats or missing FFmpeg.

**Solution:**
- Make sure you are using the latest version of FFmpeg
- Try selecting a different resolution
- If the error persists — select "Best" as video quality

## Not enough disk space

**Cause:** Insufficient free space on the disk.

**Solution:**
- Video in 4K can be several gigabytes
- Check free space on the target disk
- Select a folder on a disk with sufficient space

## No access to folder

**Cause:** The app does not have write permissions for the selected folder.

**Solution:**
- Select a different folder (e.g., "Downloads" or "Documents")
- Do not use system folders (Windows, Program Files)
- Check if the folder is open in another process

## App stopped working after YouTube updates

**Cause:** YouTube changed its internal format, yt-dlp is outdated.

**Solution:**
- Download the latest version of yt-dlp from https://github.com/yt-dlp/yt-dlp/releases
- Replace `yt-dlp.exe` in the `Tools` folder
- Restart the app

## Antivirus blocking the file

**Cause:** Antivirus detects yt-dlp as a potentially unwanted program.

**Solution:**
- Add the BaldGrabber folder to antivirus exceptions
- This is a false positive — yt-dlp does not contain malicious code
- If needed, download yt-dlp from the official repository and verify the hash

## App doesn't launch / crashes immediately

**Cause:** Required Windows components are missing.

**Solution:**
- Install the latest Windows 10/11 update package
- Install the Windows App SDK runtime from https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads
- Make sure the system is 64-bit

## Playlist not downloading completely

**Cause:** Unstable connection or temporary YouTube error.

**Solution:**
- The app automatically retries failed tracks
- For large playlists — ensure a stable internet connection
- You can retry the download — already downloaded tracks will be skipped

---

# Решение проблем

## Ссылка не распознаётся

**Причина:** Ссылка ведёт не на YouTube или введена в неверном формате.

**Решение:**
- Убедитесь, что ссылка начинается с `https://www.youtube.com/`, `https://youtube.com/`, `https://youtu.be/`, `https://m.youtube.com/` или `https://music.youtube.com/`
- Скопируйте ссылку заново из адресной строки браузера
- Не используйте сокращённые ссылки (bit.ly и т.д.)

## Форматы не загружаются / не отображаются в списке

**Причина:** YouTube временно недоступен, истёк API-лимит или видео было удалено/сделано приватным.

**Решение:**
- Проверьте, открывается ли видео в браузере
- Подождите несколько минут и попробуйте снова
- Если список видео-форматов пуст, попробуйте другой формат или выберите другое видео

## Загрузка не начинается

**Причина:** Кнопка «Скачать» неактивна.

**Решение:**
- Проверьте, что ссылка на YouTube вставлена корректно
- Проверьте, что папка для сохранения выбрана и существует
- В режиме видео дождитесь окончания проверки форматов (список качества должен загрузиться)
- Убедитесь, что выбран формат качества (в списке не должно быть «...»)

## yt-dlp не найден

**Причина:** Файл `yt-dlp.exe` отсутствует в папке `Tools` рядом с программой.

**Решение:**
- Проверьте, что папка `Tools` существует рядом с `BaldGrabber.exe`
- В ней должны быть `yt-dlp.exe` и `ffmpeg.exe`
- Если файлы отсутствуют — скачайте последнюю версию yt-dlp с https://github.com/yt-dlp/yt-dlp/releases и поместите `yt-dlp.exe` в папку `Tools`

## FFmpeg не найден

**Причина:** Файл `ffmpeg.exe` отсутствует в папке `Tools` рядом с программой.

**Решение:**
- Скачайте статическую сборку FFmpeg с https://www.gyan.dev/ffmpeg/builds/
- Извлеките `ffmpeg.exe` и поместите в папку `Tools` рядом с `BaldGrabber.exe`
- Видео-загрузка и конвертация в MP3 не работают без FFmpeg

## Видео скачалось без звука

**Причина:** YouTube раздаёт видео и аудио отдельными потоками, объединение не выполнено.

**Решение:**
- Убедитесь, что FFmpeg находится в папке `Tools`
- Попробуйте выбрать другое качество видео
- В логе может быть ошибка `[Merger]` — это указывает на проблему с FFmpeg

## Ошибка объединения (склейки) аудио и видео

**Причина:** Несовместимые форматы потоков или отсутствует FFmpeg.

**Решение:**
- Убедитесь, что используете последнюю версию FFmpeg
- Попробуйте выбрать другое разрешение
- Если ошибка повторяется — выберите «Best» в качестве видео

## Нет места на диске

**Причина:** На диске недостаточно свободного пространства.

**Решение:**
- Видео в 4K может весить несколько гигабайт
- Проверьте свободное место на целевом диске
- Выберите папку на диске с достаточным объёмом

## Нет доступа к папке

**Причина:** Программа не имеет прав на запись в выбранную папку.

**Решение:**
- Выберите другую папку (например, «Загрузки» или «Документы»)
- Не используйте системные папки (Windows, Program Files)
- Проверьте, не открыта ли папка другим процессом

## Программа перестала работать после обновлений YouTube

**Причина:** YouTube изменил внутренний формат, yt-dlp устарел.

**Решение:**
- Скачайте последнюю версию yt-dlp с https://github.com/yt-dlp/yt-dlp/releases
- Замените `yt-dlp.exe` в папке `Tools`
- Перезапустите программу

## Антивирус блокирует файл

**Причина:** Антивирус распознаёт yt-dlp как потенциально нежелательную программу.

**Решение:**
- Добавьте папку BaldGrabber в исключения антивируса
- Это ложное срабатывание — yt-dlp не содержит вредоносного кода
- При необходимости скачайте yt-dlp с официального репозитория и проверьте хеш

## Программа не запускается / вылетает сразу

**Причина:** Отсутствуют необходимые компоненты Windows.

**Решение:**
- Установите последний пакет обновлений Windows 10/11
- Установите Windows App SDK runtime с https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads
- Убедитесь, что система 64-разрядная

## Плейлист скачивается не полностью

**Причина:** Нестабильное соединение или временная ошибка YouTube.

**Решение:**
- Программа автоматически повторяет неудачные треки
- Если плейлист большой — убедитесь в стабильности интернета
- Скачивание можно повторить — уже скачанные треки пропускаются
