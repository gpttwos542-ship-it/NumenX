# NumenX — Create. Capture. Stream.

A lightweight Windows screen recorder (WPF / .NET 8) that uses FFmpeg for GPU capture and
hardware H.264 encoding (NVENC, AMD AMF, Intel Quick Sync, CPU fallback).

> **Status: v0.1 — Stage 1 (foundation) + Stage 2 (recording core).**
> It was written without access to a Windows machine or the .NET SDK, so it has **not been
> compiled or run yet**. Expect to fix a few compile or runtime issues on first build — send the
> error text and they get fixed quickly. Nothing below is marked done unless it exists in code.

## What works (implemented in code)
| Feature | Notes |
|---|---|
| Branded dark UI, logo, app icon, accent colours | Original logo; 6 accent colours |
| Home dashboard | Big RECORD button, status, recent recordings |
| Live stats | Real CPU %, RAM, free disk, NumenX + encoder process memory |
| Hardware detection | Test-encodes NVENC / AMF / QSV; only working encoders are offered |
| Full-desktop recording | GPU capture (`ddagrab`) with GDI fallback, monitor 1 |
| Resolution / FPS / quality | 720p–4K or native, 30/60 FPS, Performance / Balanced / Maximum |
| Microphone + desktop audio | Via DirectShow devices (desktop audio needs "Stereo Mix" or VB-Cable) |
| Crash-safe saving | Records to MKV, converts to MP4 on clean stop; MKV kept if anything fails |
| Global hotkey | Ctrl + Shift + R starts / stops recording |
| Disk-space check, clear errors, log file | Log: `%AppData%\NumenX\logs` (never contains stream keys) |
| My Recordings | List, play, show in folder, delete |
| Smart Optimize (basic) | Uses detected encoder + screen height only |

## Not built yet (shown as disabled or absent — no fake buttons)
Window / region / game / webcam capture · pause/resume · live streaming (Twitch/YouTube/RTMP) ·
instant replay · scenes, sources, overlays · noise suppression / gate / compressor · audio meters ·
video thumbnails/duration/rename/search · setup wizard · screenshots · GPU-usage readout ·
multi-monitor picker · OBS benchmark · secure credential storage (needed before streaming).

## Easiest way to get the installer (no Visual Studio)
1. Create a free GitHub repository and upload this whole folder (drag & drop works).
2. Open the **Actions** tab → **Build NumenX** → **Run workflow**.
3. When it finishes (about 5 min) download the **NumenX-Installer** artifact → `NumenX-Setup.exe`.
   (**NumenX-Portable** is the same app as a plain folder.)

## Build locally instead
1. `winget install Microsoft.DotNet.SDK.8` (and optionally Inno Setup 6)
2. In PowerShell: `.\build.ps1`
3. Output: `publish\NumenX.exe` and, with Inno Setup, `installer\Output\NumenX-Setup.exe`.
   `ffmpeg.exe` must sit next to `NumenX.exe` (the script copies it if FFmpeg is on your PATH).

## Licensing note
The installer bundles a GPL build of FFmpeg (gyan.dev "essentials"). Fine for personal use; if you
distribute NumenX publicly you must comply with FFmpeg's license (provide source / notices).
"NumenX" is an original name and logo; no OBS assets are used.

## Roadmap (next stages)
2b window/region/webcam capture + pause → 3 library thumbnails → 4 streaming (+ secure key storage)
→ 5 instant replay, scenes, audio effects → 6 profiling vs OBS → 7 polish + docs.
