using NAudio.CoreAudioApi;

namespace ScreenRecorderApp.Services.Capture;

public sealed record AudioDeviceOption(string Id, string Name)
{
    // ComboBox displays an item's ToString() when no DisplayMemberPath/template is set; without this
    // override it falls back to the compiler-generated record ToString(), which dumps every property
    // (including the raw device Id) instead of just the friendly name.
    public override string ToString() => Name;
}

public static class AudioDeviceEnumerator
{
    public static List<AudioDeviceOption> GetMicrophones()
    {
        var result = new List<AudioDeviceOption>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                result.Add(new AudioDeviceOption(device.ID, device.FriendlyName));
            }
        }
        catch
        {
            // No audio subsystem available; return whatever we found (possibly empty).
        }
        return result;
    }
}
