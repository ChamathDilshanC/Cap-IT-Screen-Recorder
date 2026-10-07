using NAudio.Wave;

namespace ScreenRecorderApp.Services;

/// <summary>Plays the bundled click effect while a capture session is active.</summary>
public sealed class ClickSoundService : IDisposable
{
    private readonly object _lock = new();
    private string _soundFileName = "mixkit-fast-double-click-on-mouse-275.wav";
    private readonly List<(AudioFileReader Reader, WaveOutEvent Output)> _active = [];

    public bool Enabled { get; set; }
    public double Volume { get; set; } = 0.75;

    public void SetSound(string fileName) => _soundFileName = fileName;

    public void Play()
    {
        var soundPath = Path.Combine(AppContext.BaseDirectory, "assets", "sounds", _soundFileName);
        if (!Enabled || !File.Exists(soundPath)) return;

        try
        {
            var reader = new AudioFileReader(soundPath);
            var output = new WaveOutEvent();
            output.Volume = (float)Math.Clamp(Volume, 0, 1);
            output.PlaybackStopped += (_, _) =>
            {
                output.Dispose();
                reader.Dispose();
                lock (_lock) _active.Remove((reader, output));
            };
            lock (_lock) _active.Add((reader, output));
            output.Init(reader);
            output.Play();
        }
        catch
        {
            // A missing or unavailable playback device must not interrupt recording.
        }
    }

    public void Dispose()
    {
        (AudioFileReader Reader, WaveOutEvent Output)[] active;
        lock (_lock) active = _active.ToArray();
        foreach (var (_, output) in active)
        {
            try { output.Stop(); } catch { }
        }
        lock (_lock) _active.Clear();
    }
}
