namespace ZenithAudio.Core.Remote;

public sealed record ZenithRemoteOutputDevice(
    int Index,
    string Id,
    string Name,
    string KindLabel,
    bool IsDedicatedDac,
    bool IsSelected);

public sealed record ZenithRemoteBackendItem(
    int Index,
    string Name,
    string ShortLabel,
    bool IsSelected);

public sealed record ZenithRemoteAsioDriver(
    int Index,
    string Name,
    bool IsSelected);

public sealed record ZenithRemoteState(
    string TrackTitle,
    string TrackSubtitle,
    string TrackPath,
    bool HasTrack,
    bool IsPlaying,
    bool IsPaused,
    bool Shuffle,
    double PositionSeconds,
    double DurationSeconds,
    string CurrentTimeText,
    string DurationText,
    double Volume,
    bool VolumeLocked,
    string VolumeLabel,
    double VuLevel,
    string TransportBadge,
    string Codec,
    string SampleRate,
    string BitDepth,
    string Bitrate,
    string OutputPath,
    string StatusMessage,
    int BackendIndex,
    string BackendName,
    IReadOnlyList<ZenithRemoteBackendItem> Backends,
    int OutputDeviceIndex,
    string OutputDeviceName,
    IReadOnlyList<ZenithRemoteOutputDevice> OutputDevices,
    int AsioDriverIndex,
    IReadOnlyList<ZenithRemoteAsioDriver> AsioDrivers,
    bool ExclusiveMode,
    bool HasDedicatedDac,
    int DsdModeIndex,
    double DsdGainDb,
    int DsdPcmRate,
    bool EqEnabled,
    bool DspBypass,
    double PreampDb,
    double SubBassDb,
    double PresenceDb,
    double AirDb,
    int CoverVersion,
    bool HasCover,
    string AccentHex,
    int TotalLibraryTracks,
    int DsdTracksCount,
    int HiResTracksCount,
    string ActiveLyricLine,
    bool RequiresPin,
    string ServerHostName);

public sealed record ZenithRemoteTrackItem(
    string Path,
    string Title,
    string Folder,
    string Format,
    string Extension,
    bool IsCurrent);

public sealed record ZenithRemoteLibraryResponse(
    int TotalLibraryCount,
    int MatchedCount,
    int Offset,
    int Limit,
    bool HasMore,
    IReadOnlyList<ZenithRemoteTrackItem> Items);

public sealed record ZenithRemoteActionResult(
    bool Ok,
    string Message);

public sealed record ZenithRemoteCoverData(
    byte[] Bytes,
    string ContentType);
