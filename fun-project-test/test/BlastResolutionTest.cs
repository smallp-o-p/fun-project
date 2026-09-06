using FunProject.Battle;
using FunProject.Core;
using FunProject.Items.Effects;
using GdUnit4;
using Godot;

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
}
