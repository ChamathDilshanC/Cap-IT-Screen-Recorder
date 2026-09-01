namespace ScreenRecorderApp.Models;

/// <summary>What a recording session captures: an entire monitor (DXGI Desktop Duplication) or a single
/// application window (Windows.Graphics.Capture) — see VideoCaptureService for the two acquisition paths.</summary>
public enum CaptureTargetKind
{
    Monitor,
    Window,
}
