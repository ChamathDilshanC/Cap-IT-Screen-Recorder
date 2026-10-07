# ffmpeg.exe

This folder needs a Windows `ffmpeg.exe` build (~97MB) to build and run the app from source. It's
excluded from git because it's a large third-party binary, not project source. The release workflow
downloads FFmpeg 9.0 from [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds), verifies its
SHA-256 checksum, and bundles it in the installer.

1. Download a Windows x64 build from the [official FFmpeg download page](https://ffmpeg.org/download.html)
   (e.g. the gyan.dev or BtbN builds).
2. Copy `ffmpeg.exe` into this folder: `ffmpeg/ffmpeg.exe`.
3. Build as usual — see the main [README](../README.md#building).

If you just want to run the app rather than build it, download the installer from the
[Releases page](../releases) instead — it already bundles `ffmpeg.exe`.
