namespace ZenithAudio.Core.Audio;

public sealed class ToneControlSettings
{
    public bool EqEnabled { get; set; }

    public bool DspBypassed { get; set; } = true;

    public double PreampDb { get; set; }

    public double SubBassDb { get; set; }

    public double PresenceDb { get; set; }

    public double AirDb { get; set; }

    public bool IsActive => EqEnabled && !DspBypassed;
}
