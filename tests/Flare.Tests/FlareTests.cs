using System;
using System.Linq;
using Flare;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using SignalSupport;

namespace Flare.Tests;

[TestClass]
public sealed class FlareTests
{
    [TestMethod]
    public void SmokeReconstructsTheFlightAndPersistsAfterExtinction()
    {
        var signal = Shot(); signal.Vx = 10;
        var near = SmokeTrail.Sample(signal, 104, 20, 100).ToArray();
        var repeat = SmokeTrail.Sample(signal, 104, 20, 100).ToArray();
        CollectionAssert.AreEqual(near, repeat);
        Assert.IsTrue(near.Max(p => p.X) - near.Min(p => p.X) > 20);
        Assert.IsTrue(near.Max(p => p.Y) - near.Min(p => p.Y) > 70);
        Assert.IsTrue(SmokeTrail.Sample(signal, 104, 20, 3000).Any());
        Assert.IsTrue(SmokeTrail.Sample(signal, 104, 20, 3000).Count() < near.Length);
        signal.StopAt = 104;
        Assert.IsFalse(signal.Alive(105));
        Assert.IsTrue(SmokeTrail.Sample(signal, 110, 20, 100).All(p => p.Warmth == 0));
        Assert.IsTrue(SmokeTrail.Sample(signal, 110, 20, 100).Any());
        Assert.IsFalse(SmokeTrail.Sample(signal, 125, 20, 100).Any());
        signal.StopAt = 0;
        Assert.IsTrue(SmokeTrail.Sample(signal, signal.End + 5, 20, 100).Any());
        Assert.IsFalse(SmokeTrail.Sample(signal, signal.End + 61, 60, 100).Any());
        Assert.IsTrue(SmokeTrail.Sample(signal, signal.End, 60, 0).Count() <= 401);
    }

    [TestMethod]
    public void JoinSnapshotsIncludeResidualSmokeButExpireItsHistory()
    {
        var ledger = new SignalLedger(); var signal = Shot();
        ledger.Reserve(signal, 100); ledger.Commit(signal.Id, 7, 100);
        ledger.Stop(signal.Id, 7, 104);
        Assert.AreEqual(104d, signal.EmissionEnd);
        Assert.AreEqual(0, ledger.Active(105).Count());
        Assert.AreEqual(1, ledger.Recent(105).Count());
        Assert.AreEqual(0, ledger.Recent(164).Count());
    }

    [TestMethod]
    public void SmokeSettingsAreValidatedAndServerAuthoritative()
    {
        var changed = new LightingSettings(300, 3, 1, 40, 2);
        Assert.IsTrue(changed.Valid()); Assert.IsFalse(changed.Same(new LightingSettings()));
        Assert.IsFalse(new LightingSettings(300, 3, 1, 4).Valid());
        Assert.IsFalse(new LightingSettings(300, 3, 1, 61).Valid());
        Assert.IsFalse(new LightingSettings(300, 3, 1, 20, float.NaN).Valid());
        Assert.IsFalse(new LightingSettings(300, 3, 1, 20, 4).Valid());
        var state = new LightingState();
        Assert.IsFalse(state.Apply(8, 7, changed));
        Assert.IsTrue(state.Apply(7, 7, changed)); Assert.IsTrue(state.Current.Same(changed));
        Assert.IsFalse(typeof(LightingSettings).GetFields().Any(f => f.Name == "LightShafts"));
    }

    [TestMethod]
    public void ServerLightingUpdatesLiveAndRejectsOtherPeersOrInvalidSettings()
    {
        var state = new LightingState();
        Assert.IsFalse(state.Apply(0, 0, new LightingSettings()));
        Assert.IsFalse(state.Apply(8, 7, new LightingSettings(900, 10, 4)));
        Assert.IsFalse(state.Received);
        Assert.IsTrue(state.Apply(7, 7, new LightingSettings(400, 4, 2)));
        Assert.IsTrue(state.Received);
        Assert.IsTrue(state.Apply(7, 7, new LightingSettings(500, 6, 3)));
        Assert.AreEqual(500f, state.Current.Radius);
        Assert.IsFalse(state.Apply(7, 7, new LightingSettings(float.NaN, 6, 3)));
        Assert.AreEqual(500f, state.Current.Radius);
        state.Reset(); Assert.IsFalse(state.Received);
        Assert.AreEqual(250f, state.Current.Radius);
    }

    [TestMethod]
    public void ShotAuthorizationClaimsOnlyTheExactItemOnceWithoutAmmunition()
    {
        var firingItem = new object();
        var ticket = new ShotTicket<object>("shot", firingItem, 8);
        Assert.IsNull(ticket.Claim("other", 1));
        Assert.AreSame(firingItem, ticket.Claim("shot", 1));
        Assert.IsNull(ticket.Claim("shot", 1));
        Assert.IsNull(new ShotTicket<object>("expired", firingItem, 8).Claim("expired", 8));
    }
    private static Signal Shot(long owner = 7, double now = 100) => new() {
        Id = Guid.NewGuid().ToString("N"), Owner = owner, Start = now, Y = 20, Vy = Signal.LaunchSpeed(150, 1) };

    [TestMethod]
    public void VerticalFullDrawReaches150MetresThenDescendsOneMetrePerSecond()
    {
        var s = Shot();
        s.Position(s.Start + s.Apex, out float x, out float y, out float z);
        Assert.AreEqual(170f, y, .001f); Assert.AreEqual(0, x); Assert.AreEqual(0, z);
        s.Position(s.Start + s.Apex + 25, out _, out y, out _);
        Assert.AreEqual(145f, y, .001f);
        Assert.IsTrue(s.Alive(s.End - .001)); Assert.IsFalse(s.Alive(s.End));
        Assert.IsFalse(s.Alive(s.Start - 1));
    }

    [TestMethod]
    public void AimedShotTravelsSidewaysAndWeakDrawDoesNotReachFullHeight()
    {
        var s = Shot(); s.Vx = s.Vy / (float)Math.Sqrt(2); s.Vy = s.Vx;
        s.Position(s.Start + s.Apex, out float x, out float y, out _);
        Assert.IsTrue(x > 50); Assert.AreEqual(95f, y, .001f);
        s.Position(s.Start + s.Apex + 5, out float laterX, out _, out _);
        Assert.IsTrue(laterX > x);
        Assert.AreEqual(x + s.Vx / 2, laterX, .002f);
        Assert.IsTrue(Signal.LaunchSpeed(150, .5f) < Signal.LaunchSpeed(150, 1));
        Assert.AreEqual(Signal.LaunchSpeed(150, 0), Signal.LaunchSpeed(150, -2));
    }

    [TestMethod]
    public void HorizontalAndDownwardShotsFollowAimBeforeDescendingWithoutRising()
    {
        var s = Shot(); s.Vy = -20; s.Vx = 30;
        Assert.AreEqual(.5, s.Apex);
        s.Position(s.Start + 2, out float x, out float y, out _);
        Assert.AreEqual(29.2532f, x, .0001f); Assert.AreEqual(7.25f, y);
        s.Vy = 0;
        Assert.IsTrue(s.Valid()); Assert.AreEqual(60.5, s.End - s.Start);
    }

    [TestMethod]
    public void ReservationsDoNotBroadcastAndOnlyTheOwnerCanCommitOnce()
    {
        var ledger = new SignalLedger(); var s = Shot();
        Assert.IsTrue(ledger.Reserve(s, 100));
        Assert.AreEqual(0, ledger.Active(100).Count());
        Assert.IsNull(ledger.Commit(s.Id, 8, 100));
        Assert.IsNotNull(ledger.Commit(s.Id, 7, 101));
        Assert.IsNull(ledger.Commit(s.Id, 7, 102));
        Assert.AreEqual(1, ledger.Active(102).Count());
        Assert.IsFalse(ledger.Reserve(s, 103));
    }

    [TestMethod]
    public void TimedOutReservationsAndStoppedOrExpiredSignalsCannotBeResurrected()
    {
        var ledger = new SignalLedger(); var s = Shot();
        Assert.IsTrue(ledger.Reserve(s, 100));
        Assert.IsNull(ledger.Commit(s.Id, 7, 110));
        s = Shot(now: 111); Assert.IsTrue(ledger.Reserve(s, 111));
        Assert.IsNotNull(ledger.Commit(s.Id, 7, 111));
        Assert.IsFalse(ledger.Stop(s.Id, 8, 112)); Assert.IsTrue(ledger.Stop(s.Id, 7, 112));
        Assert.IsFalse(ledger.Stop(s.Id, 7, 112)); Assert.AreEqual(0, ledger.Active(112).Count());
        Assert.IsNull(ledger.Commit(s.Id, 7, 112)); Assert.IsFalse(ledger.Reserve(s, 112));
        var other = Shot(9, 112); Assert.IsTrue(ledger.Reserve(other, 112)); ledger.Commit(other.Id, 9, 112);
        Assert.AreEqual(0, ledger.Active(other.End).Count());
        ledger.Clear(); Assert.AreEqual(0, ledger.Active(112).Count());
    }

    [TestMethod]
    public void LimitsAndMalformedFlightDataAreRejected()
    {
        var ledger = new SignalLedger(); var invalid = Shot(); invalid.Vy = float.NaN;
        Assert.IsFalse(ledger.Reserve(invalid, 100));
        invalid = Shot(); invalid.Burn = 10000; Assert.IsFalse(invalid.Valid());
        invalid = Shot(); invalid.X = float.PositiveInfinity; Assert.IsFalse(invalid.Valid());
        invalid = Shot(); invalid.Start = double.NaN; Assert.IsFalse(invalid.Valid());
        for (int i = 0; i < 64; i++) Assert.IsTrue(ledger.Reserve(Shot(i), 100));
        Assert.IsFalse(ledger.Reserve(Shot(1000), 100));
        ledger.Clear(); Assert.IsTrue(ledger.Reserve(Shot(), 100));
        Assert.IsFalse(ledger.Reserve(Shot(), 100.1));
    }

    [TestMethod]
    public void LightingBoundsAllowDisablingAndRejectInvalidNumbers()
    {
        Assert.IsTrue(new LightingSettings().Valid());
        Assert.IsTrue(new LightingSettings(0, 0, 0).Valid());
        Assert.IsTrue(new LightingSettings(1000, 20, 4).Valid());
        Assert.IsFalse(new LightingSettings(-1, 3, 1).Valid());
        Assert.IsFalse(new LightingSettings(1001, 3, 1).Valid());
        Assert.IsFalse(new LightingSettings(300, 21, 1).Valid());
        Assert.IsFalse(new LightingSettings(300, 3, float.NaN).Valid());
        Assert.IsFalse(new LightingSettings(float.PositiveInfinity, 3, 1).Valid());
    }

    [TestMethod]
    public void LightingChangesAreDetectedIndependentlyAndFadeEndsAtZero()
    {
        var initial = new LightingSettings();
        Assert.IsTrue(initial.Same(new LightingSettings()));
        Assert.IsFalse(initial.Same(new LightingSettings(400, 3, 1)));
        Assert.IsFalse(initial.Same(new LightingSettings(250, 4, 1)));
        Assert.IsFalse(initial.Same(new LightingSettings(250, 3, 2)));
        Assert.AreEqual(1f, LightingSettings.Fade(60));
        Assert.AreEqual(.5f, LightingSettings.Fade(1.5));
        Assert.AreEqual(0f, LightingSettings.Fade(0));
        Assert.AreEqual(0f, LightingSettings.Fade(-1));
        Assert.IsFalse(typeof(LightingSettings).GetFields().Any(f => f.Name.Contains("Shadow")));
    }
}
