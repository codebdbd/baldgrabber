using System;
using System.Threading;
using System.Threading.Tasks;

namespace BaldGrabber.Services;

public partial class DownloadService
{
    public Task<(string path, string title, string actualQuality, bool isCollection)> DownloadVkVideoAsync(
        string url, string outputFolder, string? timeFrom, string? timeTo,
        IProgress<double> progress, CancellationToken cancellationToken,
        Action<string, string>? onSpeedEta = null) =>
        DownloadExternalVideoSourceAsync(
            url, outputFolder, "VK Video", timeFrom, timeTo,
            progress, cancellationToken, onSpeedEta);
}
