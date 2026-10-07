using System.Runtime.InteropServices;
using ScreenRecorderApp.Services.Capture.Interop;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace ScreenRecorderApp.Services.Playback;

/// <summary>
/// Plays a recording through Windows' own <see cref="MediaPlayer"/> (Media Foundation decoding and audio
/// output, exactly as the editor always has) but in frame-server mode, so the decoded picture is handed
/// back as plain BGRA pixels instead of being bound to one UI framework's media element.
/// </summary>
/// <remarks>
/// <para>Each decoded frame is copied by MediaPlayer into a D3D11 render target sized to
/// <see cref="FrameWidth"/> × <see cref="FrameHeight"/> (the source scaled down to fit the requested
/// bound, so a 4K recording does not cost a 33 MB readback per frame), then read back through a staging
/// texture into a CPU buffer. The UI pulls the newest buffer with <see cref="TryCopyLatestFrame"/>.</para>
/// <para><b>Threading:</b> MediaPlayer raises its events on Media Foundation worker threads. Every D3D
/// call happens inside <see cref="OnVideoFrameAvailable"/> under <see cref="_gate"/>, which also guards
/// the CPU buffer and disposal, so the immediate context is never used from two threads at once. Event
/// subscribers must marshal to their own UI thread.</para>
/// </remarks>
public sealed class FrameServerVideoPlayer : IDisposable
{
    private readonly object _gate = new();
    private readonly int _maxWidth;
    private readonly int _maxHeight;
    private MediaPlayer? _player;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private ID3D11Texture2D? _target;
    private ID3D11Texture2D? _staging;
    private IDirect3DSurface? _surface;
    private byte[] _frame = [];
    private long _frameSequence;
    private bool _disposed;

    /// <param name="maxWidth">Upper bound for the decoded output; the source is scaled down (never up) to fit.</param>
    /// <param name="maxHeight">Upper bound for the decoded output height.</param>
    public FrameServerVideoPlayer(int maxWidth = 1920, int maxHeight = 1440)
    {
        _maxWidth = Math.Max(16, maxWidth);
        _maxHeight = Math.Max(16, maxHeight);
    }

    /// <summary>The media opened and <see cref="NaturalDuration"/>/<see cref="FrameWidth"/> are valid. Raised on a worker thread.</summary>
    public event Action? Opened;

    /// <summary>Playback failed; the argument is the platform's error message. Raised on a worker thread.</summary>
    public event Action<string>? Failed;

    /// <summary>A new frame is available through <see cref="TryCopyLatestFrame"/>. Raised on a worker thread.</summary>
    public event Action? FrameReady;

    public int FrameWidth { get; private set; }
    public int FrameHeight { get; private set; }
    public int NaturalVideoWidth => (int)(_player?.PlaybackSession.NaturalVideoWidth ?? 0);
    public int NaturalVideoHeight => (int)(_player?.PlaybackSession.NaturalVideoHeight ?? 0);
    public TimeSpan NaturalDuration => _player?.PlaybackSession.NaturalDuration ?? TimeSpan.Zero;
    public bool IsPlaying => _player?.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;

    /// <summary>Increments for every frame copied out; lets a UI skip redundant uploads.</summary>
    public long FrameSequence => Interlocked.Read(ref _frameSequence);

    public TimeSpan Position
    {
        get => _player?.PlaybackSession.Position ?? TimeSpan.Zero;
        set { if (_player is not null) _player.PlaybackSession.Position = value; }
    }

    public double Volume
    {
        get => _player?.Volume ?? 0;
        set { if (_player is not null) _player.Volume = Math.Clamp(value, 0, 1); }
    }

    public bool IsMuted
    {
        get => _player?.IsMuted ?? false;
        set { if (_player is not null) _player.IsMuted = value; }
    }

    /// <summary>Opens <paramref name="path"/> paused. Completion is reported through <see cref="Opened"/> or <see cref="Failed"/>.</summary>
    public void Open(string path, double volume = .8)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ReleasePlayer();
        var player = new MediaPlayer
        {
            AutoPlay = false,
            IsVideoFrameServerEnabled = true,
            Volume = Math.Clamp(volume, 0, 1),
        };
        player.MediaOpened += OnMediaOpened;
        player.MediaFailed += OnMediaFailed;
        player.VideoFrameAvailable += OnVideoFrameAvailable;
        _player = player;
        player.Source = MediaSource.CreateFromUri(new Uri(Path.GetFullPath(path)));
    }

    public void Play() => _player?.Play();
    public void Pause() => _player?.Pause();
    public void StepForwardOneFrame() { _player?.Pause(); _player?.StepForwardOneFrame(); }

    /// <summary>
    /// Copies the newest decoded frame (BGRA, <see cref="FrameWidth"/> × <see cref="FrameHeight"/>) into
    /// <paramref name="destination"/>, honouring its row stride. Returns false until a frame exists.
    /// </summary>
    public unsafe bool TryCopyLatestFrame(nint destination, int destinationStride, int destinationHeight)
    {
        lock (_gate)
        {
            if (_frame.Length == 0 || FrameWidth == 0 || destinationHeight < FrameHeight) return false;
            var rowBytes = FrameWidth * 4;
            if (destinationStride < rowBytes) return false;
            fixed (byte* source = _frame)
            {
                for (var y = 0; y < FrameHeight; y++)
                    Buffer.MemoryCopy(source + (long)y * rowBytes, (byte*)destination + (long)y * destinationStride, destinationStride, rowBytes);
            }
            return true;
        }
    }

    private void OnMediaOpened(MediaPlayer sender, object args)
    {
        try
        {
            lock (_gate)
            {
                if (_disposed || sender != _player) return;
                var (width, height) = FitWithin((int)sender.PlaybackSession.NaturalVideoWidth, (int)sender.PlaybackSession.NaturalVideoHeight);
                CreateTargets(width, height);
            }
            Opened?.Invoke();
        }
        catch (Exception ex)
        {
            Failed?.Invoke(ex.Message);
        }
    }

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        if (sender != _player) return;
        Failed?.Invoke(string.IsNullOrWhiteSpace(args.ErrorMessage) ? args.Error.ToString() : args.ErrorMessage);
    }

    private unsafe void OnVideoFrameAvailable(MediaPlayer sender, object args)
    {
        lock (_gate)
        {
            if (_disposed || sender != _player || _surface is null || _context is null || _target is null || _staging is null) return;
            try
            {
                sender.CopyFrameToVideoSurface(_surface);
                _context.CopyResource(_staging, _target);
                var mapped = _context.Map(_staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                try
                {
                    var rowBytes = FrameWidth * 4;
                    fixed (byte* destination = _frame)
                    {
                        for (var y = 0; y < FrameHeight; y++)
                            Buffer.MemoryCopy((byte*)mapped.DataPointer + (long)y * mapped.RowPitch, destination + (long)y * rowBytes, rowBytes, rowBytes);
                    }
                }
                finally
                {
                    _context.Unmap(_staging, 0);
                }
                Interlocked.Increment(ref _frameSequence);
            }
            catch (COMException)
            {
                // A frame can be torn down underneath us during seek/close; the next one will arrive.
                return;
            }
        }
        FrameReady?.Invoke();
    }

    private (int Width, int Height) FitWithin(int width, int height)
    {
        width = Math.Max(2, width); height = Math.Max(2, height);
        var scale = Math.Min(1d, Math.Min((double)_maxWidth / width, (double)_maxHeight / height));
        return (Math.Max(2, (int)(width * scale) & ~1), Math.Max(2, (int)(height * scale) & ~1));
    }

    private void CreateTargets(int width, int height)
    {
        if (_device is null)
        {
            D3D11.D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
                [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0],
                out var device).CheckError();
            _device = device!;
            _context = _device.ImmediateContext;
            using var multithread = _device.QueryInterfaceOrNull<ID3D11Multithread>();
            multithread?.SetMultithreadProtected(true);
        }

        ReleaseTargets();
        FrameWidth = width; FrameHeight = height;
        _target = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default, BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
        });
        _staging = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)width, Height = (uint)height, MipLevels = 1, ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm, SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging, CPUAccessFlags = CpuAccessFlags.Read,
        });
        using var dxgiSurface = _target.QueryInterface<IDXGISurface>();
        GraphicsCaptureInterop.CreateDirect3D11SurfaceFromDXGISurface(dxgiSurface.NativePointer, out var surfacePtr);
        try { _surface = WinRT.MarshalInterface<IDirect3DSurface>.FromAbi(surfacePtr); }
        finally { Marshal.Release(surfacePtr); }
        _frame = new byte[width * height * 4];
    }

    private void ReleaseTargets()
    {
        (_surface as IDisposable)?.Dispose();
        _surface = null;
        _staging?.Dispose(); _staging = null;
        _target?.Dispose(); _target = null;
    }

    private void ReleasePlayer()
    {
        var player = _player;
        if (player is null) return;
        _player = null;
        player.MediaOpened -= OnMediaOpened;
        player.MediaFailed -= OnMediaFailed;
        player.VideoFrameAvailable -= OnVideoFrameAvailable;
        try { player.Pause(); } catch (Exception ex) when (ex is COMException or ObjectDisposedException) { }
        try { player.Dispose(); } catch (Exception ex) when (ex is COMException or ObjectDisposedException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        ReleasePlayer();
        lock (_gate)
        {
            _disposed = true;
            ReleaseTargets();
            _context?.Dispose(); _context = null;
            _device?.Dispose(); _device = null;
            _frame = [];
        }
    }
}
