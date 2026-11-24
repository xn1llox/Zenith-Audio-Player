# Third Party Notices

## NuGet / Managed Libraries

Zenith Audio uses the following open-source .NET libraries restored via NuGet during build:

- **Microsoft Windows App SDK (WinUI 3)** — MIT License
- **NAudio** (`NAudio`, `NAudio.Wasapi`, `NAudio.Asio`) — MIT License
- **TagLibSharp** — LGPL-2.1 License
- **DiscUtils.Iso9660** — MIT License

## Web Remote Styling

- **Tailwind CSS** (loaded via Play CDN in `src/ZenithAudio/Assets/Remote/index.html` for the optional LAN Remote Web UI) — MIT License

## Optional Native Tools

Zenith Audio can use optional native audio tools placed under
`src/ZenithAudio/runtimes/win-x64/native/`.

### sacd_extract

`sacd_extract.exe` enables local SACD ISO extraction to DSF.

- Project: https://github.com/sacd-ripper/sacd-ripper
- Release asset: `sacd_extract-0.3.9.3-107-win64.zip`
- License: GPL-2.0
- Expected local path: `src/ZenithAudio/runtimes/win-x64/native/sacd_extract.exe`

The executable is used as an external command-line tool. Its license terms apply
to that binary when distributed.
