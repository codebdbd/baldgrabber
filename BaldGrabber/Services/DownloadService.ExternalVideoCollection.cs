using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace BaldGrabber.Services;

public partial class DownloadService
{
    private async Task<(string path, string title, string actualQuality, bool isCollection)> DownloadExternalVideoSourceAsync(
        string url,
        string outputFolder,
        string sourceName,
        string? timeFrom,
        string? timeTo,
        IProgress<double> progress,
        CancellationToken cancellationToken,
        Action<string, string>? onSpeedEta)
    {
        var collection = await GetExternalMediaCollectionAsync(url, sourceName, cancellationToken);
        if (collection is { Entries.Count: > 0 })
        {
            var folder = await DownloadExternalVideoCollectionAsync(
                collection, outputFolder, sourceName, timeFrom, timeTo,
                progress, cancellationToken, onSpeedEta);
            return (folder, collection.Title, string.Empty, true);
        }

        var result = await DownloadBestExternalVideoAsync(
            url, outputFolder, timeFrom, timeTo, progress, cancellationToken, onSpeedEta);
        return (result.filePath, result.title, result.actualQuality, false);
    }

    private async Task<string> DownloadExternalVideoCollectionAsync(
        ExternalMediaCollection collection,
        string outputFolder,
        string sourceName,
        string? timeFrom,
        string? timeTo,
        IProgress<double> progress,
        CancellationToken cancellationToken,
        Action<string, string>? onSpeedEta)
    {
        var collectionFolder = Path.Combine(outputFolder, SanitizeFileName(collection.Title));
        Directory.CreateDirectory(collectionFolder);

        var failed = new List<int>();
        for (var index = 0; index < collection.Entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await TryDownloadExternalVideoEntryAsync(
                    collection.Entries[index], index, collection.Entries.Count,
                    collectionFolder, sourceName, timeFrom, timeTo,
                    progress, cancellationToken, onSpeedEta))
            {
                failed.Add(index);
            }
        }

        var permanentlyFailed = new List<int>();
        foreach (var index in failed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await TryDownloadExternalVideoEntryAsync(
                    collection.Entries[index], index, collection.Entries.Count,
                    collectionFolder, sourceName, timeFrom, timeTo,
                    progress, cancellationToken, onSpeedEta))
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

    private async Task<bool> TryDownloadExternalVideoEntryAsync(
        ExternalMediaEntry entry,
        int index,
        int totalCount,
        string outputFolder,
        string sourceName,
        string? timeFrom,
        string? timeTo,
        IProgress<double> progress,
        CancellationToken cancellationToken,
        Action<string, string>? onSpeedEta)
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
            var result = await DownloadBestExternalVideoAsync(
                entry.Url, outputFolder, timeFrom, timeTo,
                itemProgress, cancellationToken, onSpeedEta);

            var extension = Path.GetExtension(result.filePath);
            var numberedBaseName = $"{index + 1:D2} - {Path.GetFileNameWithoutExtension(result.filePath)}";
            var numberedName = GetUniqueFileName(outputFolder, numberedBaseName, extension);
            File.Move(result.filePath, Path.Combine(outputFolder, numberedName), overwrite: true);
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
}
