using Microsoft.UI.Xaml;
using NAudio.CoreAudioApi;

namespace WindowsAudioManager;

public class AudioDevice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; }
    public DataFlow DataFlow { get; set; }
    public Visibility CheckVisibility => IsDefault ? Visibility.Visible : Visibility.Collapsed;
}
