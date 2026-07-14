using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BaldGrabber.Models;
using Serilog;

namespace BaldGrabber.Services;

public partial class DownloadService
{
    private async Task<(string filePath, string title, string actualQuality)> DownloadBestExternalVideoAsync(
        string url,
        string outputFolder,
        string? timeFrom,
        string? timeTo,
        IProgress<double> progress,
        CancellationToken cancellationToken,
        Action<string, string>? onSpeedEta)
    {
        if (!File.Exists(_ytDlpPath))
            throw new FileNotFoundException("yt-dlp.exe not found", _ytDlpPath);
        if (!File.Exists(_ffmpegPath))
            throw new FileNotFoundException("ffmpeg.exe not found", _ffmpegPath);

        var jobId = Guid.NewGuid().ToString("N");
        var tempFilePath = Path.Combine(_tempFolder, $"temp_{jobId}");

        try
        {
            var title = await GetVideoTitleAsync(url, cancellationToken);
            var arguments = new List<string>
            {
                "--encoding", "utf-8",
                "--ignore-config",
                "--no-playlist",
                "--ffmpeg-location", _ffmpegPath,
                "-f", "bestvideo*+bestaudio/best",
                "--merge-output-format", "mp4",
                "-o", $"{tempFilePath}.%(ext)s",
                url
            };

            var exitCode = await Task.Run(
                () => RunYtDlpCommand(arguments, progress, cancellationToken, onSpeedEta),
                cancellationToken);
            if (exitCode != 0)
                throw new InvalidOperationException($"yt-dlp error code: {exitCode}");

            var downloadedFile = FindDownloadedFile(jobId)
                ?? throw new FileNotFoundException("File not found after download");
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

            var safeTitle = SanitizeFileName(title);
            var finalName = GetUniqueFileName(outputFolder, safeTitle, extension);
            var finalPath = Path.Combine(outputFolder, finalName);
            File.Move(processedFile, finalPath, overwrite: true);

            progress.Report(1.0);
            var actualQuality = await GetFileQualityInfoAsync(finalPath, DownloadMode.Video, cancellationToken);
            return (finalPath, title, actualQuality);
        }
        catch (OperationCanceledException)
        {
            CleanupTempFiles(jobId);
            throw;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "External video download error for {Url}", TruncateUrl(url));
            CleanupTempFiles(jobId);
            throw;
        }
    }
}
