using System;
using System.Threading;
using System.Threading.Tasks;

namespace BaldGrabber.Services;

public partial class DownloadService
{
    public Task<(string filePath, string title, string actualQuality)> DownloadTwitterAsync(
        string url, string outputFolder, string? timeFrom, string? timeTo,
        IProgress<double> progress, CancellationToken cancellationToken,
        Action<string, string>? onSpeedEta = null) =>
        DownloadBestExternalVideoAsync(url, outputFolder, timeFrom, timeTo, progress, cancellationToken, onSpeedEta);
}
