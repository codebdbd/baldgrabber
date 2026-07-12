using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BaldGrabber.Models;
using Serilog;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace BaldGrabber.Services;

public class DownloadService
{
    private readonly string _ytDlpPath;
    private readonly string _ffmpegPath;
    private readonly string _tempFolder;

    private static readonly TimeSpan TitleTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);

    private static readonly string[] AllowedHosts = ["www.youtube.com", "youtube.com", "youtu.be", "m.youtube.com", "music.youtube.com"];

    public DownloadService()
    {
        var basePath = AppDomain.CurrentDomain.BaseDirectory;
        _ytDlpPath = Path.Combine(basePath, "Tools", "yt-dlp.exe");
        _ffmpegPath = Path.Combine(basePath, "Tools", "ffmpeg.exe");

        var portableDataPath = Path.Combine(basePath, "..", "..", "Data");
        _tempFolder = Directory.Exists(portableDataPath)
            ? Path.Combine(portableDataPath, "Temp")
            : Path.Combine(Path.GetTempPath(), "BaldGrabber");

        if (!Directory.Exists(_tempFolder))
            Directory.CreateDirectory(_tempFolder);
        else
            CleanupStaleTempFiles();
    }

    private void CleanupStaleTempFiles()
    {
        try
        {
            foreach (var pattern in new[] { "temp_*", "thumb_*", "trimmed_*", "with_thumb_*" })
            {
                foreach (var f in Directory.GetFiles(_tempFolder, pattern))
                {
                    var info = new FileInfo(f);
                    if (DateTime.UtcNow - info.LastWriteTimeUtc > TimeSpan.FromHours(2))
                        File.Delete(f);
                }
            }
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to cleanup stale temp files"); }
    }

    public static bool IsValidYouTubeUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != "https" && uri.Scheme != "http") return false;
        return AllowedHosts.Contains(uri.Host);
    }

    public static bool IsPlaylistUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return uri.AbsolutePath.Contains("playlist") || uri.Query.Contains("list=");
    }

    public static bool IsChannelUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!AllowedHosts.Contains(uri.Host)) return false;
        var path = uri.AbsolutePath;
        return path.Contains("/@") || path.Contains("/c/") || path.Contains("/user/") || path.Contains("/channel/");
    }

    public async Task<(string filePath, string title, string actualQuality)> DownloadAsync(
        DownloadMode mode, string url, string quality, string outputFolder,
        string? timeFrom, string? timeTo,
        IProgress<double> progress, CancellationToken cancellationToken,
        Action<string, string>? onSpeedEta = null)
    {
        if (!File.Exists(_ytDlpPath)) throw new FileNotFoundException("yt-dlp.exe not found", _ytDlpPath);
        if (!File.Exists(_ffmpegPath)) throw new FileNotFoundException("ffmpeg.exe not found", _ffmpegPath);

        Log.Information("Download start: {Url}, mode: {Mode}, quality: {Quality}", TruncateUrl(url), mode, quality);

        var jobId = Guid.NewGuid().ToString("N");
        var tempFilePath = Path.Combine(_tempFolder, $"temp_{jobId}");
        string? thumbnailPath = null;

        try
        {
            var qualityArg = mode == DownloadMode.Audio ? GetAudioQualityArg(quality) : GetVideoQualityArg(quality);
            var extension = mode == DownloadMode.Audio ? GetAudioExtension(quality) : ".mp4";

            var title = await GetVideoTitleAsync(url, cancellationToken);
            var safeFileName = SanitizeFileName(title);
            var finalFileName = GetUniqueFileName(outputFolder, safeFileName, extension);
            var finalPath = Path.Combine(outputFolder, finalFileName);

            // Always download thumbnail for audio
            if (mode == DownloadMode.Audio)
            {
                thumbnailPath = await DownloadThumbnailAsync(url, jobId, cancellationToken);
            }

            var exitCode = await Task.Run(() => RunYtDlp(mode, quality, qualityArg, tempFilePath, url, progress, cancellationToken, onSpeedEta: onSpeedEta), cancellationToken);
            if (exitCode != 0) throw new Exception($"yt-dlp error code: {exitCode}");

            var downloadedFile = FindDownloadedFile(jobId);
            if (downloadedFile == null) throw new FileNotFoundException("File not found after download");

            string processedFile;

            // Trim if needed
            if (!string.IsNullOrWhiteSpace(timeFrom) || !string.IsNullOrWhiteSpace(timeTo))
            {
                progress.Report(-4);
                var trimmedPath = Path.Combine(_tempFolder, $"trimmed_{jobId}{extension}");
                var trimExitCode = await Task.Run(() => RunFfmpegTrim(downloadedFile, trimmedPath, timeFrom, timeTo, cancellationToken), cancellationToken);
                if (trimExitCode != 0) throw new Exception($"ffmpeg trim error code: {trimExitCode}");
                File.Delete(downloadedFile);
                processedFile = trimmedPath;
            }
            else
            {
                processedFile = downloadedFile;
            }

            // Always embed thumbnail for audio if available
            if (mode == DownloadMode.Audio && thumbnailPath != null)
            {
                progress.Report(-5);
                var withThumbnailPath = Path.Combine(_tempFolder, $"with_thumb_{jobId}{extension}");
                var thumbExitCode = await Task.Run(() => RunFfmpegEmbedThumbnail(processedFile, thumbnailPath, withThumbnailPath, mode, cancellationToken), cancellationToken);
                if (thumbExitCode != 0)
                {
                    Log.Warning("Failed to embed thumbnail, continuing without it");
                    File.Move(processedFile, finalPath, overwrite: true);      
                }
                else
                {
                    File.Delete(processedFile);
                    File.Move(withThumbnailPath, finalPath, overwrite: true);  
                }
                File.Delete(thumbnailPath);
            }
            else
            {
                File.Move(processedFile, finalPath, overwrite: true);
            }

            progress.Report(1.0);
            var actualQuality = await GetFileQualityInfoAsync(finalPath, mode, cancellationToken);
            return (finalPath, title, actualQuality);
        }
        catch (OperationCanceledException) { CleanupTempFiles(jobId); throw; }
        catch (Exception ex) { Log.Error(ex, "Download error"); CleanupTempFiles(jobId); throw; }
    }

    private static string GetAudioQualityArg(string quality) => quality switch
    {
        "opus" => "bestaudio[acodec=opus]/bestaudio",
        "m4a" => "bestaudio[ext=m4a]/bestaudio",
        "mp3_128" => "bestaudio[abr<=128]/bestaudio",
        "mp3_96" => "bestaudio[abr<=96]/bestaudio",
        _ => "bestaudio/best"
    };

    private static string GetAudioExtension(string quality) => quality switch
    {
        "opus" => ".webm",
        "m4a" => ".m4a",
        _ => ".mp3"
    };

    private static bool NeedsMp3Conversion(string quality) => quality is "mp3_128" or "mp3_96";

    private static string GetVideoQualityArg(string quality) => quality switch
    {
        "Best" => "bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best",
        "2160p" => "bestvideo[height<=2160][ext=mp4]+bestaudio[ext=m4a]/best[height<=2160]",
        "1440p" => "bestvideo[height<=1440][ext=mp4]+bestaudio[ext=m4a]/best[height<=1440]",
        "1080p" => "bestvideo[height<=1080][ext=mp4]+bestaudio[ext=m4a]/best[height<=1080]",
        "720p" => "bestvideo[height<=720][ext=mp4]+bestaudio[ext=m4a]/best[height<=720]",
        "480p" => "bestvideo[height<=480][ext=mp4]+bestaudio[ext=m4a]/best[height<=480]",
        "360p" => "bestvideo[height<=360][ext=mp4]+bestaudio[ext=m4a]/best[height<=360]",
        "240p" => "bestvideo[height<=240][ext=mp4]+bestaudio[ext=m4a]/best[height<=240]",
        _ => "bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]/best"
    };

    public async Task<List<string>> GetAvailableVideoFormatsAsync(string url, CancellationToken ct)
    {
        return await GetAvailableFormatsAsync(url, DownloadMode.Video, ct);
    }

    private static string GetMp3QualityArg(string quality) => quality switch
    {
        "mp3_128" => "128K",
        "mp3_96" => "96K",
        _ => "0"
    };

    private int RunYtDlp(DownloadMode mode, string quality, string qualityArg, string tempFilePath, string url, IProgress<double> progress, CancellationToken ct, bool allowPlaylist = false, Action<string, string>? onSpeedEta = null)
    {
        var playlistArg = allowPlaylist ? Array.Empty<string>() : new[] { "--no-playlist" };
        var args = mode == DownloadMode.Audio
            ? NeedsMp3Conversion(quality)
                ? new[] { "--encoding", "utf-8", "--ffmpeg-location", _ffmpegPath, "-x", "--audio-format", "mp3", "--audio-quality", GetMp3QualityArg(quality), "--postprocessor-args", "-threads 0" }.Concat(playlistArg).Concat(new[] { "-f", qualityArg, "-o", $"{tempFilePath}.%(ext)s", url }).ToArray()
                : new[] { "--encoding", "utf-8", "--ffmpeg-location", _ffmpegPath }.Concat(playlistArg).Concat(new[] { "-f", qualityArg, "-o", $"{tempFilePath}.%(ext)s", url }).ToArray()
            : new[] { "--encoding", "utf-8", "--ffmpeg-location", _ffmpegPath, "-f", qualityArg, "--merge-output-format", "mp4" }.Concat(playlistArg).Concat(new[] { "-o", $"{tempFilePath}.%(ext)s", url }).ToArray();

        using var process = new Process { StartInfo = new ProcessStartInfo { FileName = _ytDlpPath, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);

        process.OutputDataReceived += (_, e) => { if (e.Data != null) ParseProgress(e.Data, progress, onSpeedEta); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) ParseProgress(e.Data, progress, onSpeedEta); };

        using var reg = ct.Register(() => { try { process.Kill(entireProcessTree: true); } catch { } });

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(DownloadTimeout))
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            return -1;
        }
        ct.ThrowIfCancellationRequested();
        return process.ExitCode;
    }

    private async Task<string> GetVideoTitleAsync(string url, CancellationToken ct)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo { FileName = _ytDlpPath, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
        process.StartInfo.ArgumentList.Add("--encoding"); process.StartInfo.ArgumentList.Add("utf-8");
        process.StartInfo.ArgumentList.Add("--no-playlist"); process.StartInfo.ArgumentList.Add("--get-title");
        process.StartInfo.ArgumentList.Add("--quiet"); process.StartInfo.ArgumentList.Add("--no-warnings");
        process.StartInfo.ArgumentList.Add("--ignore-config");
        process.StartInfo.ArgumentList.Add(url);

        using var reg = ct.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } });
        process.Start(); process.BeginErrorReadLine();

        using var reader = new StreamReader(process.StandardOutput.BaseStream, System.Text.Encoding.UTF8);
        var titleOutput = await reader.ReadToEndAsync(ct);
        if (!process.WaitForExit(TitleTimeout)) throw new TimeoutException("yt-dlp title timeout");
        ct.ThrowIfCancellationRequested();
        var result = titleOutput.Trim().Split('\n')[0].Trim();
        if (string.IsNullOrEmpty(result)) throw new Exception("yt-dlp returned empty title");
        return result;
    }

    private static void ParseProgress(string line, IProgress<double> progress, Action<string, string>? onSpeedEta = null)
    {
        if (line.Contains("[ExtractAudio]")) progress.Report(-1);
        else if (line.Contains("[Merger]") || line.Contains("[FixupM3u8]")) progress.Report(-2);
        else if (line.Contains("Deleting original")) progress.Report(-3);

        var match = Regex.Match(line, @"(\d+(?:\.\d+)?)%");
        if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
            progress.Report(Math.Min(p / 100.0, 1.0));

        if (onSpeedEta != null)
        {
            var speed = "";
            var eta = "";
            var speedMatch = Regex.Match(line, @"at\s+([\d.]+\w+/s)");
            if (speedMatch.Success) speed = speedMatch.Groups[1].Value;
            var etaMatch = Regex.Match(line, @"ETA\s+(\d+:\d+(?::\d+)?)");
            if (etaMatch.Success) eta = etaMatch.Groups[1].Value;
            if (speed.Length > 0 || eta.Length > 0)
                onSpeedEta(speed, eta);
        }
    }

    private static string TruncateUrl(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? $"https://{uri.Host}{uri.AbsolutePath}" : url;

    private string? FindDownloadedFile(string jobId)
    {
        var files = Directory.GetFiles(_tempFolder, $"temp_{jobId}*");
        return files.Length == 0 ? null : files.OrderBy(f => new FileInfo(f).LastWriteTimeUtc).Last();
    }

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase) { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" };

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var s = new string(fileName.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
        if (s.Length > 180) s = s[..177] + "...";
        var nameWithoutExt = Path.GetFileNameWithoutExtension(s);
        if (ReservedNames.Contains(nameWithoutExt)) s += "_";
        return s;
    }

    private static string GetUniqueFileName(string folder, string baseName, string ext)
    {
        for (var i = 0; ; i++)
        {
            var name = i == 0 ? $"{baseName}{ext}" : $"{baseName} ({i}){ext}";
            if (!File.Exists(Path.Combine(folder, name))) return name;
        }
    }

    private async Task<string?> DownloadThumbnailAsync(string url, string jobId, CancellationToken cancellationToken)
    {
        try
        {
            var thumbnailPathPattern = Path.Combine(_tempFolder, $"thumb_{jobId}.%(ext)s");
            var args = new List<string>
            {
                "--encoding", "utf-8",
                "--no-playlist",
                "--write-all-thumbnails",
                "--skip-download",
                "--convert-thumbnails", "jpg",
                "--quiet",
                "--no-warnings",
                "-o", thumbnailPathPattern,
                url
            };

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

            foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);

            var output = new System.Text.StringBuilder();
            var error = new System.Text.StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) error.AppendLine(e.Data); };

            using var reg = cancellationToken.Register(() => { try { process.Kill(entireProcessTree: true); } catch { } });
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);

            Log.Information("yt-dlp thumbnail download exit code: {ExitCode}", process.ExitCode);
            if (output.Length > 0) Log.Information("yt-dlp thumbnail output: {Output}", output.ToString());
            if (error.Length > 0) Log.Warning("yt-dlp thumbnail error: {Error}", error.ToString());

            // Find all downloaded thumbnail files
            var possibleFiles = Directory.GetFiles(_tempFolder, $"thumb_{jobId}*");
            Log.Information("Found {Count} possible thumbnail files: {Files}", possibleFiles.Length, string.Join(", ", possibleFiles));

            // Select square thumbnail (width == height) with max size, or largest non-square
            string? selectedThumbnail = null;
            int maxSquareSize = 0;
            int maxSize = 0;
            foreach (var file in possibleFiles)
            {
                try
                {
                    using var image = SixLabors.ImageSharp.Image.Load(file);
                    var size = Math.Max(image.Width, image.Height);
                    if (image.Width == image.Height && image.Width > maxSquareSize)
                    {
                        maxSquareSize = image.Width;
                        selectedThumbnail = file;
                    }
                    else if (size > maxSize && maxSquareSize == 0)
                    {
                        maxSize = size;
                        selectedThumbnail = file;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to check thumbnail dimensions for {File}", file);
                }
            }

            // If no thumbnail found, return null
            if (selectedThumbnail == null)
                return null;

            // Now make sure the selected thumbnail is square: crop the center if it's not
            using var img = SixLabors.ImageSharp.Image.Load(selectedThumbnail);
            if (img.Width != img.Height)
            {
                var squareSize = Math.Min(img.Width, img.Height);
                var cropX = (img.Width - squareSize) / 2;
                var cropY = (img.Height - squareSize) / 2;
                img.Mutate(x => x.Crop(new Rectangle(cropX, cropY, squareSize, squareSize)));
                var squareThumbnailPath = Path.Combine(_tempFolder, $"thumb_{jobId}_square.jpg");
                await img.SaveAsJpegAsync(squareThumbnailPath, cancellationToken);
                Log.Information("Cropped thumbnail to square: {File}", squareThumbnailPath);
                return squareThumbnailPath;
            }

            return selectedThumbnail;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to download thumbnail");
            return null;
        }
    }

    private int RunFfmpegEmbedThumbnail(string inputPath, string thumbnailPath, string outputPath, DownloadMode mode, CancellationToken cancellationToken)
    {
        var args = new List<string> { "-y", "-i", inputPath, "-i", thumbnailPath };

        if (mode == DownloadMode.Audio)
        {
            // For audio: map thumbnail as cover art
            args.AddRange(new[] { "-map", "0", "-map", "1", "-c", "copy", "-disposition:v:0", "attached_pic", outputPath });
        }
        else
        {
            // For video: just copy streams (video already has thumbnail, but we can replace it
            args.AddRange(new[] { "-map", "0", "-map", "1", "-c", "copy", "-disposition:v:1", "attached_pic", outputPath });
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);

        var output = new System.Text.StringBuilder();
        var error = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) error.AppendLine(e.Data); };

        using var reg = cancellationToken.Register(() => { try { process.Kill(entireProcessTree: true); } catch { } });
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        Log.Information("ffmpeg embed thumbnail exit code: {ExitCode}", process.ExitCode);
        if (output.Length > 0) Log.Information("ffmpeg embed thumbnail output: {Output}", output.ToString());
        if (error.Length > 0) Log.Warning("ffmpeg embed thumbnail error: {Error}", error.ToString());

        return process.ExitCode;
    }

    private void CleanupTempFiles(string jobId)
    {
        try
        {
            foreach (var f in Directory.GetFiles(_tempFolder, $"temp_{jobId}*"))
                File.Delete(f);
            foreach (var f in Directory.GetFiles(_tempFolder, $"thumb_{jobId}*"))
                File.Delete(f);
            foreach (var f in Directory.GetFiles(_tempFolder, $"trimmed_{jobId}*"))
                File.Delete(f);
            foreach (var f in Directory.GetFiles(_tempFolder, $"with_thumb_{jobId}*"))
                File.Delete(f);
        }
        catch { }
    }

    private int RunFfmpegTrim(string inputPath, string outputPath, string? timeFrom, string? timeTo, CancellationToken ct)
    {
        var args = new System.Collections.Generic.List<string> { "-y" };
        if (!string.IsNullOrWhiteSpace(timeFrom)) { args.Add("-ss"); args.Add(timeFrom.Trim()); }
        args.Add("-i"); args.Add(inputPath);
        if (!string.IsNullOrWhiteSpace(timeTo)) { args.Add("-to"); args.Add(timeTo.Trim()); }
        args.Add("-c"); args.Add("copy"); args.Add(outputPath);

        using var process = new Process { StartInfo = new ProcessStartInfo { FileName = _ffmpegPath, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);

        using var reg = ct.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } });
        process.Start(); process.BeginErrorReadLine();

        if (!process.WaitForExit(TimeSpan.FromMinutes(10))) { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } return -1; }
        ct.ThrowIfCancellationRequested();
        return process.ExitCode;
    }

    public async Task<List<string>> GetAvailableFormatsAsync(string url, DownloadMode mode, CancellationToken ct)
    {
        var result = new List<string>();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            using var process = new Process { StartInfo = new ProcessStartInfo { FileName = _ytDlpPath, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
            process.StartInfo.ArgumentList.Add("--encoding"); process.StartInfo.ArgumentList.Add("utf-8");
            process.StartInfo.ArgumentList.Add("--no-playlist"); process.StartInfo.ArgumentList.Add("--dump-json");
            process.StartInfo.ArgumentList.Add("--no-download");
            process.StartInfo.ArgumentList.Add("--quiet"); process.StartInfo.ArgumentList.Add("--no-warnings");
            process.StartInfo.ArgumentList.Add("--ignore-config");
            process.StartInfo.ArgumentList.Add(url);

            var output = new System.Text.StringBuilder();
            var error = new System.Text.StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) error.AppendLine(e.Data); };

            using var reg = timeoutCts.Token.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } });
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(timeoutCts.Token);

            Log.Information("yt-dlp get formats exit code: {ExitCode}", process.ExitCode);
            if (error.Length > 0) Log.Warning("yt-dlp get formats error: {Error}", error.ToString());
            Log.Information("yt-dlp get formats output length: {Length}", output.Length);

            var json = output.ToString();
            if (string.IsNullOrWhiteSpace(json))
            {
                Log.Warning("yt-dlp returned empty json");
                return result;
            }

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("formats", out var formats))
            {
                Log.Warning("No 'formats' property in yt-dlp json");
                return result;
            }

            if (mode == DownloadMode.Audio)
            {
                var hasOpus = false;
                var hasAac = false;
                var maxAbr = 0;

                foreach (var fmt in formats.EnumerateArray())
                {
                    if (fmt.TryGetProperty("acodec", out var ac) && ac.GetString() == "opus") hasOpus = true;
                    if (fmt.TryGetProperty("acodec", out var ac2) && ac2.GetString()?.StartsWith("mp4a") == true) hasAac = true;
                    if (fmt.TryGetProperty("abr", out var abr) && abr.GetDouble() > maxAbr)
                        maxAbr = (int)abr.GetDouble();
                }

                if (hasOpus) result.Add("opus");
                if (hasAac) result.Add("m4a");
                if (maxAbr >= 128) result.Add("mp3_128");
                if (maxAbr >= 96) result.Add("mp3_96");
            }
            else
            {
                var videoHeights = new HashSet<int>();
                var hasAudio = false;

                foreach (var fmt in formats.EnumerateArray())
                {
                    if (fmt.TryGetProperty("acodec", out var ac) && ac.GetString() != "none" && ac.GetString() != "")
                        hasAudio = true;

                    if (fmt.TryGetProperty("vcodec", out var vc) && vc.GetString() != "none" && vc.GetString() != "")
                    {
                        if (fmt.TryGetProperty("height", out var h) && h.GetInt32() > 0)
                            videoHeights.Add(h.GetInt32());
                    }
                }

                Log.Information("Video mode: hasAudio={HasAudio}, videoHeights={Heights}", hasAudio, string.Join(", ", videoHeights));

                if (hasAudio)
                {
                    foreach (var h in videoHeights.OrderByDescending(x => x))
                        result.Add($"{h}p");
                }
                else
                {
                    var heights = new HashSet<int>();
                    foreach (var fmt in formats.EnumerateArray())
                    {
                        if (fmt.TryGetProperty("height", out var h) && h.GetInt32() > 0)
                            heights.Add(h.GetInt32());
                    }
                    Log.Information("Video mode (no audio): heights={Heights}", string.Join(", ", heights));
                    foreach (var h in heights.OrderByDescending(x => x))
                        result.Add($"{h}p");
                }
            }

            Log.Information("GetAvailableFormatsAsync result: {Result}", string.Join(", ", result));
        }
        catch (Exception ex) { Log.Error(ex, "Failed to get available formats"); }
        return result;
    }

    private async Task<string> GetFileQualityInfoAsync(string filePath, DownloadMode mode, CancellationToken ct)
    {
        try
        {
            using var process = new Process { StartInfo = new ProcessStartInfo { FileName = _ffmpegPath, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
            var ffprobePath = Path.Combine(Path.GetDirectoryName(_ffmpegPath)!, "ffprobe.exe");
            if (!File.Exists(ffprobePath)) return string.Empty;

            process.StartInfo.FileName = ffprobePath;
            process.StartInfo.ArgumentList.Add("-v"); process.StartInfo.ArgumentList.Add("quiet");
            process.StartInfo.ArgumentList.Add("-print_format"); process.StartInfo.ArgumentList.Add("json");
            process.StartInfo.ArgumentList.Add("-show_streams"); process.StartInfo.ArgumentList.Add(filePath);

            using var reg = ct.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } });
            process.Start();

            using var reader = new StreamReader(process.StandardOutput.BaseStream, System.Text.Encoding.UTF8);
            var output = await reader.ReadToEndAsync(ct);
            if (!process.WaitForExit(TimeSpan.FromSeconds(10))) return string.Empty;

            if (mode == DownloadMode.Audio)
            {
                if (output.Contains("\"bit_rate\""))
                {
                    var match = Regex.Match(output, "\"bit_rate\"\\s*:\\s*\"(\\d+)\"");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var bps))
                        return $"{bps / 1000} kbps";
                }
            }
            else
            {
                if (output.Contains("\"height\""))
                {
                    var match = Regex.Match(output, "\"height\"\\s*:\\s*(\\d+)");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var height))
                        return $"{height}p";
                }
            }
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to get file quality info"); }
        return string.Empty;
    }

    public async Task<(string playlistTitle, List<(string url, string title)> tracks)> GetPlaylistTracksAsync(string url, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));

        using var process = new Process { StartInfo = new ProcessStartInfo { FileName = _ytDlpPath, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
        process.StartInfo.ArgumentList.Add("--encoding"); process.StartInfo.ArgumentList.Add("utf-8");
        process.StartInfo.ArgumentList.Add("--flat-playlist"); process.StartInfo.ArgumentList.Add("--dump-json");
        process.StartInfo.ArgumentList.Add("--no-download"); process.StartInfo.ArgumentList.Add("--no-warnings");
        process.StartInfo.ArgumentList.Add("--ignore-config");
        process.StartInfo.ArgumentList.Add(url);

        var output = new System.Text.StringBuilder();
        var error = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) error.AppendLine(e.Data); };

        using var reg = timeoutCts.Token.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } });
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(timeoutCts.Token);

        if (error.Length > 0) Log.Warning("yt-dlp playlist error: {Error}", error.ToString());

        var playlistTitle = "Playlist";
        var tracks = new List<(string url, string title)>();

        foreach (var line in output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (root.TryGetProperty("playlist_title", out var playlistTitleProp) &&
                    !string.IsNullOrWhiteSpace(playlistTitleProp.GetString()))
                {
                    playlistTitle = playlistTitleProp.GetString()!;
                }
                else if (playlistTitle == "Playlist" &&
                         root.TryGetProperty("channel", out var channelProp) &&
                         !string.IsNullOrWhiteSpace(channelProp.GetString()))
                {
                    playlistTitle = channelProp.GetString()!;
                }

                if (root.TryGetProperty("url", out var urlProp))
                {
                    var trackUrl = urlProp.GetString();
                    var trackTitle = root.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(trackUrl))
                        tracks.Add((trackUrl, trackTitle));
                }
            }
            catch (Exception ex) { Log.Warning(ex, "Failed to parse playlist track"); }
        }

        Log.Information("Playlist '{Title}': {Count} tracks found", playlistTitle, tracks.Count);
        return (playlistTitle, tracks);
    }

    public async Task<string> DownloadPlaylistAsync(
        DownloadMode mode, string playlistUrl, string quality, string outputFolder,
        IProgress<double> progress, CancellationToken ct,
        Action<string, string>? onSpeedEta = null)
    {
        if (!File.Exists(_ytDlpPath)) throw new FileNotFoundException("yt-dlp.exe not found", _ytDlpPath);
        if (!File.Exists(_ffmpegPath)) throw new FileNotFoundException("ffmpeg.exe not found", _ffmpegPath);

        var (playlistTitle, tracks) = await GetPlaylistTracksAsync(playlistUrl, ct);
        if (tracks.Count == 0) throw new Exception("No tracks found in playlist");

        var safePlaylistName = SanitizeFileName(playlistTitle);
        var playlistFolder = Path.Combine(outputFolder, safePlaylistName);
        if (!Directory.Exists(playlistFolder))
            Directory.CreateDirectory(playlistFolder);

        var qualityArg = mode == DownloadMode.Audio ? GetAudioQualityArg(quality) : GetVideoQualityArg(quality);
        var extension = mode == DownloadMode.Audio ? GetAudioExtension(quality) : ".mp4";

        var failedTracks = new List<(int index, string url, string title)>();

        for (var i = 0; i < tracks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var (trackUrl, trackTitle) = tracks[i];
            var trackNumber = (i + 1).ToString("D2");
            var safeTrackName = SanitizeFileName(trackTitle);
            var trackFileName = $"{trackNumber} - {safeTrackName}{extension}";
            var finalPath = Path.Combine(playlistFolder, trackFileName);

            if (File.Exists(finalPath))
            {
                Log.Information("Track {Index}/{Total} already exists, skipping: {Title}", i + 1, tracks.Count, trackTitle);
                progress.Report((double)(i + 1) / tracks.Count);
                continue;
            }

            Log.Information("Playlist track {Index}/{Total}: {Title}", i + 1, tracks.Count, trackTitle);

            var trackProgress = new Progress<double>(p =>
            {
                var overallProgress = ((double)i + Math.Max(p, 0)) / tracks.Count;
                progress.Report(overallProgress);
            });

            var success = await DownloadPlaylistTrack(mode, quality, qualityArg, extension, trackUrl, trackTitle, trackNumber, playlistFolder, trackProgress, onSpeedEta, ct);
            if (!success)
                failedTracks.Add((i, trackUrl, trackTitle));
        }

        if (failedTracks.Count > 0)
        {
            Log.Information("Retrying {Count} failed tracks", failedTracks.Count);
            var permanentlyFailedTracks = new List<(int index, string url, string title)>();
            foreach (var (index, trackUrl, trackTitle) in failedTracks)
            {
                ct.ThrowIfCancellationRequested();

                var trackNumber = (index + 1).ToString("D2");
                var safeTrackName = SanitizeFileName(trackTitle);
                var trackFileName = $"{trackNumber} - {safeTrackName}{extension}";
                var finalPath = Path.Combine(playlistFolder, trackFileName);

                if (File.Exists(finalPath)) continue;

                Log.Information("Retry track {Index}/{Total}: {Title}", index + 1, tracks.Count, trackTitle);

                var trackProgress = new Progress<double>(p =>
                {
                    var overallProgress = ((double)index + Math.Max(p, 0)) / tracks.Count;
                    progress.Report(overallProgress);
                });

                var success = await DownloadPlaylistTrack(mode, quality, qualityArg, extension, trackUrl, trackTitle, trackNumber, playlistFolder, trackProgress, onSpeedEta, ct);
                if (!success)
                    permanentlyFailedTracks.Add((index, trackUrl, trackTitle));
            }

            if (permanentlyFailedTracks.Count > 0)
                throw new InvalidOperationException($"Failed to download {permanentlyFailedTracks.Count} of {tracks.Count} playlist items");
        }

        progress.Report(1.0);
        return playlistFolder;
    }

    private async Task<bool> DownloadPlaylistTrack(
        DownloadMode mode, string quality, string qualityArg, string extension,
        string trackUrl, string trackTitle, string trackNumber, string playlistFolder,
        IProgress<double> progress, Action<string, string>? onSpeedEta, CancellationToken ct)
    {
        var jobId = Guid.NewGuid().ToString("N");
        var tempFilePath = Path.Combine(_tempFolder, $"temp_{jobId}");
        var safeTrackName = SanitizeFileName(trackTitle);
        var trackFileName = $"{trackNumber} - {safeTrackName}{extension}";
        var finalPath = Path.Combine(playlistFolder, trackFileName);

        using var trackCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        trackCts.CancelAfter(DownloadTimeout);

        try
        {
            var exitCode = await Task.Run(() => RunYtDlp(mode, quality, qualityArg, tempFilePath, trackUrl, progress, trackCts.Token, allowPlaylist: false, onSpeedEta: onSpeedEta), trackCts.Token);
            if (exitCode != 0)
            {
                Log.Warning("yt-dlp error for track {Title}: code {Code}", trackTitle, exitCode);
                return false;
            }

            var downloadedFile = FindDownloadedFile(jobId);
            if (downloadedFile == null)
            {
                Log.Warning("File not found after download for track {Title}", trackTitle);
                return false;
            }

            if (mode == DownloadMode.Audio)
            {
                var thumbnailPath = await DownloadThumbnailAsync(trackUrl, jobId, trackCts.Token);
                if (thumbnailPath != null)
                {
                    var withThumbPath = Path.Combine(_tempFolder, $"with_thumb_{jobId}{extension}");
                    var thumbExit = await Task.Run(() => RunFfmpegEmbedThumbnail(downloadedFile, thumbnailPath, withThumbPath, mode, trackCts.Token), trackCts.Token);
                    if (thumbExit == 0)
                    {
                        File.Delete(downloadedFile);
                        File.Move(withThumbPath, finalPath, overwrite: true);
                    }
                    else
                    {
                        File.Move(downloadedFile, finalPath, overwrite: true);
                    }
                    File.Delete(thumbnailPath);
                }
                else
                {
                    File.Move(downloadedFile, finalPath, overwrite: true);
                }
            }
            else
            {
                File.Move(downloadedFile, finalPath, overwrite: true);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Track {Title} timed out or cancelled, will retry", trackTitle);
            CleanupTempFiles(jobId);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error downloading track {Title}", trackTitle);
            CleanupTempFiles(jobId);
            return false;
        }
        finally
        {
            CleanupTempFiles(jobId);
        }
    }
}
