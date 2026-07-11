using System.Text.Json.Serialization;

namespace BaldGrabber.Models;

public class Settings
{
    [JsonPropertyName("LastFolder")]
    public string? LastFolder { get; set; }

    [JsonPropertyName("SelectedMode")]
    public string SelectedMode { get; set; } = "Video";

    [JsonPropertyName("SelectedQuality")]
    public string SelectedQuality { get; set; } = "";

    [JsonPropertyName("SelectedAudioQuality")]
    public string SelectedAudioQuality { get; set; } = "m4a";
}
