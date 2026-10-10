# Smart Tracking recording continuity — root-cause report

Scope: the saved video (not just the preview) when Smart Tracking zoom is active. Everything below was
measured on one machine: AMD Radeon integrated GPU, 1920x1080 @ 60 Hz, Windows 11, GPU frame pipeline
active, libx264 unless stated. See [Not verified](#not-verified) for what that leaves open.

## How it was measured

`Tests/RecordingChecks` drives the real `RecordingManager`. A separate process covers the primary display
with a window that paints a millisecond clock as a scale-invariant bit pattern, so it can be decoded back out
of a zoomed, compressed frame. The harness moves the real mouse and clicks, records, then decodes every
output frame and compares **source time** against **output time**:

- *end drift* — source time covered minus output timeline length. Positive/negative growth over a run is
  lost/stretched timeline; a bounded value is not.
- *jump* — one output frame spanning ≥ 2 frame periods of source time; *duplicate* — the same source instant
  in consecutive output frames; *torn* — one frame whose rows show different instants.
- Internal counters (`RecordingDiagnostics`): compose time, publish interval, GPU wait/readback, pacer wake
  gaps, owed/repeated frames, pipe write time, GC. They hold counts and durations only.

Run it (takes over the display and mouse; **Esc aborts**):

```powershell
dotnet build Tests\RecordingChecks -c Release
dotnet Tests\RecordingChecks\bin\Release\net8.0-windows10.0.19041.0\RecordingChecks.dll --scenarios zoom60,baseline60 --seconds 20
dotnet Tests\RecordingChecks\bin\Release\net8.0-windows10.0.19041.0\RecordingChecks.dll --timer-demo
```

Scenarios: `baseline30/60`, `zoom30/60`, `still60`, `rapid60`, `clicks60`, `typing60`, `zoom60-sw`, `zoom60-amf`,
`zoom30-audio`, `zoom60-audio`, `pause30-audio`, `window30-audio`, `mic30`, `amf60-audio`, `long60-audio` (150 s), `long30-off-sw-audio`, `long30-window-audio`, `long60-amf-audio` (130 s), `sharp-zoom`, `sharp-zoom-cpu`, `sharp-1x` (sharpness target), `caret30` (real caret), `pause30/60`, `cpu30/60` (forces the CPU kernels via `CAPIT_FORCE_CPU_PIPELINE=1`),
`window30` (Windows Graphics Capture), `preview`, `long60` (120 s), `ref30/60` (ffmpeg's own capture of the
same source, as a floor). `CompositionChecks` gained deterministic `FrameClock` tests. Offline analysis:
`Tests/RecordingChecks/sharpness.py` (zoom sharpness against a native render) and `caretcheck.py` (caret
following); `RecordingChecks --audio-state` prints the default playback device.

## Root causes (original code → what the evidence showed)

| # | Cause | Evidence (original code) | Status |
|---|---|---|---|
| 1 | **Pacer period truncated to whole ms.** `PeriodicTimer(16.67 ms)` ticks at 16 ms: a "60 fps" recording is fed at 62.5 fps (30 fps → 30.3). The file runs long against real time and audio. | 60 fps: output 18.80 s for 18.01 s of source (−791 ms, ≈ 4.4 %); `--timer-demo`: 625 frames in 10 s (+3.4 %) | Fixed |
| 2 | **A late pacer wakeup loses frames.** `PeriodicTimer` never makes up a missed tick, and ffmpeg is fed untimestamped rawvideo, so a missed tick is a frame that never exists (timeline shortens, audio does not). | `--timer-demo` with all cores busy: 576 frames in 10 s (−4.1 %). **Not reproduced in a real recording** — pacer missed 0 ticks in every run on this machine | Fixed (mechanism proven, symptom not seen) |
| 3 | **Synchronous GPU→CPU readback on the capture thread.** Mapping the staging texture made the capture thread wait out the GPU's whole frame (zoom + sharpen passes) on top of the copy. | Zoom 60 fps: compose 15.7 ms mean vs 16.7 ms budget; 268/857 composes over budget; 92 publish gaps > 2 budgets (up to 82 ms) → stutter | Fixed |
| 4 | **Torn frames.** The GPU readback wrote into the published NV12/preview arrays with no lock while the pacer copied them (`_gpuFrameLock` guarded only the flags). | 9 torn frames (no zoom) / 18 (zoom) per 18 s | Fixed |
| 5 | **Compose-on-arrival.** Which frame the pacer found waiting depended on DXGI wake jitter + compose time; camera positions were sampled unevenly. | Mid-value `publish interval` p99 50–65 ms | Fixed (see caveat on 60 fps below) |
| 6 | **Encoder stalls overran a 3-frame pool** (~50 ms at 60 fps). | AMD AMF: 210 and 819 of ~900 frames emitted as repeats; one 0.9 s freeze | Fixed (pool ≈ 0.2 s of frames, memory-capped) |
| 7 | **Audio starts after video** (ffmpeg can only open the audio pipe after video is flowing) but both streams call their first sample t=0. | First audio write 52–93 ms after video start in three runs | Fixed (leading silence) |

## What changed

- `FrameClock` (new, `CapIT.Core`): slot *k* opens at exactly `start + k/fps`; the pacer fills every owed
  slot, repeating the previous frame for slots it was late for and giving only the newest slot the fresh capture.
  Pause cuts its interval out. Dedicated thread, not the thread pool.
- GPU path (`GpuFrameProcessor`, `VideoCaptureService`): two-slot readback ring (frame *n−1* is read while the
  GPU works on *n*); published frames are swapped from back buffers under the lock; while recording the
  capture thread composes once per output slot on the shared clock (arrivals are only cached), and the pacer
  samples 1.5 slots after a slot opens — half a slot of margin either side of when the frame is published.
- `RecordingManager`: pool sized to ≈ 0.2 s of frames; clock-driven pacer; `RecordingDiagnostics`.
- `AudioCaptureService`: leading silence equal to the audio start offset.
- `CAPIT_FORCE_CPU_PIPELINE=1`: diagnostic switch (reproduce CPU-path behaviour on a GPU-capable machine).
- No UI, settings, encoder arguments or saved-file format changed. Nothing committed.

## Results (same machine, 1080p, libx264, Smart Tracking on)

| | 60 fps before | 60 fps after | 30 fps before | 30 fps after |
|---|---|---|---|---|
| End drift vs source | −791 ms | **+6 ms** (3 runs: +3…+19) | −165 ms | −29…0 ms |
| 120 s run: end / max drift | (≈ −5.3 s extrapolated) | **+24 ms / 72 ms** | – | – |
| Torn frames | 18 | **0** (all runs) | 0–6 | 0 |
| Compose mean, ms | 15.7 | **3.0–5.1** | – | 1.7 |
| Composes over budget | 268 / 857 | **6–66 / ~1200** | – | 0 |
| Publish gaps > 2 budgets | 92 | **4–15** | – | 0 |
| AMF encoder repeats | 210–819 / ~900 | **0 / 1203** | – | – |
| 120 s GC pause total | – | 30.7 ms | – | – |
| Duplicates / jumps (60 fps) | 517 / 208 (zoom) | 375–464 / 184–211 (zoom) | 33 / 27 | 17–33 / 12–30 |

Caveat on the last row: at 60 fps the **test source itself** only produces ~40 distinct display updates a
second (ffmpeg's own Desktop-Duplication capture of it shows 266–306 duplicates and 88–132 jumps with no zoom
at all, Cap-IT without zoom 284–373), so duplicate/jump counts at 60 fps are dominated by the display, not the
recorder. They improved ~20 % with zoom on; do not read them as "60 fps judder is gone". At 30 fps they are
at the no-zoom floor.

Other checks that passed: pause/resume (exactly one jump the size of the pause, timeline = un-paused time),
window capture (drift +34 ms), live preview (181 frames, all decoded, 0 torn, monotonic), software x264, AMF,
click-only, rapid movement, forced CPU kernels, 120 s.

## Second pass: audio tail, sharpness, caret, window capture, long runs

### Audio tail — root cause and fix

The audio stream ended 0 to 0.9 s short of the video (and its *content* sat 0.2–0.4 s off from run to run).
Measured with a pump trace, three causes combined:

1. ffmpeg opens its audio input only after the first video frames are probed, so the first pipe `Write` blocked
   ~1 s. The pump read from the capture buffer and wrote to the pipe in one loop, so the 2 s capture buffer sat
   at 1.1–1.8 s full for the whole recording and anything arriving while it was full was dropped.
2. On stop, whatever was still in that backlog was lost; how much had been written at the instant the loop was
   told to stop was a race. That is the 0–0.9 s.
3. WASAPI loopback hands over a burst of already-queued audio when capture starts; because order was preserved
   and nothing trimmed, that stale burst shifted the whole stream's content.

Fix (`PcmLegPump`, new; `AudioCaptureService`, `FFmpegEncoderService`, `RecordingManager`): a clock-driven
reader produces exactly the samples the clock says are owed (a silent loopback device yields real-time silence),
trims stale backlog, and feeds an unbounded queue drained by a separate writer, so pipe stalls cannot back up
into the capture buffer. On stop the reader runs to the stop instant, the video pipe is closed first, the audio
queue is drained, and ffmpeg ends by pipe EOF (no immediate `q`) so it consumes the tail. The pacer also emits
the slots owed up to the stop instant. No silence is added to hide truncation: `UnderrunMs`/`StaleDroppedMs`
are reported separately.

| Audio length minus video length (positive = audio short) | before | after |
|---|---|---|
| 20 s runs, 3 runs | 0 / +909 / +870 ms | |
| zoom 30, pause, window, mic, zoom 60, AMF 60 (20 s) | | −26 … +9 ms |
| 130–150 s: ST on 60 fps, ST off 30 fps (x264), window 30 fps, AMF 60 fps | | −22, −6, −11, ±0 ms |

No drain timeouts in any run. Run-to-run A/V content offset fell from ~410 ms to ~25 ms.

**A/V sync.** The test source plays a 1 kHz, 60 ms beep each second through the real output device while painting
its decoded millisecond clock; beep onsets are mapped to video time. Drift (last minus first offset): +6, +7,
−13, −23, +3, −6 ms across 20–150 s runs (zoom, long, AMF, x264 ST-off, window). Per-beep jitter ±7–19 ms. The
**absolute** offset is not established: a loopback calibration of the output+capture chain read +46 … +151 ms in
different runs, so only drift and run-to-run change are claimed.

### Zoom sharpness

Method: the test window shows a text/edge target (Segoe UI 12–20 px in black, blue and light-on-dark, rotated
squares, hairline grids, circles). The same layout rendered natively at the zoom factor is the ground truth. A
2x Smart Tracking recording is registered to the 1x layout (zoom 1.9995, origin error < 0.15 px, residual rms
0.02) and scored against it: high-frequency energy ratio (1.0 = as much fine detail as native), 10–90 % edge
width through the rotated squares, and text overshoot.

| 2x zoom, steady camera, encoded x264 | HF ratio | edge 10–90 % | text overshoot |
|---|---|---|---|
| native render (ground truth) | 1.000 | 1.93 px | 0 |
| before (sharpen 1.0) | 0.907 | 2.42 px | 1.20 % |
| after (sharpen 1.3) — GPU | **0.992** | **2.22 px** | 1.40 % |
| after — forced CPU kernels | 0.987 | 2.20 px | 1.35 % |

Catmull-Rom was kept: in an offline model of the candidates (bilinear, Mitchell, Catmull-Rom ± unsharp, Lanczos3 ±
unsharp) Lanczos3 had a similar edge width but less detail energy at comparable overshoot, and it costs 2.25× the
texture fetches. Only the two constants (`ZoomSharpenRamp`/`ZoomSharpenMaxAmount`,
1.1/1.0 → 1.3/1.3) changed, so GPU and CPU cost are unchanged; no extra pass, no extra resample. Visual check of
before/after crops at 3× magnification: slightly crisper strokes, no rims.

Limits: a 3× zoom recovers only ~0.1 of native detail energy regardless of filter (the information is not in the
captured pixels), so no sharpening is claimed to fix it. 1.25× and 1.75× were not measured separately; the
sharpen amount at those levels is 0.33 and 0.98.

### Caret following (real typing)

`caret30`: a native multi-line edit control in the lower right of the target, mouse clicked into it and then
parked in the opposite corner (idle), real keystrokes typed for ~11 s, the control's `GetGUIThreadInfo` caret
position logged as reference. In every one of the 38 steady zoomed frames from the first keystroke on, the caret
was inside the 2× crop (median 126 px from its centre, crop 960×540) and the parked mouse position was inside
the crop in 0 of 38. The camera panned to the caret and followed it along the line and across line breaks.
Behaviour not changed.

### Window capture

Window capture is paced by Windows Graphics Capture, and on the callback thread every frame went through the
16-tap CPU Catmull-Rom resample even when the window was exactly canvas-sized (1:1, all weights (0,1,0,0)).
`ResampleCatmullRomInto` now row-copies in that case (bit-identical result).

| 130 s, window, audio | before | after |
|---|---|---|
| jumps ≥ 2 periods | 143 | **46** |
| end drift / max drift | +53 / 218 ms | **+18 / 53 ms** |
| pacer missed ticks / catch-up repeats | 7 / 7 | **0 / 0** |
| pacer copy max | 109 ms | 20 ms |

Window capture is still less even than display capture (46 jumps in 3905 frames against 1 in 3906 for display
capture at 30 fps), because WGC delivers frames only when the window changes. No content is lost.

### Long-session stability (this machine)

| scenario | length | end / max drift | audio tail | A/V drift | GC pause total (gen2) |
|---|---|---|---|---|---|
| ST on, 60 fps, GPU, x264 + system audio | 150 s | −13 / 92 ms | −22 ms | +7 ms | 44 ms (2) |
| ST off, 30 fps, GPU, x264 | 130 s | −43 / 43 ms | −6 ms | −23 ms | 29 ms (1) |
| ST on, 30 fps, window (CPU path), audio | 130 s | +18 / 53 ms | −11 ms | n/a¹ | 115 ms (2) |
| ST on, 60 fps, AMF, audio | 130 s | −29 / 65 ms | ±0 ms | n/a¹ | 32 ms (1) |

¹ The default playback device changed during the session (to a monitor's HDMI audio, 8 % volume), so the beeps
no longer reached the loopback in the last two runs; stream lengths are valid, A/V drift was not measured.
Audio queue maxima 107–946 ms (the first-write stall, absorbed by the queue).

### Regression re-check after the changes

CompositionChecks: **651 assertions pass** (including real MP4/GIF exports and new `FrameClock` pause tests).
Release build of the whole solution: 0 errors. RecordingChecks, 15–20 s each, drift end / max, no torn frames:

| baseline30 | zoom30 | baseline60 | zoom60 | still60 | rapid60 | clicks60 | typing60 | zoom60-sw | zoom60-amf |
|---|---|---|---|---|---|---|---|---|---|
| −75 / 76 | −32 / 74 | −6 / 27 | −33 / 101 | −2 / 22 | −10 / 104 | −18 / 149 | −16 / 84 | −43 / 63 | −12 / 64 ms |

`pause30/60`: exactly one jump the size of the 3 s pause (timeline = un-paused time). `cpu30`: +46 / 53 ms.
Pacer: 0 missed ticks, 0 encoder-behind repeats in every non-window run. The 60 fps duplicate/jump counts remain
display-limited (see above). zoom60-amf had 70 frames whose test code could not be decoded (heavily blurred
mid-zoom frames); timeline and drift are intact.
**Live preview**: the preview scenario was cut short by Esc keypresses reaching the test window in all three
re-runs in this pass (62 frames pulled, 62 decoded, 0 torn, 0 backwards in the longest). The earlier
uninterrupted run (181/181) predates the audio and window changes, which do not touch the preview path.

## Not fixed / not found / not verified

- **I did not reproduce a literal "section of the video cut"** on this machine. Stutter, A/V drift, torn
  frames, encoder-stall freezes and the audio tail were reproduced and fixed; frame-loss mechanism shown in
  `--timer-demo`. If cuts appear elsewhere, run `RecordingChecks` there.
- **CPU kernels cannot hold 60 fps at 1080p here** (compose 36 ms mean). Timeline stays correct, motion sparse.
- **Absolute A/V offset** is not calibrated (see above); only drift is claimed. Window-capture A/V jitter ±14–27 ms in the earlier runs.
- **AMF**: the fragmented-MP4 start has one 46 ms first-packet offset (single pts gap at the very start); the
  timeline thereafter is regular.
- **Not tested**: 1440p, 4K (display is 1080p), NVENC, QSV, any GPU other than this AMD iGPU, 3× / 1.25× / 1.75×
  sharpness, recordings > 150 s, browser/IDE content, mid-recording display or audio-device changes, the
  application's user interface (not changed).
