using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using BaldGrabber.Models;
using Serilog;

namespace BaldGrabber.Services;

public enum DownloadSource
{
    Unsupported,
    YouTube,
    SoundCloud,
    TikTok,
    Facebook,
    Instagram
}

public partial class DownloadService
{
    private static readonly HashSet<string> SoundCloudHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "soundcloud.com", "www.soundcloud.com", "m.soundcloud.com", "on.soundcloud.com"
    };

    private static readonly HashSet<string> TikTokHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "tiktok.com", "www.tiktok.com", "m.tiktok.com", "vm.tiktok.com", "vt.tiktok.com"
    };

    private static readonly HashSet<string> FacebookHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "facebook.com", "www.facebook.com", "m.facebook.com", "web.facebook.com", "fb.watch"
    };

    private static readonly HashSet<string> InstagramHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "instagram.com", "www.instagram.com"
    };

    public static DownloadSource GetDownloadSource(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return DownloadSource.Unsupported;
        }

        if (IsValidYouTubeUrl(url))
            return DownloadSource.YouTube;

        if (SoundCloudHosts.Contains(uri.Host) && IsSoundCloudMediaPath(uri))
            return DownloadSource.SoundCloud;

        if (TikTokHosts.Contains(uri.Host) && IsTikTokVideoPath(uri))
            return DownloadSource.TikTok;

        if (FacebookHosts.Contains(uri.Host) && IsFacebookVideoPath(uri))
            return DownloadSource.Facebook;

        if (InstagramHosts.Contains(uri.Host) && IsInstagramVideoPath(uri))
            return DownloadSource.Instagram;

        return DownloadSource.Unsupported;
    }

    public static bool IsValidSupportedUrl(string? url) =>
        GetDownloadSource(url) != DownloadSource.Unsupported;

    private static bool IsSoundCloudMediaPath(Uri uri)
    {
        if (uri.Host.Equals("on.soundcloud.com", StringComparison.OrdinalIgnoreCase))
            return uri.AbsolutePath.Length > 1;

        var segments = GetPathSegments(uri);
        if (segments.Length < 2)
            return false;

        if (segments.Length == 2 &&
            (segments[1].Equals("sets", StringComparison.OrdinalIgnoreCase) ||
             segments[1].Equals("tracks", StringComparison.OrdinalIgnoreCase) ||
             segments[1].Equals("albums", StringComparison.OrdinalIgnoreCase) ||
             segments[1].Equals("popular-tracks", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return !segments.Any(segment => segment.Equals("likes", StringComparison.OrdinalIgnoreCase) ||
                                        segment.Equals("reposts", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTikTokVideoPath(Uri uri)
    {
        if (uri.Host.Equals("vm.tiktok.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("vt.tiktok.com", StringComparison.OrdinalIgnoreCase))
        {
            return uri.AbsolutePath.Length > 1;
        }

        return uri.AbsolutePath.Contains("/video/", StringComparison.OrdinalIgnoreCase) ||
               uri.AbsolutePath.Contains("/share/video/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFacebookVideoPath(Uri uri)
    {
        if (uri.Host.Equals("fb.watch", StringComparison.OrdinalIgnoreCase))
            return uri.AbsolutePath.Length > 1;

        var path = uri.AbsolutePath;
        return path.Contains("/videos/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/watch", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/reel/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/share/r/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/share/v/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInstagramVideoPath(Uri uri)
    {
        var path = uri.AbsolutePath;
        if (path.Contains("/stories/", StringComparison.OrdinalIgnoreCase))
            return false;

        return path.Contains("/reel/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/reels/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/p/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/tv/", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("/share/reel/", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] GetPathSegments(Uri uri) =>
        uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private int RunYtDlpCommand(
        IReadOnlyList<string> arguments,
        IProgress<double> progress,
        CancellationToken cancellationToken,
        Action<string, string>? onSpeedEta = null)
    {
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

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        var error = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
                ParseProgress(e.Data, progress, onSpeedEta);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            error.AppendLine(e.Data);
            ParseProgress(e.Data, progress, onSpeedEta);
        };

        cancellationToken.ThrowIfCancellationRequested();
        process.Start();
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { }
        });

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(DownloadTimeout))
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch { }
            return -1;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (process.ExitCode != 0 && error.Length > 0)
            Log.Warning("yt-dlp external source error: {Error}", error.ToString().Trim());

        return process.ExitCode;
    }

    private static string GetActualExtension(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return string.IsNullOrWhiteSpace(extension) ? ".bin" : extension;
    }
}
