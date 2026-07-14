using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BaldGrabber.Models;
using Serilog;

namespace BaldGrabber.Services;

public partial class DownloadService
{
    private const string SoundCloudFormatSelector =
        "bestaudio[format_id=download]/bestaudio[acodec!=opus]/bestaudio[acodec=opus]";

    private sealed record SoundCloudEntry(string Url, string Title);
    private sealed record SoundCloudCollection(string Title, List<SoundCloudEntry> Entries);

    public async Task<(string path, string title, string actualQuality, bool isCollection)> DownloadSoundCloudAsync(
        string url,
        string outputFolder,
        string? timeFrom,
        string? timeTo,
        IProgress<double> progress,
        CancellationToken cancellationToken,
        Action<string, string>? onSpeedEta = null)
    {
        if (!File.Exists(_ytDlpPath))
            throw new FileNotFoundException("yt-dlp.exe not found", _ytDlpPath);
        if (!File.Exists(_ffmpegPath))
            throw new FileNotFoundException("ffmpeg.exe not found", _ffmpegPath);

        var collection = await GetSoundCloudCollectionAsync(url, cancellationToken);
        if (collection is { Entries.Count: > 0 })
        {
            var folder = await DownloadSoundCloudCollectionAsync(
                collection, outputFolder, timeFrom, timeTo, progress, cancellationToken, onSpeedEta);
            return (folder, collection.Title, string.Empty, true);
        }

        var result = await DownloadSoundCloudTrackAsync(
            url, outputFolder, null, null, timeFrom, timeTo,
            progress, cancellationToken, onSpeedEta);
        return (result.filePath, result.title, result.actualQuality, false);
    }

    private async Task<SoundCloudCollection?> GetSoundCloudCollectionAsync(
        string url,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _ytDlpPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var argument in new[]
                 {
                     "--encoding", "utf-8",
                     "--ignore-config",
                     "--flat-playlist",
                     "--dump-single-json",
                     "--no-download",
                     "--no-warnings",
                     url
                 })
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        timeoutCts.Token.ThrowIfCancellationRequested();
        process.Start();
        using var registration = timeoutCts.Token.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { }
        });

        var outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
        await process.WaitForExitAsync(timeoutCts.Token);
        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            if (!string.IsNullOrWhiteSpace(error))
                Log.Warning("SoundCloud metadata error: {Error}", error.Trim());
            throw new InvalidOperationException($"yt-dlp metadata error code: {process.ExitCode}");
        }

        if (string.IsNullOrWhiteSpace(output))
            return null;

        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        if (!root.TryGetProperty("entries", out var entriesElement) ||
            entriesElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var entries = new List<SoundCloudEntry>();
        foreach (var entry in entriesElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                continue;

            var entryUrl = GetJsonString(entry, "webpage_url")
                           ?? GetJsonString(entry, "original_url")
                           ?? GetJsonString(entry, "url");
            if (string.IsNullOrWhiteSpace(entryUrl) ||
                !Uri.TryCreate(entryUrl, UriKind.Absolute, out _))
            {
                continue;
            }

            var entryTitle = GetJsonString(entry, "title") ?? string.Empty;
            entries.Add(new SoundCloudEntry(entryUrl, entryTitle));
        }

        if (entries.Count == 0)
            return null;

        var title = GetJsonString(root, "title")
                    ?? GetJsonString(root, "playlist_title")
                    ?? "SoundCloud";
        return new SoundCloudCollection(title, entries);
    }

    private async Task<string> DownloadSoundCloudCollectionAsync(
        SoundCloudCollection collection,
        string outputFolder,
        string? timeFrom,
        string? timeTo,
        IProgress<double> progress,
        CancellationToken cancellationToken,
        Action<string, string>? onSpeedEta)
    {
        var playlistFolder = Path.Combine(outputFolder, SanitizeFileName(collection.Title));
        Directory.CreateDirectory(playlistFolder);

        var failed = new List<int>();
        for (var index = 0; index < collection.Entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var capturedIndex = index;
            var trackProgress = new Progress<double>(value =>
            {
                var normalized = value < 0 ? 0 : value;
                progress.Report((capturedIndex + normalized) / collection.Entries.Count);
            });

            if (!await TryDownloadSoundCloudCollectionEntryAsync(
                    collection.Entries[index], index, playlistFolder,
                    timeFrom, timeTo, trackProgress, cancellationToken, onSpeedEta))
            {
                failed.Add(index);
            }
        }

        if (failed.Count > 0)
        {
            var permanentlyFailed = new List<int>();
            foreach (var index in failed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var capturedIndex = index;
                var trackProgress = new Progress<double>(value =>
                {
                    var normalized = value < 0 ? 0 : value;
                    progress.Report((capturedIndex + normalized) / collection.Entries.Count);
                });

                if (!await TryDownloadSoundCloudCollectionEntryAsync(
                        collection.Entries[index], index, playlistFolder,
                        timeFrom, timeTo, trackProgress, cancellationToken, onSpeedEta))
                {
                    permanentlyFailed.Add(index);
                }
            }

            if (permanentlyFailed.Count > 0)
                throw new InvalidOperationException(
                    $"Failed to download {permanentlyFailed.Count} of {collection.Entries.Count} SoundCloud tracks");
        }

        progress.Report(1.0);
        return playlistFolder;
    }

    private async Task<bool> TryDownloadSoundCloudCollectionEntryAsync(
        SoundCloudEntry entry,
        int index,
        string outputFolder,
        string? timeFrom,
        string? timeTo,
        IProgress<double> progress,
        CancellationToken cancellationToken,
        Action<string, string>? onSpeedEta)
    {
        try
        {
            var baseName = $"{index + 1:D2} - {SanitizeFileName(entry.Title)}";
            var existingTrack = Directory.EnumerateFiles(outputFolder)
                .FirstOrDefault(path =>
                    string.Equals(Path.GetFileNameWithoutExtension(path), baseName, StringComparison.OrdinalIgnoreCase) &&
                    new FileInfo(path).Length > 0);
            if (existingTrack != null)
            {
                Log.Information("SoundCloud track already exists, skipping: {Path}", existingTrack);
                progress.Report(1.0);
                return true;
            }

            await DownloadSoundCloudTrackAsync(
                entry.Url, outputFolder, entry.Title, (index + 1).ToString("D2"),
                timeFrom, timeTo, progress, cancellationToken, onSpeedEta);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SoundCloud track failed: {Title}", entry.Title);
            return false;
        }
    }

    private async Task<(string filePath, string title, string actualQuality)> DownloadSoundCloudTrackAsync(
        string url,
        string outputFolder,
        string? titleHint,
        string? trackNumber,
        string? timeFrom,
        string? timeTo,
        IProgress<double> progress,
        CancellationToken cancellationToken,
        Action<string, string>? onSpeedEta)
    {
        var jobId = Guid.NewGuid().ToString("N");
        var tempFilePath = Path.Combine(_tempFolder, $"temp_{jobId}");

        try
        {
            var title = string.IsNullOrWhiteSpace(titleHint)
                ? await GetVideoTitleAsync(url, cancellationToken)
                : titleHint;

            var arguments = new List<string>
            {
                "--encoding", "utf-8",
                "--ignore-config",
                "--no-playlist",
                "--ffmpeg-location", _ffmpegPath,
                "--embed-metadata",
                "-f", SoundCloudFormatSelector,
                "-o", $"{tempFilePath}.%(ext)s",
                url
            };

            var exitCode = await Task.Run(
                () => RunYtDlpCommand(arguments, progress, cancellationToken, onSpeedEta),
                cancellationToken);
            if (exitCode != 0)
                throw new InvalidOperationException($"yt-dlp error code: {exitCode}");

            var downloadedFile = FindDownloadedFile(jobId)
                ?? throw new FileNotFoundException("File not found after SoundCloud download");
            var extension = GetActualExtension(downloadedFile);
            var processedFile = downloadedFile;

            if (!string.IsNullOrWhiteSpace(timeFrom) || !string.IsNullOrWhiteSpace(timeTo))
            {
                progress.Report(-4);
                var trimmedPath = Path.Combine(_tempFolder, $"trimmed_{jobId}{extension}");
                var trimExitCode = await Task.Run(
                    () => RunFfmpegTrim(downloadedFile, trimmedPath, timeFrom, timeTo, cancellationToken),
                    cancellationToken);
                if (trimExitCode != 0)
                    throw new InvalidOperationException($"ffmpeg trim error code: {trimExitCode}");

                File.Delete(downloadedFile);
                processedFile = trimmedPath;
            }

            var baseName = string.IsNullOrWhiteSpace(trackNumber)
                ? SanitizeFileName(title)
                : $"{trackNumber} - {SanitizeFileName(title)}";
            var finalName = GetUniqueFileName(outputFolder, baseName, extension);
            var finalPath = Path.Combine(outputFolder, finalName);

            var thumbnailPath = await DownloadThumbnailAsync(url, jobId, true, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (thumbnailPath != null)
            {
                progress.Report(-5);
                var withThumbnailPath = Path.Combine(_tempFolder, $"with_thumb_{jobId}{extension}");
                var thumbnailExitCode = await Task.Run(
                    () => RunFfmpegEmbedAudioCover(processedFile, thumbnailPath, withThumbnailPath, cancellationToken),
                    cancellationToken);

                if (thumbnailExitCode == 0)
                {
                    File.Delete(processedFile);
                    File.Move(withThumbnailPath, finalPath, overwrite: true);
                }
                else
                {
                    Log.Warning("SoundCloud cover is not supported by container {Extension}", extension);
                    File.Move(processedFile, finalPath, overwrite: true);
                }
            }
            else
            {
                File.Move(processedFile, finalPath, overwrite: true);
            }

            progress.Report(1.0);
            var actualQuality = await GetFileQualityInfoAsync(finalPath, DownloadMode.Audio, cancellationToken);
            return (finalPath, title, actualQuality);
        }
        finally
        {
            CleanupTempFiles(jobId);
        }
    }

    private static string? GetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}
