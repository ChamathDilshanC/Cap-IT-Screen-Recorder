using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using ScreenRecorderApp.Models;

namespace ScreenRecorderApp.Services.Encoding;

/// <summary>
/// Spawns ffmpeg.exe and feeds it raw BGRA video (and optionally s16le PCM audio) over named pipes,
/// letting ffmpeg do the real-time H.264/AAC encode and MP4/MKV muxing.
/// </summary>
/// <remarks>
/// ffmpeg's rawvideo demuxer still blocks in avformat_find_stream_info() waiting for actual bytes
/// before it will move on to open the *next* input, even with -probesize/-analyzeduration forced to
/// the minimum. So StartAsync only waits for the video pipe to connect; the caller must start writing
/// real frames immediately afterwards (before the audio pipe can ever be expected to connect) and only
/// then await <see cref="WaitForAudioConnectionAsync"/>. Connecting both pipes up front before any
/// writer exists is a deadlock: ffmpeg won't open input #2 until input #1's probe is satisfied, which
/// never happens without a writer.
/// </remarks>
public sealed class FFmpegEncoderService : IDisposable
{
    private Process? _process;
    private NamedPipeServerStream? _videoPipeServer;
    private NamedPipeServerStream? _audioPipeServer;
    private readonly StringBuilder _log = new();

    public Stream? VideoPipe => _videoPipeServer;
    public Stream? AudioPipe => _audioPipeServer;
    public string LastLog => _log.ToString();

    public async Task StartAsync(RecordingSettings settings, int videoWidth, int videoHeight, bool audioEnabled, string outputPath, CancellationToken ct = default)
    {
        var ffmpegPath = FFmpegLocator.FindFFmpeg()
            ?? throw new FileNotFoundException(
                "ffmpeg.exe was not found. Place it in an 'ffmpeg' subfolder next to the app, or install it and add it to PATH.");

        var encoder = ResolveEncoderName(settings.Encoder);
        var args = BuildArguments(settings, videoWidth, videoHeight, encoder, outputPath, out var videoPipeName, out var audioPipeName, audioEnabled);

        _videoPipeServer = new NamedPipeServerStream(videoPipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        _audioPipeServer = audioEnabled
            ? new NamedPipeServerStream(audioPipeName!, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous)
            : null;

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = args,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (_log) _log.AppendLine(e.Data);
        };

        _process.Start();
        _process.BeginErrorReadLine();

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectCts.CancelAfter(TimeSpan.FromSeconds(10));

        var connectTask = _videoPipeServer.WaitForConnectionAsync(connectCts.Token);
        var exitTask = _process.WaitForExitAsync(connectCts.Token);
        var completed = await Task.WhenAny(connectTask, exitTask).ConfigureAwait(false);

        if (completed == exitTask || _process.HasExited)
        {
            var log = LastLog;
            Cleanup();
            throw new InvalidOperationException("ffmpeg exited before the video pipe connected. ffmpeg output:\n" + log);
        }

        await connectTask.ConfigureAwait(false); // observe any exception (e.g. timeout)
    }

    /// <summary>
    /// Waits for ffmpeg to connect to the audio pipe. Must be called only after the caller has begun
    /// writing frames to <see cref="VideoPipe"/>, otherwise ffmpeg will never get past probing input #1.
    /// </summary>
    public async Task WaitForAudioConnectionAsync(CancellationToken ct = default)
    {
        if (_audioPipeServer is null) return;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        var connectTask = _audioPipeServer.WaitForConnectionAsync(cts.Token);
        var exitTask = _process!.WaitForExitAsync(cts.Token);
        var completed = await Task.WhenAny(connectTask, exitTask).ConfigureAwait(false);

        if (completed == exitTask || _process.HasExited)
        {
            var log = LastLog;
            Cleanup();
            throw new InvalidOperationException("ffmpeg exited before the audio pipe connected. ffmpeg output:\n" + log);
        }

        await connectTask.ConfigureAwait(false);
    }

    private static int? TargetHeight(OutputResolution resolution) => resolution switch
    {
        OutputResolution.P360 => 360,
        OutputResolution.P480 => 480,
        OutputResolution.P720 => 720,
        OutputResolution.P900 => 900,
        OutputResolution.P1080 => 1080,
        OutputResolution.P1440 => 1440,
        OutputResolution.P2160 => 2160,
        _ => null, // Native: no scaling filter.
    };

    private static string ResolveEncoderName(HardwareEncoder requested) => requested switch
    {
        HardwareEncoder.Nvenc => "h264_nvenc",
        HardwareEncoder.Amf => "h264_amf",
        HardwareEncoder.Qsv => "h264_qsv",
        _ => "libx264", // Auto and SoftwareX264 both default to the universally-available software encoder.
    };

    private void Cleanup()
    {
        try { _videoPipeServer?.Dispose(); } catch { /* best effort */ }
        try { _audioPipeServer?.Dispose(); } catch { /* best effort */ }
        try
        {
            if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true);
        }
        catch { /* best effort */ }
        _process?.Dispose();
        _videoPipeServer = null;
        _audioPipeServer = null;
        _process = null;
    }

    private static string BuildArguments(RecordingSettings settings, int videoWidth, int videoHeight, string encoder, string outputPath, out string videoPipeName, out string? audioPipeName, bool audioEnabled)
    {
        videoPipeName = $"capit_video_{Guid.NewGuid():N}";
        audioPipeName = audioEnabled ? $"capit_audio_{Guid.NewGuid():N}" : null;

        var sb = new StringBuilder();
        sb.Append("-y -hide_banner -loglevel warning ");

        // Video input: raw BGRA frames pushed by VideoCaptureService at a fixed pace. The format is
        // fully specified via -pix_fmt/-s/-r, so probesize/analyzeduration are forced to the minimum
        // to avoid ffmpeg buffering more than it needs to before it considers the stream "found".
        sb.Append($"-probesize 32 -analyzeduration 0 -thread_queue_size 1024 -f rawvideo -pix_fmt bgra -s {videoWidth}x{videoHeight} -r {settings.Fps} -i \\\\.\\pipe\\{videoPipeName} ");

        if (audioPipeName is not null)
        {
            sb.Append($"-probesize 32 -analyzeduration 0 -thread_queue_size 1024 -f s16le -ar {Capture.AudioCaptureService.SampleRate} -ac {Capture.AudioCaptureService.Channels} -i \\\\.\\pipe\\{audioPipeName} ");
        }

        sb.Append("-map 0:v ");
        if (audioPipeName is not null) sb.Append("-map 1:a ");

        // Capture always happens at the monitor's native resolution (VideoCaptureService/live preview are
        // unaffected); scaling to the user-selected output quality is left entirely to ffmpeg's own
        // high-quality scaler here, on the encode side only. "-2" keeps the width proportional to the
        // requested height and rounds to the nearest even number (required for yuv420p). flags=lanczos
        // asks swscale for its sharpest resampling kernel — ffmpeg's own SIMD-optimized scaler, so unlike
        // a hand-rolled per-frame filter this costs nothing worth worrying about.
        var targetHeight = TargetHeight(settings.Resolution);
        if (targetHeight is int th && th != videoHeight)
        {
            sb.Append($"-vf \"scale=-2:{th}:flags=lanczos\" ");
        }

        // The yuv444p "maximize text clarity" path only exists for libx264 — hardware encoders don't
        // reliably support 4:4:4 in consumer ffmpeg builds, so it silently has no effect there.
        var useTextClarity = settings.MaximizeTextClarity && encoder == "libx264";
        var pixFmt = useTextClarity ? "yuv444p" : "yuv420p";
        sb.Append($"-c:v {encoder} -pix_fmt {pixFmt} ");
        sb.Append(BuildEncoderTuning(encoder, settings.VideoBitrateKbps, useTextClarity));

        if (audioPipeName is not null)
        {
            sb.Append("-c:a aac -b:a 192k -ar 48000 ");
        }

        if (settings.Container == OutputContainer.Mp4)
        {
            // Fragmented MP4 instead of +faststart: with faststart, ffmpeg has to rewrite the whole file
            // to move the moov atom to the front once recording stops — for a large, high-bitrate (e.g.
            // 4K/40Mbps) file that rewrite can take far longer than the shutdown grace period, and a
            // forced kill mid-rewrite leaves a truncated file with no moov atom at all ("Item is
            // unplayable" in VLC). Fragmented MP4 writes an empty moov up front and flushes a moof/mdat
            // pair per GOP as encoding progresses, so there's no expensive rewrite on stop, and the file
            // stays valid up to the last flushed fragment even if the process were killed mid-recording.
            sb.Append("-movflags +frag_keyframe+empty_moov+default_base_moof ");
        }

        sb.Append('"').Append(outputPath).Append('"');

        return sb.ToString();
    }

    /// <summary>
    /// Quality-first rate control per encoder instead of a flat average bitrate: content-adaptive bit
    /// allocation (CRF for libx264, quality-target VBR for nvenc) spends bits where the frame actually
    /// needs them — e.g. on text — instead of wasting them on static regions, while <c>-maxrate</c>/
    /// <c>-bufsize</c> still keep the user's bitrate setting as a hard ceiling so file size stays bounded.
    /// amf/qsv keep their existing bitrate-VBR rate control (their CRF-equivalent modes are less
    /// consistently supported across ffmpeg builds) and only get a safe quality-preset bump, since both
    /// are hardware-accelerated with no real-time encoding risk from that.
    /// </summary>
    private static string BuildEncoderTuning(string encoder, int bitrateKbps, bool useTextClarity)
    {
        return encoder switch
        {
            // p6 (up from p4) and -cq are essentially free quality on the dedicated encode ASIC.
            "h264_nvenc" => $"-preset p6 -rc vbr -cq 19 -b:v {bitrateKbps}k -maxrate {(int)(bitrateKbps * 1.5)}k -bufsize {bitrateKbps * 2}k ",
            "h264_amf" => $"-quality quality -b:v {bitrateKbps}k -maxrate {(int)(bitrateKbps * 1.5)}k ",
            "h264_qsv" => $"-preset veryslow -b:v {bitrateKbps}k -maxrate {(int)(bitrateKbps * 1.5)}k ",
            // libx264: CRF-driven quality capped by the user's bitrate slider as -maxrate, no flat -b:v.
            // "veryfast" stays the default preset to keep a safety margin against falling behind
            // real-time at high resolutions/framerates; the text-clarity path trades that margin for
            // quality deliberately, since the user has explicitly opted into it.
            _ when useTextClarity => $"-profile:v high444 -preset medium -crf 16 -maxrate {(int)(bitrateKbps * 1.8)}k -bufsize {bitrateKbps * 2}k ",
            _ => $"-preset veryfast -crf 18 -maxrate {bitrateKbps}k -bufsize {bitrateKbps * 2}k ",
        };
    }

    /// <summary>Signals end-of-stream to ffmpeg and waits for it to finalize the output file.</summary>
    public async Task StopAsync()
    {
        try { _videoPipeServer?.Disconnect(); } catch { /* already gone */ }
        try { _audioPipeServer?.Disconnect(); } catch { /* already gone */ }

        _videoPipeServer?.Dispose();
        _audioPipeServer?.Dispose();

        if (_process is null) return;

        try
        {
            if (!_process.HasExited)
            {
                await _process.StandardInput.WriteAsync("q");
                await _process.StandardInput.FlushAsync();
            }
        }
        catch
        {
            // Process may already be exiting; the pipe EOF above is usually enough on its own.
        }

        try
        {
            // High resolutions/bitrates (e.g. a software x264 encode scaled up to 4K) can leave the
            // encoder with several seconds of backlog still to churn through once input stops; a short
            // grace period here risks a forced kill mid-encode, which — combined with fragmented MP4
            // above — no longer corrupts the whole file, but would still truncate it early. 30s gives a
            // real recording a fair chance to finish encoding its tail before we give up on it.
            await _process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token);
        }
        catch (OperationCanceledException)
        {
            try { _process.Kill(entireProcessTree: true); } catch { /* best effort */ }
        }
    }

    public void Dispose()
    {
        _videoPipeServer?.Dispose();
        _audioPipeServer?.Dispose();
        if (_process is { HasExited: false })
        {
            try { _process.Kill(entireProcessTree: true); } catch { /* best effort */ }
        }
        _process?.Dispose();
    }
}
