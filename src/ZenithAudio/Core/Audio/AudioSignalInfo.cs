namespace ZenithAudio.Core.Audio;

public sealed record AudioSignalInfo(
    int SampleRate,
    int BitDepth,
    int Channels,
    int BitrateKbps,
    bool IsDsd,
    string Codec);
