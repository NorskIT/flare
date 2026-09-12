using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Flare;

internal sealed class SignalNetwork : IDisposable
{
    private const string RpcName = "norskit_flare_rpc_v3";
    private enum Kind { Request, Ready, Commit, Spawn, StopRequest, Stop, Snapshot, Synced, Reject, Lighting, Reload, Set, Notice }
    private sealed class Pending
    {
        internal ShotTicket<ItemDrop.ItemData> Ticket = null!;
        internal Humanoid Player = null!;
    }
    internal sealed class View
    {
        internal Signal Signal = null!;
        internal Vector3 Previous;
        internal bool HasPrevious, CollisionReported;
    }
    private readonly Plugin plugin;
    private readonly SignalLedger ledger = new();
    internal readonly Dictionary<string, View> Views = new();
    private readonly Dictionary<string, double> stopped = new();
    private readonly Dictionary<long, float> snapshotTimes = new();
    private ZRoutedRpc? router;
    private Pending? pending;
    private bool synced, disposed;
    private readonly LightingState lightingState = new();
    internal LightingSettings Lighting => lightingState.Current;
    private LightingSettings? lastLighting;
    private readonly Dictionary<long, float> adminTimes = new();
    private float nextSync;
    internal static readonly int SolidMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
    internal double Now => ZNet.instance ? ZNet.instance.GetTimeSeconds() : 0;
    private bool Server => ZNet.instance && ZNet.instance.IsServer();
    private long ServerId => Server ? ZNet.GetUID() : ZNet.instance?.GetServerPeer()?.m_uid ?? 0;
    internal bool CanFire => !disposed && router != null && ServerId != 0 && synced && pending == null;
    internal string Status => $"Flare {Plugin.Version}: active={Views.Count}, ready={synced}, pending={pending != null}, server={Server}";
    internal SignalNetwork(Plugin plugin) { this.plugin = plugin; }

    internal void Update()
    {
        if (disposed) return;
        if (router != ZRoutedRpc.instance || !ZNet.instance)
        {
            Reset();
            router = ZNet.instance ? ZRoutedRpc.instance : null;
            if (router != null) router.Register<ZPackage>(RpcName, Receive);
        }
        if (router == null || !ZNet.instance) return;
        if (Server) { synced = true; BroadcastLighting(); }
        if (Player.m_localPlayer && !synced && ServerId != 0 && Time.realtimeSinceStartup >= nextSync)
        {
            nextSync = Time.realtimeSinceStartup + 3;
            Send(ServerId, Kind.Snapshot);
        }
        if (pending != null && Time.realtimeSinceStartup >= pending.Ticket.Deadline)
        {
            pending.Player?.Message(MessageHud.MessageType.Center, "Signal was not fired. Please try again.");
            pending = null;
        }
        double now = Now;
        foreach (var view in Views.Values.ToArray())
        {
            var s = view.Signal;
            if (now >= s.EmissionEnd + 60) { Views.Remove(s.Id); continue; }
            if (!s.Alive(now)) continue;
            if (now < s.Start) continue;
            var current = Position(s, now);
            // Only the shooter reports physical hits, so observers loading different
            // chunks cannot disagree and prematurely end somebody else's signal.
            if (s.Owner == ZNet.GetUID() && !view.CollisionReported)
            {
                var previous = view.HasPrevious ? view.Previous : new Vector3(s.X, s.Y, s.Z);
                if (Physics.Linecast(previous, current, SolidMask, QueryTriggerInteraction.Ignore))
                {
                    view.CollisionReported = true;
                    Send(ServerId, Kind.StopRequest, p => p.Write(s.Id));
                }
            }
            view.Previous = current; view.HasPrevious = true;
        }
        foreach (var id in stopped.Where(p => p.Value < now).Select(p => p.Key).ToArray()) stopped.Remove(id);
        if (Server) ledger.Prune(now);
    }

    internal void Fire(Humanoid player, ItemDrop.ItemData bow, float draw)
    {
        if (!CanFire || !Plugin.IsBow(bow) || !player.GetInventory().ContainsItem(bow)) return;
        var id = Guid.NewGuid().ToString("N");
        // Start at the hands, inside the shooter's local collision area.
        var origin = player.transform.position + Vector3.up * 1.5f + player.transform.forward * .5f;
        var direction = player.GetAimDir(origin).normalized;
        pending = new Pending { Player = player, Ticket = new ShotTicket<ItemDrop.ItemData>(id, bow, Time.realtimeSinceStartup + 8) };
        Send(ServerId, Kind.Request, p => { p.Write(id); p.Write(origin); p.Write(direction); p.Write(Mathf.Clamp01(draw)); });
    }

    private void Send(long target, Kind kind, Action<ZPackage>? write = null)
    {
        if (router == null) return;
        var package = new ZPackage(); package.Write((int)kind); write?.Invoke(package);
        router.InvokeRoutedRPC(target, RpcName, package);
    }

    private void Receive(long sender, ZPackage package)
    {
        if (disposed || !ZNet.instance) return;
        try
        {
            if (package.Size() > 1024) return;
            var kind = (Kind)package.ReadInt();
            bool fromServer = sender == ServerId;
            switch (kind)
            {
                case Kind.Request when Server:
                    Request(sender, package); break;
                case Kind.Ready when fromServer:
                    Ready(package.ReadString()); break;
                case Kind.Reject when fromServer:
                    if (pending?.Ticket.Id == package.ReadString())
                    {
                        pending.Player.Message(MessageHud.MessageType.Center, "Signal rejected. Wait a moment and try again.");
                        pending = null;
                    }
                    break;
                case Kind.Commit when Server:
                    var committed = ledger.Commit(package.ReadString(), sender, Now);
                    if (committed != null) Send(ZRoutedRpc.Everybody, Kind.Spawn, p => Write(p, committed));
                    break;
                case Kind.Spawn when fromServer:
                    var signal = Read(package);
                    if (signal.Valid() && signal.EmissionEnd + 60 > Now && signal.Start <= Now + 5 && !stopped.ContainsKey(signal.Id) && !Views.ContainsKey(signal.Id))
                    {
                        if (Views.Count >= 128)
                        {
                            var oldest = Views.Where(v => !v.Value.Signal.Alive(Now)).OrderBy(v => v.Value.Signal.EmissionEnd).FirstOrDefault();
                            if (oldest.Key != null) Views.Remove(oldest.Key);
                        }
                        if (Views.Count < 128) Views.Add(signal.Id, new View { Signal = signal });
                    }
                    break;
                case Kind.StopRequest when Server:
                    var id = package.ReadString();
                    double stopTime = Now;
                    if (ledger.Stop(id, sender, stopTime)) Send(ZRoutedRpc.Everybody, Kind.Stop, p => { p.Write(id); p.Write(stopTime); });
                    break;
                case Kind.Stop when fromServer:
                    var ended = package.ReadString();
                    double endedAt = package.ReadDouble();
                    if (Guid.TryParseExact(ended, "N", out _) && !double.IsNaN(endedAt) && !double.IsInfinity(endedAt))
                    {
                        if (Views.TryGetValue(ended, out var endedView)) endedView.Signal.StopAt = Math.Max(endedView.Signal.Start, endedAt);
                        stopped[ended] = Now + 300;
                    }
                    break;
                case Kind.Snapshot when Server:
                    if (!KnownPeer(sender) || (snapshotTimes.TryGetValue(sender, out float last) && Time.realtimeSinceStartup - last < 2)) break;
                    snapshotTimes[sender] = Time.realtimeSinceStartup;
                    SendLighting(sender);
                    foreach (var active in ledger.Recent(Now).OrderByDescending(s => s.Start).Take(128)) Send(sender, Kind.Spawn, p => Write(p, active));
                    Send(sender, Kind.Synced);
                    break;
                case Kind.Synced when fromServer:
                    synced = lightingState.Received; break;
                case Kind.Lighting when fromServer:
                    var settings = new LightingSettings(package.ReadSingle(), package.ReadSingle(), package.ReadSingle(), package.ReadSingle(), package.ReadSingle());
                    lightingState.Apply(sender, ServerId, settings);
                    break;
                case Kind.Reload when Server:
                    if (AuthorizeAdmin(sender))
                    {
                        plugin.Config.Reload(); BroadcastLighting();
                        Send(sender, Kind.Notice, p => p.Write("Server Flare settings reloaded."));
                    }
                    break;
                case Kind.Set when Server:
                    var key = package.ReadString(); var value = package.ReadSingle();
                    if (AuthorizeAdmin(sender))
                    {
                        var result = plugin.SetServerSetting(key, value);
                        BroadcastLighting(); Send(sender, Kind.Notice, p => p.Write(result));
                    }
                    break;
                case Kind.Notice when fromServer:
                    if (global::Console.instance) global::Console.instance.AddString(package.ReadString());
                    break;
            }
        }
        catch (Exception e) { plugin.Log("Ignored invalid Flare message: " + e.GetType().Name); }
    }

    private bool KnownPeer(long sender) => sender == ZNet.GetUID() || ZNet.instance.GetPeer(sender)?.IsReady() == true;
    private void Request(long sender, ZPackage package)
    {
        if (!KnownPeer(sender)) return;
        var id = package.ReadString(); var origin = package.ReadVector3(); var direction = package.ReadVector3(); var draw = package.ReadSingle();
        if (!Guid.TryParseExact(id, "N", out _) || !Signal.Finite(draw) || draw < 0 || draw > 1 ||
            !Signal.Finite(direction.sqrMagnitude) || Math.Abs(direction.sqrMagnitude - 1) > .02f) return;
        Vector3 reference = sender == ZNet.GetUID() ? ZNet.instance.GetReferencePosition() : ZNet.instance.GetPeer(sender).m_refPos;
        var speed = Signal.LaunchSpeed(plugin.Height.Value, draw);
        var velocity = direction * speed;
        var signal = new Signal { Id = id, Owner = sender, Start = Now, X = origin.x, Y = origin.y, Z = origin.z,
            Vx = velocity.x, Vy = velocity.y, Vz = velocity.z, Burn = plugin.Burn.Value, Descent = plugin.Descent.Value };
        if (Vector3.Distance(origin, reference) > 32 || !ledger.Reserve(signal, Now))
        { Send(sender, Kind.Reject, p => p.Write(id)); return; }
        Send(sender, Kind.Ready, p => p.Write(id));
    }

    private void Ready(string id)
    {
        var shot = pending;
        if (shot == null) return;
        var bow = shot.Ticket.Claim(id, Time.realtimeSinceStartup);
        if (bow == null) return;
        pending = null; // Clear before invoking local host RPCs; duplicate Ready consumes nothing.
        if (!shot.Player || shot.Player != Player.m_localPlayer || shot.Player.IsDead() || !Plugin.IsBow(bow)) return;
        var inventory = shot.Player.GetInventory();
        if (!inventory.ContainsItem(bow)) return;
        // Only the exact firing item is consumed, on Unity's main thread.
        shot.Player.UnequipItem(bow, false);
        inventory.RemoveItem(bow);
        Send(ServerId, Kind.Commit, p => p.Write(id));
    }

    internal static Vector3 Position(Signal signal, double now)
    { signal.Position(now, out float x, out float y, out float z); return new Vector3(x, y, z); }
    private static void Write(ZPackage p, Signal s)
    {
        p.Write(s.Id); p.Write(s.Owner); p.Write(s.Start);
        p.Write(new Vector3(s.X, s.Y, s.Z)); p.Write(new Vector3(s.Vx, s.Vy, s.Vz));
        p.Write(s.Gravity); p.Write(s.Burn); p.Write(s.Descent); p.Write(s.StopAt);
    }
    private static Signal Read(ZPackage p)
    {
        var s = new Signal { Id = p.ReadString(), Owner = p.ReadLong(), Start = p.ReadDouble() };
        var pos = p.ReadVector3(); var vel = p.ReadVector3();
        s.X = pos.x; s.Y = pos.y; s.Z = pos.z; s.Vx = vel.x; s.Vy = vel.y; s.Vz = vel.z;
        s.Gravity = p.ReadSingle(); s.Burn = p.ReadSingle(); s.Descent = p.ReadSingle(); s.StopAt = p.ReadDouble();
        return s;
    }
    private void BroadcastLighting()
    {
        var next = new LightingSettings(plugin.LightRadius.Value, plugin.LightIntensity.Value, plugin.Strength.Value, plugin.SmokeLifetime.Value, plugin.SmokeStrength.Value);
        if (!next.Valid()) return;
        lightingState.Apply(ServerId, ServerId, next);
        if (lastLighting != null && lastLighting.Same(next)) return;
        lastLighting = next;
        SendLighting(ZRoutedRpc.Everybody);
    }
    private void SendLighting(long target) => Send(target, Kind.Lighting, p => {
        p.Write(Lighting.Radius); p.Write(Lighting.Intensity); p.Write(Lighting.Glow);
        p.Write(Lighting.SmokeLifetime); p.Write(Lighting.SmokeStrength);
    });
    private bool AuthorizeAdmin(long sender)
    {
        var peer = ZNet.instance.GetPeer(sender);
        bool allowed = sender == ZNet.GetUID() || (peer != null && peer.IsReady() && ZNet.instance.IsAdmin(peer.m_socket.GetHostName()));
        if (!allowed) { Send(sender, Kind.Notice, p => p.Write("Only the server host or an administrator may change Flare settings.")); return false; }
        if (adminTimes.TryGetValue(sender, out float last) && Time.realtimeSinceStartup - last < .2f) return false;
        adminTimes[sender] = Time.realtimeSinceStartup;
        return true;
    }
    internal string Reload()
    {
        if (router == null || ServerId == 0) return "Enter a world first.";
        Send(ServerId, Kind.Reload);
        return "Requested server settings reload.";
    }
    internal string Set(string key, float value)
    {
        if (router == null || ServerId == 0) return "Enter a world first.";
        Send(ServerId, Kind.Set, p => { p.Write(key); p.Write(value); });
        return "Requested server settings change.";
    }
    private void Reset()
    {
        // The router belongs to a single world. Its callbacks are inert after disposal.
        Views.Clear(); stopped.Clear(); ledger.Clear(); snapshotTimes.Clear(); adminTimes.Clear(); pending = null; synced = false; nextSync = 0;
        lightingState.Reset(); lastLighting = null;
    }
    public void Dispose() { disposed = true; Reset(); router = null; }
}
