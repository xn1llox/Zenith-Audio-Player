namespace ZenithAudio.Core.Audio;

public sealed class AudioEngineOptions
{
    public AudioBackend Backend { get; init; } = AudioBackend.BassWasapi;

    public bool UseWasapiExclusive { get; init; } = true;

    public int DeviceIndex { get; init; } = -1;

    public string? DeviceName { get; init; }

    public string? DeviceId { get; init; }

    public int BufferMilliseconds { get; init; } = 100;
}
