#nullable disable warnings
using System;
using System.Collections.Generic;
using CampaignGameState = global::FunProject.GameState.GameState;
using FunProject.Battle;
using FunProject.Combatants;
using FunProject.GameState;
using FunProject.Strategic;
using FunProject.Weapons;
using ZLinq;

namespace FunProject.Tests;

// The owning test fixture for geoscape tests: one retained CampaignGameState/GeoscapeSession
// pair per fixture instance, with the fixture recording every committed event behind an
// explicit assertion window (ClearEvents starts a new one; nothing clears it implicitly).
// Neither state nor session has a Dispose API, so disposal only detaches the fixture's own
// EventCommitted subscription and closes the fixture; the retained pair stays inspectable
// afterwards for lifecycle tests. AdvanceTicks is a setup convenience mirroring the old
// SessionAt/fired-event helpers: switch to Normal, advance once by ticks * 0.1, stay Normal.
// Raw speed/accumulator scenarios call ChangeSpeed/Advance with their own literal inputs.
public sealed class GeoscapeFixture : IDisposable
{
  private readonly List<IGeoscapeEvent> _events = [];
  private bool _disposed;

  public CampaignGameState State { get; }
  public GeoscapeSession Session { get; }
  public IReadOnlyList<IGeoscapeEvent> Events => _events;
  public GeoscapeEvent ActiveEvent => Session.ActiveEvents.AsValueEnumerable().Single();

  public GeoscapeFixture(CampaignStartData start)
  {
    State = new CampaignGameState(start);
    Session = new GeoscapeSession(State);
    Session.EventCommitted += RecordEvent;
  }

  public GeoscapeFixture(RegionData[] regions = null, ScheduledEventData[] timeline = null)
    : this(TestData.MakeStart(regions, timeline)) { }

  public static GeoscapeFixture WithFiredEvent(GeoscapeEventDefinition definition, RegionData[] regions = null)
  {
    var campaign = new GeoscapeFixture(regions, [TestData.MakeScheduled(1, definition)]);
    try
    {
      campaign.AdvanceTicks(1);
      return campaign;
    }
    catch
    {
      campaign.Dispose();
      throw;
    }
  }

  public void ChangeSpeed(TimeSpeed speed) { ThrowIfDisposed(); Session.ChangeSpeed(speed); }
  public void Advance(double deltaSeconds) { ThrowIfDisposed(); Session.Advance(deltaSeconds); }
  public void OpenResolution(GeoscapeEvent value) { ThrowIfDisposed(); Session.OpenResolution(value); }
  public void CompleteResolution(ResolutionOutcome outcome) { ThrowIfDisposed(); Session.CompleteResolution(outcome); }

  // Explicitly starts a new assertion window; never called by conveniences.
  public void ClearEvents() { ThrowIfDisposed(); _events.Clear(); }

  public void AdvanceTicks(int ticks)
  {
    ThrowIfDisposed();
    ArgumentOutOfRangeException.ThrowIfNegative(ticks);
    Session.ChangeSpeed(TimeSpeed.Normal);
    Session.Advance(ticks * 0.1);
  }

  // ---- Mission-return seeding ----------------------------------------------------------

  // One genuine battle per call over this campaign's own roster references: spawns the
  // squad with the campaign's current condition penalties as explicit spawn stat mods,
  // routes damage/stun through the normal damage pipeline, ends the battle, and returns
  // the un-applied player summary so tests can seed conditions exactly the way production
  // does (ApplyMissionReturn of a real battle result).
  public FactionBattleSummary PlayMission(
    BattleOutcome outcome = BattleOutcome.Victory,
    params (Combatant Combatant, int Damage, int Stun)[] squad)
  {
    ThrowIfDisposed();
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [State.PlayerFaction]);
    int slot = 0;
    foreach ((Combatant combatant, int damage, int stun) in squad)
    {
      BattleUnitState unit = battle.Spawn(combatant, new Vector3I(slot++, 0, 0),
        combatant.EquippedWeapon, combatant.EquippedArmor,
        State.Conditions.StatContributions(combatant));
      if (damage > 0)
        battle.ApplyDamage(unit, damage);
      if (stun > 0)
        battle.ApplyDamage(unit, stun, DamageKind.Stun);
    }
    battle.Session.EndBattle(outcome);
    return battle.Query(new GetFactionEndOfBattleSummary(State.PlayerFaction)).RequireRight();
  }

  // PlayMission plus the return application, for tests that only need the resulting
  // roster conditions.
  public void ReturnFromMission(params (Combatant Combatant, int Damage, int Stun)[] squad)
    => Session.ApplyMissionReturn(PlayMission(squad: squad));

  public void Dispose()
  {
    if (_disposed)
      return;
    Session.EventCommitted -= RecordEvent;
    _disposed = true;
  }

  private void RecordEvent(IGeoscapeEvent value) => _events.Add(value);

  private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
