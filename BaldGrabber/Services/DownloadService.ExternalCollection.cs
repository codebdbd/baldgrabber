using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace BaldGrabber.Services;

public partial class DownloadService
{
    private sealed record ExternalMediaEntry(string Url, string Title);
    private sealed record ExternalMediaCollection(string Title, List<ExternalMediaEntry> Entries);

    private async Task<ExternalMediaCollection?> GetExternalMediaCollectionAsync(
        string url,
        string sourceName,
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
                Log.Warning("{Source} metadata error: {Error}", sourceName, error.Trim());
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

        var entries = new List<ExternalMediaEntry>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entriesElement.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                continue;

            var entryUrl = GetExternalJsonString(entry, "webpage_url")
                           ?? GetExternalJsonString(entry, "original_url")
                           ?? GetExternalJsonString(entry, "url");
            if (string.IsNullOrWhiteSpace(entryUrl) ||
                !Uri.TryCreate(entryUrl, UriKind.Absolute, out _) ||
                !seenUrls.Add(entryUrl))
            {
                continue;
            }

            entries.Add(new ExternalMediaEntry(
                entryUrl,
                GetExternalJsonString(entry, "title") ?? string.Empty));
        }

        if (entries.Count == 0)
            return null;

        var title = GetExternalJsonString(root, "title")
                    ?? GetExternalJsonString(root, "playlist_title")
                    ?? sourceName;
        return new ExternalMediaCollection(title, entries);
    }

    private static string? GetExternalJsonString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
