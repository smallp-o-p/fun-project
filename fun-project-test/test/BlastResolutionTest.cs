using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Core;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Items.Effects;
using FunProject.Tests;
using GdUnit4;
using Godot;
using System.Collections.Generic;
using System.Linq;
using static BattleActionTestHelper;

[TestSuite]
[RequireGodotRuntime]
public partial class BlastResolutionTest
{
  private static ItemWith<ThrowableCapability> MakeBlastGrenade(
    string name,
    int blastRadius,
    int throwRange,
    params BattleEffectData[] effects)
  {
    var blastEffects = new Godot.Collections.Array<BattleEffectData>();
    foreach (BattleEffectData effect in effects)
      blastEffects.Add(effect);

    var item = new EquippableItem(new EquippableItemData
    {
      Name = name,
      Description = $"{name} grenade",
      Capabilities =
      [
        new ThrowableCapabilityData { ThrowRange = throwRange, ActionPointCost = 1, ConsumesOnUse = true },
        new BlastCapabilityData { BlastRadius = blastRadius, Effects = blastEffects },
      ],
    });
    return item.With<ThrowableCapability>().RequireSome();
  }

  private sealed record Fixture(
    BattleSession Session,
    BattleActionExecutor Executor,
    Faction PlayerFaction,
    Faction EnemyFaction,
    BattleTestUnit Thrower,
    List<BattleEvent> Events);

  private static Fixture MakeFixture()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(8, 1, 8), [playerFaction, enemyFaction]);
    var executor = new BattleActionExecutor(session);
    var thrower = SpawnUnit(session, BattleTestFactory.MakeCombatant("Thrower", playerFaction, actionPoints: 4), new Vector3I(1, 0, 1));
    var events = new List<BattleEvent>();
    session.BattleEventCommitted += events.Add;
    return new Fixture(session, executor, playerFaction, enemyFaction, thrower, events);
  }

  private static void Throw(Fixture fixture, ItemWith<ThrowableCapability> grenade, Vector3I target)
  {
    fixture.Thrower.AddInventoryItem(grenade.Item);
    var result = fixture.Executor.Submit(BattleAction.ThrowItem(fixture.Thrower.State, grenade, target)).RequireSingleResult();
    Assert.True(result.Succeeded);
  }

  [TestCase(TestName = "Thrown grenade damages an enemy within the blast radius")]
  public void ThrownGrenadeDamagesEnemyWithinBlastRadius()
  {
    Fixture fixture = MakeFixture();
    var enemy = SpawnUnit(fixture.Session, BattleTestFactory.MakeCombatant("Hostile", fixture.EnemyFaction, health: 20), new Vector3I(3, 0, 1));
    StartBattle(fixture.Session);

    var grenade = MakeBlastGrenade("Frag", blastRadius: 1, throwRange: 6, new DamageEffectData { BaseDamage = 8, Element = Element.Kinetic });
    Throw(fixture, grenade, new Vector3I(3, 0, 1));

    Assert.Equal(12, enemy.State.CurrentHealth);
    Assert.True(fixture.Events.OfType<UnitDamagedBattleEvent>().Any(e => ReferenceEquals(e.Unit, enemy.State)));
    Assert.True(fixture.Events.OfType<CapabilityResolvedBattleEvent>().Any());
  }

  [TestCase(TestName = "A unit outside the blast radius is unaffected")]
  public void UnitOutsideBlastRadiusIsUnaffected()
  {
    Fixture fixture = MakeFixture();
    var nearEnemy = SpawnUnit(fixture.Session, BattleTestFactory.MakeCombatant("Near", fixture.EnemyFaction, health: 20), new Vector3I(3, 0, 1));
    var farEnemy = SpawnUnit(fixture.Session, BattleTestFactory.MakeCombatant("Far", fixture.EnemyFaction, health: 20), new Vector3I(3, 0, 4));
    StartBattle(fixture.Session);

    var grenade = MakeBlastGrenade("Frag", blastRadius: 1, throwRange: 6, new DamageEffectData { BaseDamage = 8, Element = Element.Kinetic });
    Throw(fixture, grenade, new Vector3I(3, 0, 1));

    Assert.Equal(12, nearEnemy.State.CurrentHealth);
    Assert.Equal(20, farEnemy.State.CurrentHealth);
    Assert.False(fixture.Events.OfType<UnitDamagedBattleEvent>().Any(e => ReferenceEquals(e.Unit, farEnemy.State)));
  }

  [TestCase(TestName = "Friendly-fire: an allied unit within the blast radius is also damaged")]
  public void FriendlyFireDamagesAlliedUnitWithinRadius()
  {
    Fixture fixture = MakeFixture();
    var ally = SpawnUnit(fixture.Session, BattleTestFactory.MakeCombatant("Ally", fixture.PlayerFaction, health: 20), new Vector3I(3, 0, 1));
    var enemy = SpawnUnit(fixture.Session, BattleTestFactory.MakeCombatant("Hostile", fixture.EnemyFaction, health: 20), new Vector3I(4, 0, 1));
    StartBattle(fixture.Session);

    var grenade = MakeBlastGrenade("Frag", blastRadius: 1, throwRange: 6, new DamageEffectData { BaseDamage = 8, Element = Element.Kinetic });
    Throw(fixture, grenade, new Vector3I(4, 0, 1));

    // Target (4,0,1): the enemy (distance 0) and the allied unit (distance 1) are both in radius.
    Assert.Equal(12, enemy.State.CurrentHealth);
    Assert.Equal(12, ally.State.CurrentHealth);
    Assert.True(fixture.Events.OfType<UnitDamagedBattleEvent>().Any(e => ReferenceEquals(e.Unit, ally.State)));
  }

  [TestCase(TestName = "A status effect in the blast applies to an in-radius unit")]
  public void StatusEffectInBlastAppliesToInRadiusUnit()
  {
    Fixture fixture = MakeFixture();
    var enemy = SpawnUnit(fixture.Session, BattleTestFactory.MakeCombatant("Hostile", fixture.EnemyFaction, health: 20), new Vector3I(3, 0, 1));
    StartBattle(fixture.Session);

    var grenade = MakeBlastGrenade(
      "Glue Bomb",
      blastRadius: 1,
      throwRange: 6,
      new ImmobilizeStatusSpecData { DurationTurns = 2, ApplyChancePercent = 100 });
    Throw(fixture, grenade, new Vector3I(3, 0, 1));

    Assert.True(enemy.State.IsImmobilized);
    Assert.True(fixture.Events.OfType<UnitStatusEffectAppliedBattleEvent>().Any(e => ReferenceEquals(e.Unit, enemy.State)));
  }
}
