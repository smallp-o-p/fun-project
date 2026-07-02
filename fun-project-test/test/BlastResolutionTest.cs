using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Core;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Items.Effects;
using GdUnit4;
using Godot;
using System.Linq;

[TestSuite]
[RequireGodotRuntime]
public partial class BlastResolutionTest
{
  private sealed record Fixture(
    BattleSession Session,
    BattleActionExecutor Executor,
    Faction PlayerFaction,
    Faction EnemyFaction,
    BattleTestUnit Thrower,
    BattleEventRecorder Recorder);

  private static Fixture MakeFixture()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(8, 1, 8), [playerFaction, enemyFaction]);
    var executor = new BattleActionExecutor(session);
    var thrower = SpawnUnit(session, BattleTestFactory.MakeCombatant("Thrower", playerFaction, actionPoints: 4), new Vector3I(1, 0, 1));
    var recorder = new BattleEventRecorder(session);
    return new Fixture(session, executor, playerFaction, enemyFaction, thrower, recorder);
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

    var grenade = MakeGrenade("Frag", throwRange: 6, blastRadius: 1, effects: [new DamageEffectData { BaseDamage = 8, Element = Element.Kinetic }]);
    Throw(fixture, grenade, new Vector3I(3, 0, 1));

    Assert.Equal(12, enemy.State.CurrentHealth);
    Assert.True(fixture.Recorder.OfType<UnitDamagedBattleEvent>().Any(e => ReferenceEquals(e.Unit, enemy.State)));
    Assert.True(fixture.Recorder.OfType<CapabilityResolvedBattleEvent>().Any());
  }

  [TestCase(TestName = "A unit outside the blast radius is unaffected")]
  public void UnitOutsideBlastRadiusIsUnaffected()
  {
    Fixture fixture = MakeFixture();
    var nearEnemy = SpawnUnit(fixture.Session, BattleTestFactory.MakeCombatant("Near", fixture.EnemyFaction, health: 20), new Vector3I(3, 0, 1));
    var farEnemy = SpawnUnit(fixture.Session, BattleTestFactory.MakeCombatant("Far", fixture.EnemyFaction, health: 20), new Vector3I(3, 0, 4));
    StartBattle(fixture.Session);

    var grenade = MakeGrenade("Frag", throwRange: 6, blastRadius: 1, effects: [new DamageEffectData { BaseDamage = 8, Element = Element.Kinetic }]);
    Throw(fixture, grenade, new Vector3I(3, 0, 1));

    Assert.Equal(12, nearEnemy.State.CurrentHealth);
    Assert.Equal(20, farEnemy.State.CurrentHealth);
    Assert.False(fixture.Recorder.OfType<UnitDamagedBattleEvent>().Any(e => ReferenceEquals(e.Unit, farEnemy.State)));
  }

  [TestCase(TestName = "Friendly-fire: an allied unit within the blast radius is also damaged")]
  public void FriendlyFireDamagesAlliedUnitWithinRadius()
  {
    Fixture fixture = MakeFixture();
    var ally = SpawnUnit(fixture.Session, BattleTestFactory.MakeCombatant("Ally", fixture.PlayerFaction, health: 20), new Vector3I(3, 0, 1));
    var enemy = SpawnUnit(fixture.Session, BattleTestFactory.MakeCombatant("Hostile", fixture.EnemyFaction, health: 20), new Vector3I(4, 0, 1));
    StartBattle(fixture.Session);

    var grenade = MakeGrenade("Frag", throwRange: 6, blastRadius: 1, effects: [new DamageEffectData { BaseDamage = 8, Element = Element.Kinetic }]);
    Throw(fixture, grenade, new Vector3I(4, 0, 1));

    // Target (4,0,1): the enemy (distance 0) and the allied unit (distance 1) are both in radius.
    Assert.Equal(12, enemy.State.CurrentHealth);
    Assert.Equal(12, ally.State.CurrentHealth);
    Assert.True(fixture.Recorder.OfType<UnitDamagedBattleEvent>().Any(e => ReferenceEquals(e.Unit, ally.State)));
  }

  [TestCase(TestName = "A status effect in the blast applies to an in-radius unit")]
  public void StatusEffectInBlastAppliesToInRadiusUnit()
  {
    Fixture fixture = MakeFixture();
    var enemy = SpawnUnit(fixture.Session, BattleTestFactory.MakeCombatant("Hostile", fixture.EnemyFaction, health: 20), new Vector3I(3, 0, 1));
    StartBattle(fixture.Session);

    var grenade = MakeGrenade(
      "Glue Bomb",
      throwRange: 6,
      blastRadius: 1,
      effects: [new ImmobilizeStatusSpecData { DurationTurns = 2, ApplyChancePercent = 100 }]);
    Throw(fixture, grenade, new Vector3I(3, 0, 1));

    Assert.True(enemy.State.IsImmobilized);
    Assert.True(fixture.Recorder.OfType<UnitStatusEffectAppliedBattleEvent>().Any(e => ReferenceEquals(e.Unit, enemy.State)));
  }
}
