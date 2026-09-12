using System;
using System.Collections.Generic;

namespace Flare;

// Reconstruct bounded emission history directly from the shared trajectory.
// Fixed sample times and a stable ID hash make it independent of frame rate.
public static class SmokeTrail
{
    public readonly struct Puff
    {
        public readonly float X, Y, Z, Size, Opacity, Warmth;
        public Puff(float x, float y, float z, float size, float opacity, float warmth)
        { X = x; Y = y; Z = z; Size = size; Opacity = opacity; Warmth = warmth; }
    }
    public static IEnumerable<Puff> Sample(Signal signal, double now, float lifetime, float distance)
    {
        lifetime = Math.Max(5, Math.Min(60, lifetime));
        double end = Math.Min(now, signal.EmissionEnd), start = Math.Max(signal.Start, now - lifetime);
        if (end < start) yield break;
        // Smooth spacing change; coarse distant puffs overlap instead of vanishing.
        double step = .15 + .85 * Math.Min(1, Math.Max(0, distance / 2500));
        uint seed = 2166136261;
        foreach (char c in signal.Id) seed = unchecked((seed ^ c) * 16777619);
        double phase = (seed % 6283) / 1000.0;
        for (double t = signal.Start + Math.Ceiling((start - signal.Start) / step) * step; t <= end; t += step)
        {
            double age = now - t, progress = age / lifetime;
            signal.Position(t, out float x, out float y, out float z);
            // Mild sideways drift and buoyancy; newer smoke stays close to the source.
            x += (float)(Math.Cos(phase) * age * .35 + Math.Sin(t * .8 + phase) * Math.Sqrt(age) * .45);
            z += (float)(Math.Sin(phase) * age * .35 + Math.Cos(t * .7 + phase) * Math.Sqrt(age) * .45);
            y += (float)(age * .3);
            double ascentSpeed = Math.Max(0, signal.Vy - signal.Gravity * (t - signal.Start));
            float size = (float)(2 + age * .45 + ascentSpeed * step * .65);
            float opacity = (float)(.2 * Math.Pow(Math.Max(0, 1 - progress), 1.5));
            float warmth = signal.Alive(now) ? (float)Math.Exp(-age * .5) : 0;
            yield return new Puff(x, y, z, size, opacity, warmth);
        }
    }
}
