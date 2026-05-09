# Zenith Audio Architecture

## Application Layer

WinUI 3 owns the shell, now-playing screen, device profile controls, EQ screens, and future library browser.

## Audio Layer

`AudioEngine` is the single public facade for playback. It can use:

- BASS + BASSWASAPI for explicit WASAPI Exclusive initialization and callback-driven decoding.
- MPV/libmpv as an alternate backend for broad format support and future DSD workflows.

The UI does not call native libraries directly.

## Library Layer

The scanner will be implemented as a background pipeline with cancellation, batching, and metadata extraction workers so very large FLAC/DSD folders do not block the UI.

## EQ/Profile Layer

AutoEQ and manual TXT imports should normalize into the same `EqProfile` model before being applied to the active backend.
