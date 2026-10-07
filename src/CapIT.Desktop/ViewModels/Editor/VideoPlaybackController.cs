using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScreenRecorderApp.Services.Playback;

namespace ScreenRecorderApp.ViewModels;

/// <summary>
/// Editor playback: Windows' MediaPlayer (same decoding and audio as before) in frame-server mode,
/// presented as an Avalonia bitmap. Only the newest decoded frame is uploaded, on the UI thread, so a
/// slow frame never queues a backlog. Playback stops at the trim end, like the original editor.
/// </summary>
public sealed partial class VideoPlaybackController : ObservableObject, IDisposable
{
    private FrameServerVideoPlayer _player;
    private readonly DispatcherTimer _clock;
    private readonly Func<double> _trimStart;
    private readonly Func<double> _trimEnd;
    private int _uploadQueued;
    private bool _clockUpdate;
    private bool _disposed;
    private bool _released;
    private string? _path;

    public VideoPlaybackController(Func<double> trimStart, Func<double> trimEnd)
    {
        _trimStart = trimStart;
        _trimEnd = trimEnd;
        _player = CreatePlayer();
        _clock = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, (_, _) => Tick());
    }

    /// <summary>Playback position moved (clock tick or seek). Seconds in the original recording.</summary>
    public event Action<double>? PositionChanged;

    /// <summary>Media opened; the argument is its natural duration in seconds (0 if unknown).</summary>
    public event Action<double>? MediaOpened;

    [ObservableProperty] private WriteableBitmap? _frame;
    [ObservableProperty] private long _frameVersion;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _position;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private double _volume = .8;
    [ObservableProperty] private bool _isMuted;

    public string PositionText => TimeSpan.FromSeconds(Math.Max(0, Position)).ToString(@"mm\:ss\.f");
    public string? TechnicalError { get; private set; }

    private FrameServerVideoPlayer CreatePlayer()
    {
        var player = new FrameServerVideoPlayer(1920, 1440);
        player.Opened += () => Dispatcher.UIThread.Post(OnOpened);
        player.Failed += message => Dispatcher.UIThread.Post(() => OnFailed(message));
        player.FrameReady += OnFrameReady;
        return player;
    }

    public void Open(string path)
    {
        _path = path;
        IsLoading = true;
        HasError = false;
        if (_released) { _player = CreatePlayer(); _released = false; }
        try { _player.Open(path, Volume); }
        catch (Exception ex) { OnFailed(ex.Message); }
    }

    [RelayCommand]
    private void Retry() { if (_path is not null) Open(_path); }

    private void OnOpened()
    {
        if (_disposed) return;
        IsLoading = false;
        _player.IsMuted = IsMuted;
        _clock.Start();
        MediaOpened?.Invoke(_player.NaturalDuration.TotalSeconds);
        Seek(_trimStart());
    }

    private void OnFailed(string message)
    {
        if (_disposed) return;
        TechnicalError = message;
        IsLoading = false;
        HasError = true;
        ErrorMessage = message;
    }

    private void OnFrameReady()
    {
        if (Interlocked.Exchange(ref _uploadQueued, 1) == 1) return;
        Dispatcher.UIThread.Post(UploadFrame, DispatcherPriority.Render);
    }

    private void UploadFrame()
    {
        Interlocked.Exchange(ref _uploadQueued, 0);
        if (_disposed || _player.FrameWidth <= 0) return;
        var w = _player.FrameWidth; var h = _player.FrameHeight;
        if (Frame is null || Frame.PixelSize.Width != w || Frame.PixelSize.Height != h)
            Frame = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var buffer = Frame.Lock())
        {
            if (!_player.TryCopyLatestFrame(buffer.Address, buffer.RowBytes, buffer.Size.Height)) return;
        }
        FrameVersion++;
    }

    private void Tick()
    {
        if (_disposed) return;
        var playing = _player.IsPlaying;
        if (IsPlaying != playing) IsPlaying = playing;
        if (!playing) return;

        var time = _player.Position.TotalSeconds;
        if (time >= _trimEnd())
        {
            _player.Pause();
            IsPlaying = false;
            Seek(_trimStart());
            return;
        }
        SetPositionFromClock(time);
    }

    private void SetPositionFromClock(double seconds)
    {
        _clockUpdate = true;
        try { Position = seconds; }
        finally { _clockUpdate = false; }
        PositionChanged?.Invoke(seconds);
    }

    // User scrubbing (slider two-way binding) seeks; clock updates don't.
    partial void OnPositionChanged(double value)
    {
        OnPropertyChanged(nameof(PositionText));
        if (_clockUpdate) return;
        _player.Position = TimeSpan.FromSeconds(Math.Max(0, value));
        PositionChanged?.Invoke(value);
    }

    public void Seek(double seconds)
    {
        _player.Position = TimeSpan.FromSeconds(Math.Max(0, seconds));
        SetPositionFromClock(seconds);
    }

    [RelayCommand]
    public void TogglePlay()
    {
        if (IsLoading || HasError) return;
        if (_player.IsPlaying) { _player.Pause(); IsPlaying = false; return; }
        var time = _player.Position.TotalSeconds;
        if (time >= _trimEnd() - .02 || time < _trimStart()) Seek(_trimStart());
        _player.Play();
        IsPlaying = true;
    }

    public void Pause()
    {
        _player.Pause();
        IsPlaying = false;
    }

    [RelayCommand]
    private void StepFrame()
    {
        _player.StepForwardOneFrame();
        IsPlaying = false;
        DispatcherTimer.RunOnce(() => SetPositionFromClock(_player.Position.TotalSeconds), TimeSpan.FromMilliseconds(80));
    }

    [RelayCommand]
    private void ToggleMute() => IsMuted = !IsMuted;

    partial void OnVolumeChanged(double value) => _player.Volume = value;
    partial void OnIsMutedChanged(bool value) => _player.IsMuted = value;

    /// <summary>Releases the media file (needed before the original can be deleted).</summary>
    public void Release()
    {
        _clock.Stop();
        _player.Dispose();
        _released = true;
        IsPlaying = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Release();
    }
}
