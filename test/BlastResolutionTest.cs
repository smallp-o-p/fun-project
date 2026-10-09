using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Core;
using FunProject.Items.Effects;
using GdUnit4;
using Godot;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public partial class BlastResolutionTest
{
  // Factory-ready fixture on the shared 8x1x8 blast board: domain tuples converted to
  // placements, then built through the shared Started scenario (factions in first-appearance
  // order, one inert objective per faction).
  private static BattleFixture BlastFixture(
    params (Faction Faction, string UnitName, Vector3I Position, int Health)[] units) =>
    BattleFixture.Started(new Vector3I(8, 1, 8),
      [.. units.AsValueEnumerable().Select(u => new UnitPlacement(
        new UnitLoadout(TestData.MakeCombatant(u.UnitName, u.Faction, health: u.Health)),
        u.Position))]);

  [TestCase(TestName = "A unit outside the blast radius is unaffected")]
  public static void UnitOutsideBlastRadiusIsUnaffected()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = BlastFixture(
      (playerFaction, "Thrower", new Vector3I(1, 0, 1), 20),
      (enemyFaction, "Near", new Vector3I(3, 0, 1), 20),
      (enemyFaction, "Far", new Vector3I(3, 0, 4), 20));
    var thrower = battle.UnitAt(new Vector3I(1, 0, 1));
    var nearEnemy = battle.UnitAt(new Vector3I(3, 0, 1));
    var farEnemy = battle.UnitAt(new Vector3I(3, 0, 4));

    var grenade = TestData.MakeGrenade("Frag", throwRange: 6, blastRadius: 1, effects: [new DamageEffectData { BaseDamage = 8, Element = Element.Kinetic }]);
    thrower.AddInventoryItem(grenade.Item);
    battle.ClearEvents();
    battle.Throw(thrower, grenade, new Vector3I(3, 0, 1));

    Assert.Equal(12, nearEnemy.CurrentHealth);
    Assert.True(battle.Events.EventsOf<UnitDamagedBattleEvent>().AsValueEnumerable().Any(e => ReferenceEquals(e.Unit, nearEnemy)));
    Assert.True(battle.Events.EventsOf<CapabilityResolvedBattleEvent>().AsValueEnumerable().Any());
    Assert.Equal(20, farEnemy.CurrentHealth);
    Assert.False(battle.Events.EventsOf<UnitDamagedBattleEvent>().AsValueEnumerable().Any(e => ReferenceEquals(e.Unit, farEnemy)));
  }

  [TestCase(TestName = "Friendly-fire: an allied unit within the blast radius is also damaged")]
  public static void FriendlyFireDamagesAlliedUnitWithinRadius()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = BlastFixture(
      (playerFaction, "Thrower", new Vector3I(1, 0, 1), 20),
      (playerFaction, "Ally", new Vector3I(3, 0, 1), 20),
      (enemyFaction, "Hostile", new Vector3I(4, 0, 1), 20));
    var thrower = battle.UnitAt(new Vector3I(1, 0, 1));
    var ally = battle.UnitAt(new Vector3I(3, 0, 1));
    var enemy = battle.UnitAt(new Vector3I(4, 0, 1));

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
  public static void StatusEffectInBlastAppliesToInRadiusUnit()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = BlastFixture(
      (playerFaction, "Thrower", new Vector3I(1, 0, 1), 20),
      (enemyFaction, "Hostile", new Vector3I(3, 0, 1), 20));
    var thrower = battle.UnitAt(new Vector3I(1, 0, 1));
    var enemy = battle.UnitAt(new Vector3I(3, 0, 1));

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

  [TestCase]
  public static void BlastDamagesObjectsAndUnitsAndSkipsIneligibleObjects()
  {
    var origin = new Vector3I(3, 0, 4);
    using var battle = BattleFixture.Duel(
      player: new("Shooter", Weapon: TestData.MakeWeapon("Rifle", damage: 5)),
      configure: setup => setup with
      {
        Objects =
        [
          new ObjectPlacement(TestData.MakeObject("Near", 20), origin),
          new ObjectPlacement(TestData.MakeObject("Far", 20), new Vector3I(0, 0, 7)),
          new ObjectPlacement(TestData.MakeObject(), new Vector3I(2, 0, 4)),
          new ObjectPlacement(TestData.MakeObject("Resolved", 20, new InteractiveCapabilityData()), new Vector3I(3, 0, 3)),
          new ObjectPlacement(TestData.MakeObject("Destroyed", 5), new Vector3I(3, 0, 5)),
        ],
      });
    var objects = battle.Query(new GetBattleSpecialObjectsQuery());
    var near = objects.AsValueEnumerable().Single(o => string.Equals(o.Name, "Near", System.StringComparison.Ordinal));
    var far = objects.AsValueEnumerable().Single(o => string.Equals(o.Name, "Far", System.StringComparison.Ordinal));
    var scenery = objects.AsValueEnumerable().Single(o => o.Position == new Vector3I(2, 0, 4));
    var resolved = objects.AsValueEnumerable().Single(o => string.Equals(o.Name, "Resolved", System.StringComparison.Ordinal));
    var destroyed = objects.AsValueEnumerable().Single(o => string.Equals(o.Name, "Destroyed", System.StringComparison.Ordinal));
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
  public static void RadiusZeroMultipleEffectsAndStatusesDoNotRepeatDestruction()
  {
    var origin = new Vector3I(3, 0, 2);
    using var battle = BattleFixture.Duel(configure: setup => setup with
    {
      Objects =
      [
        new ObjectPlacement(TestData.MakeObject("Crate", 5), origin),
        new ObjectPlacement(TestData.MakeObject("Neighbor", 20), new Vector3I(2, 0, 2)),
      ],
    });
    var objects = battle.Query(new GetBattleSpecialObjectsQuery());
    var obj = objects.AsValueEnumerable().Single(o => o.Position == origin);
    var neighbor = objects.AsValueEnumerable().Single(o => string.Equals(o.Name, "Neighbor", System.StringComparison.Ordinal));
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
