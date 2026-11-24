namespace ZenithAudio.Core.Audio;

public enum DsdOutputMode
{
    PcmFir = 0,
    DoPExclusive = 1,
    NativeExternalOnly = 2
}

public sealed record DsdPlaybackOptions(
    DsdOutputMode OutputMode = DsdOutputMode.PcmFir,
    int TargetPcmSampleRate = 0,
    double GainDb = 6.0);
