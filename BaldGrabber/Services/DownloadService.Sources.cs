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
    Instagram,
    Twitter,
    Reddit,
    Vimeo,
    Twitch,
    VkVideo,
    Bandcamp,
    Mixcloud,
    BandLab,
    HearThisAt
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

    private static readonly HashSet<string> TwitterHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "x.com", "www.x.com", "mobile.x.com",
        "twitter.com", "www.twitter.com", "mobile.twitter.com", "t.co"
    };

    private static readonly HashSet<string> RedditHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "reddit.com", "www.reddit.com", "old.reddit.com", "new.reddit.com",
        "redd.it", "v.redd.it"
    };

    private static readonly HashSet<string> VimeoHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "vimeo.com", "www.vimeo.com", "player.vimeo.com"
    };

    private static readonly HashSet<string> TwitchHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "twitch.tv", "www.twitch.tv", "go.twitch.tv", "m.twitch.tv",
        "clips.twitch.tv", "player.twitch.tv"
    };

    private static readonly HashSet<string> VkHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "vk.com", "www.vk.com", "m.vk.com", "new.vk.com",
        "vk.ru", "www.vk.ru", "m.vk.ru",
        "vkvideo.ru", "www.vkvideo.ru"
    };

    private static readonly HashSet<string> MixcloudHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "mixcloud.com", "www.mixcloud.com", "beta.mixcloud.com", "m.mixcloud.com"
    };

    private static readonly HashSet<string> BandLabHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "bandlab.com", "www.bandlab.com"
    };

    private static readonly HashSet<string> HearThisAtHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "hearthis.at", "www.hearthis.at"
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

        if (TwitterHosts.Contains(uri.Host) && IsTwitterVideoPath(uri))
            return DownloadSource.Twitter;

        if (RedditHosts.Contains(uri.Host) && IsRedditVideoPath(uri))
            return DownloadSource.Reddit;

        if (VimeoHosts.Contains(uri.Host) && IsVimeoVideoPath(uri))
            return DownloadSource.Vimeo;

        if (TwitchHosts.Contains(uri.Host) && IsTwitchVideoPath(uri))
            return DownloadSource.Twitch;

        if (IsVkHost(uri.Host) && IsVkVideoPath(uri))
            return DownloadSource.VkVideo;

        if (IsBandcampHost(uri.Host) && IsBandcampMediaPath(uri))
            return DownloadSource.Bandcamp;

        if (MixcloudHosts.Contains(uri.Host) && IsMixcloudMediaPath(uri))
            return DownloadSource.Mixcloud;

        if (BandLabHosts.Contains(uri.Host) && IsBandLabMediaPath(uri))
            return DownloadSource.BandLab;

        if (HearThisAtHosts.Contains(uri.Host) && IsHearThisAtMediaPath(uri))
            return DownloadSource.HearThisAt;

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

    private static bool IsTwitterVideoPath(Uri uri)
    {
        if (uri.Host.Equals("t.co", StringComparison.OrdinalIgnoreCase))
            return uri.AbsolutePath.Length > 1;

        var segments = GetPathSegments(uri);
        var statusIndex = Array.FindIndex(segments,
            segment => segment.Equals("status", StringComparison.OrdinalIgnoreCase));
        return statusIndex >= 0 && statusIndex + 1 < segments.Length &&
               long.TryParse(segments[statusIndex + 1], out _);
    }

    private static bool IsRedditVideoPath(Uri uri)
    {
        if (uri.Host.Equals("redd.it", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("v.redd.it", StringComparison.OrdinalIgnoreCase))
        {
            return uri.AbsolutePath.Length > 1;
        }

        var segments = GetPathSegments(uri);
        var commentsIndex = Array.FindIndex(segments,
            segment => segment.Equals("comments", StringComparison.OrdinalIgnoreCase));
        var shareIndex = Array.FindIndex(segments,
            segment => segment.Equals("s", StringComparison.OrdinalIgnoreCase));
        return commentsIndex >= 0 && commentsIndex + 1 < segments.Length ||
               shareIndex >= 0 && shareIndex + 1 < segments.Length;
    }

    private static bool IsVimeoVideoPath(Uri uri)
    {
        var segments = GetPathSegments(uri);
        if (segments.Length == 0 || !long.TryParse(segments[^1], out _))
            return false;

        if (uri.Host.Equals("player.vimeo.com", StringComparison.OrdinalIgnoreCase))
            return segments.Length == 2 && segments[0].Equals("video", StringComparison.OrdinalIgnoreCase);

        return segments.Length == 1 ||
               segments.Any(segment => segment.Equals("channels", StringComparison.OrdinalIgnoreCase)) ||
               segments.Any(segment => segment.Equals("groups", StringComparison.OrdinalIgnoreCase)) ||
               segments.Any(segment => segment.Equals("video", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTwitchVideoPath(Uri uri)
    {
        if (uri.Host.Equals("clips.twitch.tv", StringComparison.OrdinalIgnoreCase))
            return uri.AbsolutePath.Length > 1;

        if (uri.Host.Equals("player.twitch.tv", StringComparison.OrdinalIgnoreCase))
            return uri.Query.Contains("video=", StringComparison.OrdinalIgnoreCase);

        var segments = GetPathSegments(uri);
        if (segments.Length == 0)
            return false;

        if (segments[0].Equals("videos", StringComparison.OrdinalIgnoreCase))
            return segments.Length > 1 && long.TryParse(segments[1], out _);

        if (segments[0].Equals("collections", StringComparison.OrdinalIgnoreCase))
            return segments.Length > 1;

        if (segments.Length < 2)
            return false;

        return segments[1].Equals("clip", StringComparison.OrdinalIgnoreCase) && segments.Length > 2 ||
               segments[1].Equals("clips", StringComparison.OrdinalIgnoreCase) ||
               segments[1].Equals("videos", StringComparison.OrdinalIgnoreCase) ||
               (segments[1].Equals("v", StringComparison.OrdinalIgnoreCase) && segments.Length > 2) ||
               (segments[1].Equals("video", StringComparison.OrdinalIgnoreCase) && segments.Length > 2);
    }

    private static bool IsVkHost(string host) =>
        VkHosts.Contains(host) || host.EndsWith(".vkvideo.ru", StringComparison.OrdinalIgnoreCase);

    private static bool IsVkVideoPath(Uri uri)
    {
        var path = uri.AbsolutePath;
        if (path.Equals("/video_ext.php", StringComparison.OrdinalIgnoreCase))
            return true;

        var segments = GetPathSegments(uri);
        if (segments.Any(segment =>
                segment.StartsWith("video", StringComparison.OrdinalIgnoreCase) ||
                segment.StartsWith("clip", StringComparison.OrdinalIgnoreCase) ||
                segment.StartsWith("wall", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("playlist", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("collections", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return uri.Host.EndsWith("vkvideo.ru", StringComparison.OrdinalIgnoreCase) &&
               segments.Length > 0 && segments[0].StartsWith("@", StringComparison.Ordinal);
    }

    private static bool IsBandcampHost(string host) =>
        host.Equals("bandcamp.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".bandcamp.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsBandcampMediaPath(Uri uri)
    {
        if (uri.Host.Equals("bandcamp.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.Equals("www.bandcamp.com", StringComparison.OrdinalIgnoreCase))
        {
            return uri.AbsolutePath.Trim('/').Equals("radio", StringComparison.OrdinalIgnoreCase) &&
                   uri.Query.Contains("show=", StringComparison.OrdinalIgnoreCase);
        }

        var segments = GetPathSegments(uri);
        return segments.Length == 0 ||
               segments.Length == 1 && segments[0].Equals("music", StringComparison.OrdinalIgnoreCase) ||
               segments.Length > 1 &&
               (segments[0].Equals("track", StringComparison.OrdinalIgnoreCase) ||
                segments[0].Equals("album", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsMixcloudMediaPath(Uri uri)
    {
        var segments = GetPathSegments(uri);
        return segments.Length > 0 &&
               !segments[0].Equals("discover", StringComparison.OrdinalIgnoreCase) &&
               !segments[0].Equals("live", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBandLabMediaPath(Uri uri)
    {
        var segments = GetPathSegments(uri);
        return segments.Any(segment =>
            segment.Equals("track", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("post", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("revision", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("albums", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("collections", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("embed", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsHearThisAtMediaPath(Uri uri) => GetPathSegments(uri).Length >= 2;

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
