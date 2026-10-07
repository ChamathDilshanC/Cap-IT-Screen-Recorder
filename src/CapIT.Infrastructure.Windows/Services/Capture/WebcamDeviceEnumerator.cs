using Windows.Devices.Enumeration;

namespace ScreenRecorderApp.Services.Capture;

public sealed record WebcamDeviceOption(string Id, string Name)
{
    // ComboBox displays an item's ToString() when no DisplayMemberPath/template is set; without this
    // override it falls back to the compiler-generated record ToString(), which dumps every property
    // (including the raw device Id) instead of just the friendly name. Same reasoning as AudioDeviceOption.
    public override string ToString() => Name;
}

/// <summary>
/// Lists available webcams via Windows.Devices.Enumeration — a fully-projected WinRT API with no missing
/// public surface (unlike Windows.Graphics.Capture's window-targeting, this needs no hand-rolled interop
/// at all). Async because DeviceInformation.FindAllAsync is; unlike AudioDeviceEnumerator's synchronous
/// NAudio-backed GetMicrophones(), there's no sync wrapper worth adding here — callers already run this
/// from an async context (see MainViewModel.RefreshWebcamsAsync).
/// </summary>
public static class WebcamDeviceEnumerator
{
    public static async Task<List<WebcamDeviceOption>> GetWebcamsAsync()
    {
        var result = new List<WebcamDeviceOption>();
        try
        {
            var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            foreach (var device in devices)
            {
                result.Add(new WebcamDeviceOption(device.Id, device.Name));
            }
        }
        catch
        {
            // No camera subsystem, or the user hasn't granted camera permission — return whatever we
            // found (possibly empty) rather than surface this as a hard failure just from listing devices.
        }
        return result;
    }
}
