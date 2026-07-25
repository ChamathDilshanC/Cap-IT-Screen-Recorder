# Screen Recorder Application - System Architecture & Flow

## 1. Project Overview
This document outlines the complete architecture, data flow, and technical implementation plan for a High-Performance Screen Recorder for Windows.
This architecture document can be used as a master prompt/reference for **Claude Code** to generate the necessary files step-by-step.

### Tech Stack:
*   **Language:** C# (.NET 8 or latest)
*   **UI Framework:** WinUI 3 (Windows App SDK)
*   **Screen Capture API:** Windows.Graphics.Capture API (High-performance D3D11 capture)
*   **Audio Capture:** NAudio (WASAPI Loopback Capture for system audio & Microphone capture)
*   **Video Encoding/Muxing:** FFmpeg (via process stdin piping or FFmpeg.AutoGen wrapper)
*   **Architecture Pattern:** MVVM (Model-View-ViewModel)

---

## 2. High-Level Architecture Diagram

```mermaid
graph TD
    %% UI Layer
    subgraph "UI Layer (WinUI 3)"
        UI[Main Window / Overlay UI]
        VM[ViewModel / Commands]
    end

    %% Core Engine
    subgraph "Core Recording Engine"
        REC_MGR[Recording Manager]
        
        subgraph "Capture Modules"
            VID_CAP[Windows.Graphics.Capture<br/>(Video Frames)]
            AUD_CAP[NAudio WASAPI<br/>(System + Mic Audio)]
        end
        
        FRAME_PROC[Frame Processor<br/>& Synchronization]
    end

    %% Encoding Layer
    subgraph "Encoding & Output"
        FFMPEG[FFmpeg Encoder<br/>H.264 / AAC]
        FILE[(Output Video File<br/>.mp4 / .mkv)]
    end

    %% Flow lines
    UI <-->|Data Binding| VM
    VM -->|Start/Stop/Pause| REC_MGR
    REC_MGR -->|Initialize & Start| VID_CAP
    REC_MGR -->|Initialize & Start| AUD_CAP
    
    VID_CAP -->|Direct3D11 Textures / Bitmaps| FRAME_PROC
    AUD_CAP -->|PCM Audio Buffers| FRAME_PROC
    
    FRAME_PROC -->|Pipes (Raw Video/Audio streams)| FFMPEG
    FFMPEG -->|Muxing| FILE
```

---

## 3. Core Component Breakdown

### 3.1. UI Layer (WinUI 3)
*   **MainPage.xaml:** The main dashboard containing the Record, Pause, Stop buttons, settings (resolution, FPS, audio sources), and a recording timer.
*   **MVVM Pattern:** Binds the UI controls to the `MainViewModel`. This ensures the UI thread is never blocked by the recording engine.

### 3.2. Screen Capture Module (`Windows.Graphics.Capture`)
*   Uses `GraphicsCapturePicker` (or direct Window/Monitor handle selection) to select what to record.
*   Creates a `Direct3D11CaptureFramePool` to receive frames continuously.
*   **Frame processing:** When a `FrameArrived` event triggers, the D3D11 surface is converted into a raw byte array (NV12 or BGRA8 format) to be sent to the encoder.

### 3.3. Audio Capture Module (`NAudio`)
*   **System Audio:** Uses `WasapiLoopbackCapture` to record desktop audio.
*   **Microphone Audio:** Uses `WasapiCapture` to record the microphone.
*   **Mixing:** A `MixingSampleProvider` can be used to merge the system audio and microphone audio into a single stereo PCM stream before sending it to FFmpeg.

### 3.4. Encoding & Muxing Module (`FFmpeg`)
*   **Approach:** The easiest and highly performant way in C# is to spawn an `ffmpeg.exe` process using `System.Diagnostics.Process`.
*   **Input Streams:** Configure FFmpeg to accept raw video via standard input (stdin) or named pipes, and raw audio via a separate named pipe.
*   **Encoding:** Hardware acceleration (NVENC, AMF, or QSV) should be passed in FFmpeg arguments for maximum performance without CPU overload.

---

## 4. Execution Data Flow (Step-by-Step)

1.  **Initialization:** 
    *   User selects monitor/window, output resolution, and FPS.
    *   User clicks "Start Recording".
2.  **Setup Phase:**
    *   `RecordingManager` validates settings.
    *   Spawns the `ffmpeg.exe` process configured to listen to Named Pipes (`\.\pipeideo_pipe` and `\.\pipeudio_pipe`).
    *   Initializes NAudio WASAPI capture and connects it to the audio pipe.
    *   Initializes `Windows.Graphics.Capture` and binds the D3D11 device.
3.  **Capture Loop (Real-time):**
    *   *Video Thread:* `FrameArrived` event fires -> Read Direct3D Surface -> Copy bytes to Video Named Pipe.
    *   *Audio Thread:* NAudio `DataAvailable` event fires -> Read PCM bytes -> Copy bytes to Audio Named Pipe.
4.  **Encoding (Asynchronous):**
    *   FFmpeg reads the raw pipes, encodes frames to H.264 and audio to AAC, multiplexes them, and writes to `output.mp4`.
5.  **Termination:**
    *   User clicks "Stop".
    *   `RecordingManager` stops capture APIs.
    *   Closes the Named Pipes safely.
    *   Sends `q` command to FFmpeg standard input to gracefully finalize the MP4 file header.
    *   Notifies UI that the file is ready.

---

## 5. Recommended Directory Structure

For Claude Code, organize the project directory as follows:

```text
/ScreenRecorderApp
│
├── /Views
│   ├── MainWindow.xaml          # Application Window Shell
│   ├── MainPage.xaml            # Main Recording UI
│   └── SettingsPage.xaml        # Preferences UI
│
├── /ViewModels
│   ├── MainViewModel.cs         # UI Logic and Command Bindings
│   └── BaseViewModel.cs         # INotifyPropertyChanged implementation
│
├── /Services
│   ├── /Capture
│   │   ├── VideoCaptureService.cs  # Windows.Graphics.Capture logic
│   │   └── AudioCaptureService.cs  # NAudio WASAPI logic
│   │
│   ├── /Encoding
│   │   └── FFmpegEncoderService.cs # FFmpeg process and Pipe management
│   │
│   └── RecordingManager.cs      # Orchestrates Capture and Encoding services
│
├── /Models
│   └── RecordingSettings.cs     # Data model for FPS, Resolution, Bitrate
│
└── App.xaml                     # App Entry Point
```

---

## 6. Prompting Guide for Claude Code

To build this using Claude Code, use these step-by-step commands in your terminal:

**Step 1: Setup & Scaffolding**
> *"Claude, initialize a new blank WinUI 3 (Windows App SDK) C# project named 'ScreenRecorderApp'. Set up the MVVM folder structure (Views, ViewModels, Services, Models) as per standard best practices."*

**Step 2: Dependencies**
> *"Claude, add the required NuGet packages to the project: NAudio for audio capture, and CommunityToolkit.Mvvm for MVVM boilerplate. Note that Windows.Graphics.Capture is built into the Windows SDK."*

**Step 3: Core Capture Services**
> *"Claude, implement the 'VideoCaptureService.cs' using Windows.Graphics.Capture API to capture the primary display and expose an event that yields raw byte frames. Also, implement 'AudioCaptureService.cs' using NAudio's WasapiLoopbackCapture."*

**Step 4: FFmpeg Integration**
> *"Claude, implement the 'FFmpegEncoderService.cs'. It should start an ffmpeg.exe process, set up named pipes for raw video (e.g. rawvideo, rgb32) and raw audio (s16le), and output to an MP4 file using h264_nvenc or libx264."*

**Step 5: Orchestration & UI**
> *"Claude, create the 'RecordingManager' to synchronize video and audio frames and feed them to the FFmpeg service. Finally, build the WinUI 3 'MainPage.xaml' with Record and Stop buttons mapped to the ViewModel."*
