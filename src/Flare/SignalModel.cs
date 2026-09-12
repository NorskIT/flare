using System;
using System.Collections.Generic;
using System.Linq;

namespace Flare;

public sealed class Signal
{
    public string Id = "";
    public long Owner;
    public double Start;
    public double StopAt;
    public float X, Y, Z, Vx, Vy, Vz;
    public float Gravity = 10, Burn = 60, Descent = 1;
    // Even a horizontal/downward shot travels briefly along the aim direction.
    public double Apex => Math.Max(.5, Vy / Gravity);
    public double End => Start + Apex + Burn;
    public double EmissionEnd => StopAt > 0 ? Math.Min(StopAt, End) : End;
    public bool Alive(double now) => now >= Start && now < EmissionEnd;

    public void Position(double now, out float x, out float y, out float z)
    {
        double age = Math.Max(0, now - Start), up = Math.Min(age, Apex);
        // Ballistic ascent follows the aim. At the apex the burning signal sheds
        // horizontal momentum smoothly, drifting briefly before settling.
        double falling = Math.Max(0, age - Apex);
        double horizontal = up + (1 - Math.Exp(-2 * falling)) / 2;
        x = X + (float)(Vx * horizontal);
        z = Z + (float)(Vz * horizontal);
        y = Y + (float)(Vy * up - Gravity * up * up / 2 - Math.Max(0, age - Apex) * Descent);
    }

    public static float LaunchSpeed(float height, float draw) =>
        (float)Math.Sqrt(2 * 10 * height) * (0.35f + 0.65f * Math.Max(0, Math.Min(1, draw)));

    public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    public bool Valid() => Guid.TryParseExact(Id, "N", out _) &&
        !double.IsNaN(Start) && !double.IsInfinity(Start) &&
        !double.IsNaN(StopAt) && !double.IsInfinity(StopAt) && (StopAt == 0 || StopAt >= Start) &&
        new[] { X, Y, Z, Vx, Vy, Vz, Gravity, Burn, Descent }.All(Finite) &&
        Math.Abs(X) <= 20000 && Math.Abs(Z) <= 20000 && Math.Abs(Y) <= 20000 &&
        Math.Abs(Vx) <= 200 && Math.Abs(Vy) <= 200 && Math.Abs(Vz) <= 200 &&
        Gravity == 10 && Burn >= 5 && Burn <= 180 && Descent >= .2f && Descent <= 10;
}

// Reservations are private until the firing client has consumed its exact items.
// Completed IDs remain tombstoned so delayed messages cannot resurrect a signal.
public sealed class SignalLedger
{
    private sealed class Entry
    {
        public Signal Signal = null!;
        public double Reserved;
        public bool Committed, Stopped;
    }
    private readonly Dictionary<string, Entry> entries = new();
    public IEnumerable<Signal> Active(double now) => entries.Values
        .Where(e => e.Committed && !e.Stopped && e.Signal.Alive(now)).Select(e => e.Signal);
    public IEnumerable<Signal> Recent(double now) => entries.Values
        .Where(e => e.Committed && now < e.Signal.EmissionEnd + 60).Select(e => e.Signal);
    public bool Reserve(Signal signal, double now)
    {
        Prune(now);
        if (!signal.Valid() || entries.ContainsKey(signal.Id) || entries.Count >= 2048 ||
            entries.Values.Any(e => e.Signal.Owner == signal.Owner && now - e.Reserved < 1)) return false;
        if (entries.Values.Count(e => !e.Stopped && (e.Committed ? e.Signal.End > now : now - e.Reserved < 10)) >= 64) return false;
        entries.Add(signal.Id, new Entry { Signal = signal, Reserved = now });
        return true;
    }
    public Signal? Commit(string id, long owner, double now)
    {
        if (!entries.TryGetValue(id, out var e) || e.Signal.Owner != owner || e.Committed || e.Stopped || now - e.Reserved >= 10) return null;
        e.Committed = true;
        e.Signal.Start = now;
        return e.Signal;
    }
    public bool Stop(string id, long owner, double now)
    {
        if (!entries.TryGetValue(id, out var e) || e.Signal.Owner != owner || !e.Committed || e.Stopped) return false;
        e.Stopped = true;
        e.Signal.StopAt = Math.Max(e.Signal.Start, now);
        return true;
    }
    public void Prune(double now)
    {
        foreach (var id in entries.Where(p => now > Math.Max(p.Value.Signal.End, p.Value.Reserved + 10) + 120).Select(p => p.Key).ToArray()) entries.Remove(id);
    }
    public void Clear() => entries.Clear();
}
