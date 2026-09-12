namespace Flare;

// Only shared lighting values belong on the wire. Shadow preference stays local.
public sealed class LightingSettings
{
    public readonly float Radius, Intensity, Glow, SmokeLifetime, SmokeStrength;
    public LightingSettings(float radius = 250, float intensity = 3, float glow = 1,
        float smokeLifetime = 20, float smokeStrength = 1)
    { Radius = radius; Intensity = intensity; Glow = glow; SmokeLifetime = smokeLifetime; SmokeStrength = smokeStrength; }
    public bool Valid() => Signal.Finite(Radius) && Signal.Finite(Intensity) && Signal.Finite(Glow)
        && Signal.Finite(SmokeLifetime) && Signal.Finite(SmokeStrength)
        && Radius >= 0 && Radius <= 1000 && Intensity >= 0 && Intensity <= 20 && Glow >= 0 && Glow <= 4
        && SmokeLifetime >= 5 && SmokeLifetime <= 60 && SmokeStrength >= 0 && SmokeStrength <= 3;
    public bool Same(LightingSettings other) => Radius == other.Radius && Intensity == other.Intensity && Glow == other.Glow
        && SmokeLifetime == other.SmokeLifetime && SmokeStrength == other.SmokeStrength;
    public static float Fade(double secondsRemaining) => (float)System.Math.Max(0, System.Math.Min(1, secondsRemaining / 3));
}

public sealed class LightingState
{
    public LightingSettings Current { get; private set; } = new LightingSettings();
    public bool Received { get; private set; }
    public bool Apply(long sender, long server, LightingSettings settings)
    {
        if (server == 0 || sender != server || !settings.Valid()) return false;
        Current = settings; Received = true; return true;
    }
    public void Reset() { Current = new LightingSettings(); Received = false; }
}
