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
            foreach (var f in Directory.GetFiles(_tempFolder, "temp_*"))
            {
                var info = new FileInfo(f);
                if (DateTime.UtcNow - info.LastWriteTimeUtc > TimeSpan.FromHours(2))
                    File.Delete(f);
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
        return uri.AbsolutePath.Contains("playlist");
    }

    public async Task<(string filePath, string title, string actualQuality)> DownloadAsync(
        DownloadMode mode, string url, string quality, string outputFolder,
        string? timeFrom, string? timeTo,
        IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (!File.Exists(_ytDlpPath)) throw new FileNotFoundException("yt-dlp.exe not found", _ytDlpPath);
        if (!File.Exists(_ffmpegPath)) throw new FileNotFoundException("ffmpeg.exe not found", _ffmpegPath);

        Log.Information("Download start: {Url}, mode: {Mode}, quality: {Quality}", TruncateUrl(url), mode, quality);

        var jobId = Guid.NewGuid().ToString("N");
        var tempFilePath = Path.Combine(_tempFolder, $"temp_{jobId}");

        try
        {
            var qualityArg = mode == DownloadMode.Audio ? GetAudioQualityArg(quality) : GetVideoQualityArg(quality);
            var extension = mode == DownloadMode.Audio ? GetAudioExtension(quality) : ".mp4";

            var title = await GetVideoTitleAsync(url, cancellationToken);
            var safeFileName = SanitizeFileName(title);
            var finalFileName = GetUniqueFileName(outputFolder, safeFileName, extension);
            var finalPath = Path.Combine(outputFolder, finalFileName);

            var exitCode = await Task.Run(() => RunYtDlp(mode, quality, qualityArg, tempFilePath, url, progress, cancellationToken), cancellationToken);
            if (exitCode != 0) throw new Exception($"yt-dlp error code: {exitCode}");

            var downloadedFile = FindDownloadedFile(jobId);
            if (downloadedFile == null) throw new FileNotFoundException("File not found after download");

            if (!string.IsNullOrWhiteSpace(timeFrom) || !string.IsNullOrWhiteSpace(timeTo))
            {
                progress.Report(-4);
                var trimmedPath = Path.Combine(_tempFolder, $"trimmed_{jobId}{extension}");
                var trimExitCode = await Task.Run(() => RunFfmpegTrim(downloadedFile, trimmedPath, timeFrom, timeTo, cancellationToken), cancellationToken);
                if (trimExitCode != 0) throw new Exception($"ffmpeg trim error code: {trimExitCode}");
                File.Delete(downloadedFile);
                File.Move(trimmedPath, finalPath, overwrite: true);
            }
            else
            {
                File.Move(downloadedFile, finalPath, overwrite: true);
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
        var result = new List<string>();
        try
        {
            using var process = new Process { StartInfo = new ProcessStartInfo { FileName = _ytDlpPath, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
            process.StartInfo.ArgumentList.Add("--js-runtimes"); process.StartInfo.ArgumentList.Add("node");
            process.StartInfo.ArgumentList.Add("--encoding"); process.StartInfo.ArgumentList.Add("utf-8");
            process.StartInfo.ArgumentList.Add("--no-playlist"); process.StartInfo.ArgumentList.Add("--dump-json");
            process.StartInfo.ArgumentList.Add("--no-download"); process.StartInfo.ArgumentList.Add(url);

            using var reg = ct.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } });
            process.Start();

            using var reader = new StreamReader(process.StandardOutput.BaseStream, System.Text.Encoding.UTF8);
            var json = await reader.ReadToEndAsync(ct);
            if (!process.WaitForExit(TimeSpan.FromSeconds(5))) return result;

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("formats", out var formats)) return result;

            var heights = new HashSet<int>();
            foreach (var fmt in formats.EnumerateArray())
            {
                if (fmt.TryGetProperty("vcodec", out var vc) && vc.GetString() != "none" && vc.GetString() != "")
                {
                    if (fmt.TryGetProperty("height", out var h) && h.GetInt32() > 0)
                        heights.Add(h.GetInt32());
                }
            }

            foreach (var h in heights.OrderByDescending(x => x))
                result.Add($"{h}p");
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to get available video formats"); }
        return result;
    }

    private static string GetMp3QualityArg(string quality) => quality switch
    {
        "mp3_128" => "128K",
        "mp3_96" => "96K",
        _ => "0"
    };

    private int RunYtDlp(DownloadMode mode, string quality, string qualityArg, string tempFilePath, string url, IProgress<double> progress, CancellationToken ct)
    {
        var args = mode == DownloadMode.Audio
            ? NeedsMp3Conversion(quality)
                ? new[] { "--js-runtimes", "node", "--encoding", "utf-8", "--ffmpeg-location", _ffmpegPath, "-x", "--audio-format", "mp3", "--audio-quality", GetMp3QualityArg(quality), "--postprocessor-args", "-threads 0", "--no-playlist", "-f", qualityArg, "-o", $"{tempFilePath}.%(ext)s", url }
                : new[] { "--js-runtimes", "node", "--encoding", "utf-8", "--ffmpeg-location", _ffmpegPath, "--no-playlist", "-f", qualityArg, "-o", $"{tempFilePath}.%(ext)s", url }
            : new[] { "--js-runtimes", "node", "--encoding", "utf-8", "--ffmpeg-location", _ffmpegPath, "-f", qualityArg, "--merge-output-format", "mp4", "--no-playlist", "-o", $"{tempFilePath}.%(ext)s", url };

        using var process = new Process { StartInfo = new ProcessStartInfo { FileName = _ytDlpPath, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);

        process.OutputDataReceived += (_, e) => { if (e.Data != null) ParseProgress(e.Data, progress); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) ParseProgress(e.Data, progress); };

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
        process.StartInfo.ArgumentList.Add("--js-runtimes"); process.StartInfo.ArgumentList.Add("node");
        process.StartInfo.ArgumentList.Add("--encoding"); process.StartInfo.ArgumentList.Add("utf-8");
        process.StartInfo.ArgumentList.Add("--no-playlist"); process.StartInfo.ArgumentList.Add("--get-title"); process.StartInfo.ArgumentList.Add(url);

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

    private static void ParseProgress(string line, IProgress<double> progress)
    {
        if (line.Contains("[ExtractAudio]")) progress.Report(-1);
        else if (line.Contains("[Merger]") || line.Contains("[FixupM3u8]")) progress.Report(-2);
        else if (line.Contains("Deleting original")) progress.Report(-3);

        var match = Regex.Match(line, @"(\d+(?:\.\d+)?)%");
        if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
            progress.Report(Math.Min(p / 100.0, 1.0));
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

    private void CleanupTempFiles(string jobId)
    {
        try { foreach (var f in Directory.GetFiles(_tempFolder, $"temp_{jobId}*")) File.Delete(f); }
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
        try
        {
            using var process = new Process { StartInfo = new ProcessStartInfo { FileName = _ytDlpPath, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true } };
            process.StartInfo.ArgumentList.Add("--js-runtimes"); process.StartInfo.ArgumentList.Add("node");
            process.StartInfo.ArgumentList.Add("--encoding"); process.StartInfo.ArgumentList.Add("utf-8");
            process.StartInfo.ArgumentList.Add("--no-playlist"); process.StartInfo.ArgumentList.Add("--dump-json");
            process.StartInfo.ArgumentList.Add("--no-download"); process.StartInfo.ArgumentList.Add(url);

            using var reg = ct.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } });
            process.Start();

            using var reader = new StreamReader(process.StandardOutput.BaseStream, System.Text.Encoding.UTF8);
            var json = await reader.ReadToEndAsync(ct);
            if (!process.WaitForExit(TimeSpan.FromSeconds(5))) return result;

            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("formats", out var formats)) return result;

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
                    foreach (var h in heights.OrderByDescending(x => x))
                        result.Add($"{h}p");
                }
            }
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to get available formats"); }
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
}
