using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Core;
using FunProject.Items.Effects;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public partial class BlastResolutionTest
{
  [TestCase(TestName = "Thrown grenade damages an enemy within the blast radius")]
  public void ThrownGrenadeDamagesEnemyWithinBlastRadius()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(8, 1, 8), [playerFaction, enemyFaction]);
    var thrower = battle.Spawn(TestData.MakeCombatant("Thrower", playerFaction, actionPoints: 4), new Vector3I(1, 0, 1));
    var enemy = battle.Spawn(TestData.MakeCombatant("Hostile", enemyFaction, health: 20), new Vector3I(3, 0, 1));
    battle.Start();

    var grenade = TestData.MakeGrenade("Frag", throwRange: 6, blastRadius: 1, effects: [new DamageEffectData { BaseDamage = 8, Element = Element.Kinetic }]);
    thrower.AddInventoryItem(grenade.Item);
    battle.ClearEvents();
    battle.Throw(thrower, grenade, new Vector3I(3, 0, 1));

    Assert.Equal(12, enemy.CurrentHealth);
    Assert.True(battle.Events.EventsOf<UnitDamagedBattleEvent>().AsValueEnumerable().Any(e => ReferenceEquals(e.Unit, enemy)));
    Assert.True(battle.Events.EventsOf<CapabilityResolvedBattleEvent>().AsValueEnumerable().Any());
  }

  [TestCase(TestName = "A unit outside the blast radius is unaffected")]
  public void UnitOutsideBlastRadiusIsUnaffected()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(8, 1, 8), [playerFaction, enemyFaction]);
    var thrower = battle.Spawn(TestData.MakeCombatant("Thrower", playerFaction, actionPoints: 4), new Vector3I(1, 0, 1));
    var nearEnemy = battle.Spawn(TestData.MakeCombatant("Near", enemyFaction, health: 20), new Vector3I(3, 0, 1));
    var farEnemy = battle.Spawn(TestData.MakeCombatant("Far", enemyFaction, health: 20), new Vector3I(3, 0, 4));
    battle.Start();

    var grenade = TestData.MakeGrenade("Frag", throwRange: 6, blastRadius: 1, effects: [new DamageEffectData { BaseDamage = 8, Element = Element.Kinetic }]);
    thrower.AddInventoryItem(grenade.Item);
    battle.ClearEvents();
    battle.Throw(thrower, grenade, new Vector3I(3, 0, 1));

    Assert.Equal(12, nearEnemy.CurrentHealth);
    Assert.Equal(20, farEnemy.CurrentHealth);
    Assert.False(battle.Events.EventsOf<UnitDamagedBattleEvent>().AsValueEnumerable().Any(e => ReferenceEquals(e.Unit, farEnemy)));
  }

  [TestCase(TestName = "Friendly-fire: an allied unit within the blast radius is also damaged")]
  public void FriendlyFireDamagesAlliedUnitWithinRadius()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(8, 1, 8), [playerFaction, enemyFaction]);
    var thrower = battle.Spawn(TestData.MakeCombatant("Thrower", playerFaction, actionPoints: 4), new Vector3I(1, 0, 1));
    var ally = battle.Spawn(TestData.MakeCombatant("Ally", playerFaction, health: 20), new Vector3I(3, 0, 1));
    var enemy = battle.Spawn(TestData.MakeCombatant("Hostile", enemyFaction, health: 20), new Vector3I(4, 0, 1));
    battle.Start();

    var grenade = TestData.MakeGrenade("Frag", throwRange: 6, blastRadius: 1, effects: [new DamageEffectData { BaseDamage = 8, Element = Element.Kinetic }]);
    thrower.AddInventoryItem(grenade.Item);
    battle.ClearEvents();
    battle.Throw(thrower, grenade, new Vector3I(4, 0, 1));

    // Target (4,0,1): the enemy (distance 0) and the allied unit (distance 1) are both in radius.
    Assert.Equal(12, enemy.CurrentHealth);
    Assert.Equal(12, ally.CurrentHealth);
    Assert.True(battle.Events.EventsOf<UnitDamagedBattleEvent>().AsValueEnumerable().Any(e => ReferenceEquals(e.Unit, ally)));
  }

  [TestCase(TestName = "A status effect in the blast applies to an in-radius unit")]
  public void StatusEffectInBlastAppliesToInRadiusUnit()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(8, 1, 8), [playerFaction, enemyFaction]);
    var thrower = battle.Spawn(TestData.MakeCombatant("Thrower", playerFaction, actionPoints: 4), new Vector3I(1, 0, 1));
    var enemy = battle.Spawn(TestData.MakeCombatant("Hostile", enemyFaction, health: 20), new Vector3I(3, 0, 1));
    battle.Start();

    var grenade = TestData.MakeGrenade(
      "Glue Bomb",
      throwRange: 6,
      blastRadius: 1,
      effects: [new ImmobilizeStatusSpecData { DurationTurns = 2, ApplyChancePercent = 100 }]);
    thrower.AddInventoryItem(grenade.Item);
    battle.ClearEvents();
    battle.Throw(thrower, grenade, new Vector3I(3, 0, 1));

    Assert.True(enemy.IsImmobilized);
    Assert.True(battle.Events.EventsOf<UnitStatusEffectAppliedBattleEvent>().AsValueEnumerable().Any(e => ReferenceEquals(e.Unit, enemy)));
  }

  // Reaction double: queues a PassUnit interrupt on the first damage event it sees, so the
  // terminal-blast case can prove queued reactions are discarded at settlement.
  private sealed class QueueReactionOnDamage(BattleUnitState unit) : BattleHook
  {
    private bool _queued;

    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      if (_queued || battleEvent is not UnitDamagedBattleEvent damaged
        || !ReferenceEquals(damaged.Unit, unit))
        return [];
      _queued = true;
      return [BattleAction.PassUnit(context.Read.State.TryGetAlive(unit).RequireSome())];
    }
  }

  [TestCase(TestName = "A terminal request at the first blast recipient still damages the remaining recipients")]
  public void TerminalRequestAtFirstRecipientStillDamagesRemainingRecipients()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(8, 1, 8), [playerFaction, enemyFaction],
      playerFaction: Some(playerFaction));
    var thrower = battle.Spawn(TestData.MakeCombatant("Thrower", playerFaction, actionPoints: 4), new Vector3I(1, 0, 1));
    var first = battle.Spawn(TestData.MakeCombatant("First", enemyFaction, health: 30), new Vector3I(3, 0, 1));
    var captured = battle.Spawn(TestData.MakeCombatant("Captured", enemyFaction, health: 12), new Vector3I(4, 0, 1));
    var laterKilled = battle.Spawn(TestData.MakeCombatant("Later", enemyFaction, health: 6), new Vector3I(5, 0, 1));
    battle.Damage(captured, 6, DamageKind.Stun); // unconscious once health drops to 6 or less
    battle.Damage(laterKilled, 5, DamageKind.Stun); // unconscious at 5, then killed by the second packet
    var crate = battle.PlaceObject(TestData.MakeObject("Crate", 5), new Vector3I(4, 0, 2));
    battle.AddObjective(playerFaction, new FakeObjective(new FakeObjectiveData
    {
      Complete = true,
      Observe = typeof(UnitDamagedBattleEvent),
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    }));
    battle.Start();
    battle.RegisterHook<UnitDamagedBattleEvent>(new QueueReactionOnDamage(first), 0);
    BattleOutcome observedOutcome = BattleOutcome.Draw;
    int observedKilled = -1;
    List<Combatant> observedCaptured = [];
    battle.OnCommitted(battleEvent =>
    {
      if (battleEvent is not SessionEndedBattleEvent)
        return;
      CompletedBattle completed = battle.Query(new GetCompletedBattleQuery()).RequireSome();
      observedOutcome = completed.Outcome;
      observedKilled = completed.Factions[enemyFaction].Killed;
      observedCaptured.AddRange(completed.FactionSummaries[playerFaction].CapturedEnemies);
    });
    var grenade = TestData.MakeGrenade("Frag", throwRange: 10, blastRadius: 1,
      effects: [new DamageEffectData { BaseDamage = 5 }, new DamageEffectData { BaseDamage = 5 }]);
    thrower.AddInventoryItem(grenade.Item);
    battle.ClearEvents();

    battle.Throw(thrower, grenade, new Vector3I(4, 0, 1)); // the first recipient's damage flips the objective

    // The remaining eligible recipients still took every synchronous packet.
    Assert.Equal(20, first.CurrentHealth);
    Assert.True(captured.IsUnconscious);
    Assert.True(laterKilled.IsDead);
    Assert.Equal(Some(ObjectStatus.Destroyed), crate.Status);
    Assert.Equal(BattleOutcome.Victory, battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
    // End callback saw the frozen report: dead units are absent from captures, the surviving
    // unconscious body is present, and the kill count is final.
    Assert.Equal(BattleOutcome.Victory, observedOutcome);
    Assert.Equal(1, observedKilled);
    Assert.Equal(1, observedCaptured.Count);
    Assert.True(ReferenceEquals(captured.Combatant, observedCaptured.AsValueEnumerable().Single()));
    // The queued reaction never ran: settlement discarded it.
    Assert.Equal(0, battle.Events.EventsOf<UnitActivationEndedBattleEvent>().AsValueEnumerable().Count());
    Assert.Equal(typeof(SessionEndedBattleEvent), battle.Events.AsValueEnumerable().Last().GetType());
  }

  [TestCase]
  public void BlastDamagesObjectsAndUnitsAndSkipsIneligibleObjects()
  {
    using var battle = BattleFixture.Duel(start: false,
      player: new("Shooter", Weapon: TestData.MakeWeapon("Rifle", damage: 5)));
    var origin = new Vector3I(3, 0, 4);
    var near = battle.PlaceObject(TestData.MakeObject("Near", 20), origin);
    var far = battle.PlaceObject(TestData.MakeObject("Far", 20), new Vector3I(0, 0, 7));
    var scenery = battle.PlaceObject(TestData.MakeObject(), new Vector3I(2, 0, 4));
    var resolved = battle.PlaceObject(TestData.MakeObject("Resolved", 20,
      new InteractiveCapabilityData()), new Vector3I(3, 0, 3));
    var destroyed = battle.PlaceObject(TestData.MakeObject("Destroyed", 5), new Vector3I(3, 0, 5));
    battle.Start();
    battle.Interact(battle.PlayerUnit, resolved);
    battle.Attack(battle.PlayerUnit, destroyed);
    var grenade = TestData.MakeGrenade("Frag", throwRange: 10, blastRadius: 1,
      effects: [new DamageEffectData { BaseDamage = 8, DamageTerrain = false }]);
    battle.PlayerUnit.AddInventoryItem(grenade.Item);
    battle.ClearEvents();
    battle.Throw(battle.PlayerUnit, grenade, origin);
    Assert.Equal(12, near.FindCapability<ObjectHealthCapability>().RequireSome().CurrentHealth);
    Assert.Equal(12, battle.EnemyUnit.CurrentHealth);
    Assert.Equal(20, far.FindCapability<ObjectHealthCapability>().RequireSome().CurrentHealth);
    Assert.Equal(20, resolved.FindCapability<ObjectHealthCapability>().RequireSome().CurrentHealth);
    Assert.Equal(Some(ObjectStatus.Destroyed), destroyed.Status);
    Assert.Equal(0, battle.Events.EventsOf<ObjectDestroyedBattleEvent>().Length);
    Assert.True(scenery.Status.IsNone);
    Assert.True(battle.Events.SingleEvent<ObjectDamagedBattleEvent>().MaybeCause.IsNone);
    battle.Events.EventBefore<ObjectDamagedBattleEvent, CapabilityResolvedBattleEvent>();
  }

  [TestCase]
  public void RadiusZeroMultipleEffectsAndStatusesDoNotRepeatDestruction()
  {
    using var battle = BattleFixture.Duel(start: false);
    var origin = new Vector3I(3, 0, 2);
    var obj = battle.PlaceObject(TestData.MakeObject("Crate", 5), origin);
    var neighbor = battle.PlaceObject(TestData.MakeObject("Neighbor", 20), new Vector3I(2, 0, 2));
    battle.Start();
    var grenade = TestData.MakeGrenade("Mixed", throwRange: 10, blastRadius: 0,
      effects: [TestData.MakeBurn(), new DamageEffectData { BaseDamage = 5 },
        new DamageEffectData { BaseDamage = 5 }]);
    battle.PlayerUnit.AddInventoryItem(grenade.Item);
    battle.ClearEvents();
    battle.Throw(battle.PlayerUnit, grenade, origin);
    Assert.Equal(Some(ObjectStatus.Destroyed), obj.Status);
    Assert.Equal(20, neighbor.FindCapability<ObjectHealthCapability>().RequireSome().CurrentHealth);
    Assert.Equal(1, battle.Events.EventsOf<ObjectDestroyedBattleEvent>().Length);
    Assert.Equal(0, battle.Events.EventsOf<ObjectDamagedBattleEvent>().Length);
    Assert.Equal(0, battle.Events.EventsOf<UnitStatusEffectAppliedBattleEvent>().Length);
  }
}
