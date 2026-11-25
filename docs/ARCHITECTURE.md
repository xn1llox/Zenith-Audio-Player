# Zenith Audio Architecture (v1.0.8)

## Application Layer (`src/ZenithAudio/`)

- **WinUI 3 + Windows App SDK 1.8** unpackaged `win-x64` shell (`MainWindow.xaml`, `MainWindow.xaml.cs`, `MainWindow.Remote.cs`).
- Responsive OLED dark layout (`ApplyResponsiveLayout`) adapting across Wide Desktop, Half-Screen Split, and Compact widths, with dark Windows 11 titlebar customization (`ConfigureDarkTitleBar` + explicit `Icono.ico` and `Logo.png` resolution).
- Right-hand inspector organized into three switchable views: **Cadena de señal**, **Control de tono / EQ**, and **Ajustes del sistema**.
- Floating overlays for **ZenithAI** (`Chat` / `Acciones` / `Config API`) and **Control Remoto Web (LAN)**.

## Audio Layer (`src/ZenithAudio/Core/Audio/`)

Zenith Audio supports hot-switching between integrated and optional native backends:

- **`WasapiPlaybackEngine`**: Native WASAPI Shared and Exclusive (Event/Push) via NAudio with custom `PcmExtensibleWaveFormat` (`KSDATAFORMAT_SUBTYPE_PCM` 32-bit and 24-in-32) for USB Audio 2.0 DAC drivers (Thesycon/FiiO), endpoint driver inspection (`InspectEndpointDriver`), and non-DAC Exclusive/DoP safety guards.
- **`AsioPlaybackEngine`**: Native Steinberg ASIO 2.1 COM (`IASIO`) driver host supporting **Native DSD64 / DSD128** (`kAsioSetIoFormat = kASIODSDFormat`, click-free `0x69` DSD silence, LSB/MSB bit-order matching), ASIO PCM playback, and native ASIO Control Panel launching.
- **`DsdDecoder` & `DsdPlaybackOptions`**: Multistage linear-phase Blackman-Harris FIR DSD→PCM decimator with 8-bit LUT, configurable SACD gain (`+0/+3/+6 dB`) + soft limiter, target PCM rates (`Auto`, `44.1`–`352.8 kHz`), and `DsdDoPWaveProvider`.
- **`BufferedAudioSampleProvider`**: Foobar2000-style decoupled producer-consumer read-ahead ring buffer (~550 ms in RAM on an `AboveNormal` thread) with coalesced non-blocking seeks, keeping WASAPI hardware latency tight (45–90 ms) and isolating the real-time audio thread from disk I/O.
- **`ToneControlSampleProvider`**: Real-time 3-band biquad EQ (Sub-bass 85 Hz, Presence 2.8 kHz, Air 11 kHz), Preamp, Volume, thread-safe raised-cosine de-click seeking, live RMS VU tap, and bit-transparent pass-through when bypassed at 100% volume.
- **`AudioEngine`**: Optional external native facade for BASS + BASSWASAPI and libmpv/MPV backends.

## Library Layer

- Resilient recursive scanner using `EnumerationOptions` (`IgnoreInaccessible = true`, skipping system/reparse entries) capable of indexing 60 GB+ FLAC/DSD/CUE/ISO libraries without aborting on protected subfolders.
- Persists folder roots and audio/remote preferences per user under `%LOCALAPPDATA%\ZenithAudio\`.

## ZenithAI Layer (`src/ZenithAudio/Core/Ai/`)

- **`ZenithAiClient`**: Provider-agnostic OpenAI Chat Completions client with 11 built-in presets (NVIDIA NIM, OpenAI, OpenRouter, Groq, Gemini OpenAI-compat, DeepSeek, Mistral, Together, Ollama, LM Studio, Custom), universal model discovery (`GET /v1/models`, `/models`, `/api/tags`), and connection testing.
- **`ZenithAiActionParser`**: Extracts structured `[[ZENITH_ACTION:...]]` player control directives and strips `<think>` reasoning blocks before display.
- **`ZenithAiSettings`**: Per-user Windows DPAPI-protected credential storage with keyless support for local endpoints (`localhost` / `127.0.0.1`).

## Updates Layer (`src/ZenithAudio/Core/Updates/` & `MainWindow.Updates.cs`)

- **`GitHubUpdateService`**: Checks `releases/latest` on GitHub, compares semantic versions against the running assembly, streams the Inno Setup installer with live progress to `%TEMP%\ZenithAudioUpdates\`, and launches silent updates (`/SP- /SILENT /CLOSEAPPLICATIONS /NORESTART`) that reopen Zenith Audio automatically (`runasoriginaluser`).

## LAN Remote Control Layer (`src/ZenithAudio/Core/Remote/` & `Assets/Remote/`)

- **`ZenithRemoteServer`**: Zero-admin HTTP/1.1 server built directly on `System.Net.Sockets.TcpListener`, auto-discovering active LAN IPv4 addresses and serving `/`, `/manifest.json`, `/favicon.ico`, `/logo.png`, `/api/state`, `/api/cover`, `/api/library`, and `/api/action`.
- **`MainWindow.Remote.cs`**: Thread-safe UI-thread bridge (`IZenithRemoteBridge`) exposing live playback telemetry, album artwork caching, library search, output/engine/ASIO/Exclusive/DSD switching, and biquad EQ controls.
- **`Assets/Remote/index.html`**: Mobile-first, PWA-ready live remote interface styled with Tailwind CSS matching Zenith Audio's OLED dark + luminous cyan palette.
