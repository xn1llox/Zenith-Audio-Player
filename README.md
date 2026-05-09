# Zenith Audio Player

Zenith Audio Player is a Windows music player built with C#, WinUI 3, XAML, and Windows App SDK. The project focuses on hi-res local libraries, DSD/DSF playback workflows, SACD ISO extraction support, audio-device configuration, visual metering, lyrics, cover art, and an optional ZenithAI assistant for audio questions.

## Features

- Modern WinUI 3 interface for Windows 10 and Windows 11.
- Local library scanner for FLAC, WAV, MP3, M4A, DSF/DFF, and SACD ISO workflows.
- Audio-device selection using Windows output devices.
- DSD-to-PCM fallback for systems without native DAC/DSD support.
- Playback controls with seek bar, volume, shuffle, and queue behavior.
- Cover art and lyrics panel when metadata is available.
- Digital blue VU meter and background audio visualizer.
- ZenithAI chat assistant with configurable API settings.
- Inno Setup installer script for Windows x64 packaging.

## Requirements

- Windows 10 version 2004 / build 19041 or newer, or Windows 11.
- .NET 8 SDK for development.
- Inno Setup 6 for building the installer.
- Visual Studio 2022 is recommended for WinUI 3 development.

## Build

```powershell
dotnet restore .\ZenithAudio.sln
dotnet build .\src\ZenithAudio\ZenithAudio.csproj -c Release -r win-x64
dotnet run --project .\src\ZenithAudio\ZenithAudio.csproj -c Release
```

## Installer

The installer script is in `installer/ZenithAudio.iss`.

Before compiling the installer, download the required redistributable:

```powershell
.\scripts\Prepare-InstallerRedist.ps1
& 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' .\installer\ZenithAudio.iss
```

The generated installer is written to `artifacts/installer/`.

## Native Tools

Native binaries are not committed to the repository. Place optional native tools under:

```text
src/ZenithAudio/runtimes/win-x64/native/
```

Supported optional files:

- `sacd_extract.exe` for SACD ISO extraction to DSF.
- `mpv-2.dll` for MPV playback backend experiments.
- `bass.dll`, `basswasapi.dll`, and `bassdsd.dll` for BASS playback backend experiments.

## ZenithAI

ZenithAI stores user API settings locally and can be configured from the app. Do not commit personal API keys or generated local settings.

## License

This project is licensed under the Apache License 2.0. See [LICENSE](LICENSE).
