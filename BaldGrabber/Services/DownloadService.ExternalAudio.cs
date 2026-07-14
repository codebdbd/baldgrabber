using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BaldGrabber.Models;
using Serilog;

namespace BaldGrabber.Services;

public partial class DownloadService
{
    private const string ExternalAudioFormatSelector =
        "bestaudio[format_id=download]/bestaudio[acodec!=opus]/bestaudio[acodec=opus]";

    private async Task<(string path, string title, string actualQuality, bool isCollection)> DownloadExternalAudioSourceAsync(
        string url,
        string outputFolder,
        string sourceName,
        string? timeFrom,
        string? timeTo,
        IProgress<double> progress,
        CancellationToken cancellationToken,
        Action<string, string>? onSpeedEta,
        string? formatSelector = null)
    {
        if (!File.Exists(_ytDlpPath))
            throw new FileNotFoundException("yt-dlp.exe not found", _ytDlpPath);
        if (!File.Exists(_ffmpegPath))
            throw new FileNotFoundException("ffmpeg.exe not found", _ffmpegPath);

        var collection = await GetExternalMediaCollectionAsync(url, sourceName, cancellationToken);
        if (collection is { Entries.Count: > 0 })
        {
            var folder = await DownloadExternalAudioCollectionAsync(
                collection, outputFolder, sourceName, timeFrom, timeTo,
                progress, cancellationToken, onSpeedEta, formatSelector);
            return (folder, collection.Title, string.Empty, true);
        }

        var result = await DownloadExternalAudioTrackAsync(
            url, outputFolder, sourceName, null, null, timeFrom, timeTo,
            progress, cancellationToken, onSpeedEta, formatSelector);
        return (result.filePath, result.title, result.actualQuality, false);
    }

    private async Task<string> DownloadExternalAudioCollectionAsync(
        ExternalMediaCollection collection,
        string outputFolder,
        string sourceName,
        string? timeFrom,
        string? timeTo,
        IProgress<double> progress,
        CancellationToken cancellationToken,
        Action<string, string>? onSpeedEta,
        string? formatSelector)
    {
        var collectionFolder = Path.Combine(outputFolder, SanitizeFileName(collection.Title));
        Directory.CreateDirectory(collectionFolder);

        var failed = new List<int>();
        for (var index = 0; index < collection.Entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await TryDownloadExternalAudioEntryAsync(
                    collection.Entries[index], index, collection.Entries.Count,
                    collectionFolder, sourceName, timeFrom, timeTo,
                    progress, cancellationToken, onSpeedEta, formatSelector))
            {
                failed.Add(index);
            }
        }

        var permanentlyFailed = new List<int>();
        foreach (var index in failed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await TryDownloadExternalAudioEntryAsync(
                    collection.Entries[index], index, collection.Entries.Count,
                    collectionFolder, sourceName, timeFrom, timeTo,
                    progress, cancellationToken, onSpeedEta, formatSelector))
            {
                permanentlyFailed.Add(index);
            }
        }

        if (permanentlyFailed.Count > 0)
            throw new InvalidOperationException(
                $"Failed to download {permanentlyFailed.Count} of {collection.Entries.Count} {sourceName} items");

        progress.Report(1.0);
        return collectionFolder;
    }

    private async Task<bool> TryDownloadExternalAudioEntryAsync(
        ExternalMediaEntry entry,
        int index,
        int totalCount,
        string outputFolder,
        string sourceName,
        string? timeFrom,
        string? timeTo,
        IProgress<double> progress,
        CancellationToken cancellationToken,
        Action<string, string>? onSpeedEta,
        string? formatSelector)
    {
        try
        {
            var prefix = $"{index + 1:D2} - ";
            var existingItem = Directory.EnumerateFiles(outputFolder)
                .FirstOrDefault(path =>
                    Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    new FileInfo(path).Length > 0);
            if (existingItem != null)
            {
                progress.Report((index + 1d) / totalCount);
                return true;
            }

            var itemProgress = new Progress<double>(value =>
            {
                var normalized = value < 0 ? 0 : value;
                progress.Report((index + normalized) / totalCount);
            });
            await DownloadExternalAudioTrackAsync(
                entry.Url, outputFolder, sourceName, entry.Title, (index + 1).ToString("D2"),
                timeFrom, timeTo, itemProgress, cancellationToken, onSpeedEta, formatSelector);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "{Source} item failed: {Title}", sourceName, entry.Title);
            return false;
        }
    }

    private async Task<(string filePath, string title, string actualQuality)> DownloadExternalAudioTrackAsync(
        string url,
        string outputFolder,
        string sourceName,
        string? titleHint,
        string? trackNumber,
        string? timeFrom,
        string? timeTo,
        IProgress<double> progress,
        CancellationToken cancellationToken,
        Action<string, string>? onSpeedEta,
        string? formatSelector)
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
                "-f", formatSelector ?? ExternalAudioFormatSelector,
                "-o", $"{tempFilePath}.%(ext)s",
                url
            };

            var exitCode = await Task.Run(
                () => RunYtDlpCommand(arguments, progress, cancellationToken, onSpeedEta),
                cancellationToken);
            if (exitCode != 0)
                throw new InvalidOperationException($"yt-dlp error code: {exitCode}");

            var downloadedFile = FindDownloadedFile(jobId)
                ?? throw new FileNotFoundException($"File not found after {sourceName} download");
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
                    Log.Warning("{Source} cover is not supported by container {Extension}", sourceName, extension);
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
}
