using FunProject.Battle;
using GdUnit4;
using Godot;
using System.Linq;

[TestSuite]
[RequireGodotRuntime]
public class BattleCausalityTest
{
  [TestCase(TestName = "An attack hit carries the attacker as the damage event's cause")]
  public void AttackHitCarriesAttackerAsDamageCause()
  {
    var battle = new BattleDuelBuilder
    {
      HitChanceCalculator = new AlwaysHitCalculator(),
      Player = new("Alpha", Weapon: BattleTestFactory.MakeWeapon("Rifle", damage: 5)),
      Enemy = new("Hostile"),
    }.Start();
    var recorder = new BattleEventRecorder(battle.Session);

    Attack(battle.Executor, battle.PlayerUnit, battle.EnemyUnit);

    var damagedEvent = recorder.OfType<UnitDamagedBattleEvent>().Single();
    Assert.Equal(battle.PlayerUnit.State, damagedEvent.MaybeCause.RequireSome());
  }

  [TestCase(TestName = "A killing blow carries the attacker on both the damage and kill events")]
  public void KillingBlowCarriesAttackerOnDamageAndKillEvents()
  {
    var battle = new BattleDuelBuilder
    {
      HitChanceCalculator = new AlwaysHitCalculator(),
      Player = new("Alpha", Weapon: BattleTestFactory.MakeWeapon("Rifle", damage: 5)),
      Enemy = new("Hostile", Health: 5),
    }.Start();
    var recorder = new BattleEventRecorder(battle.Session);

    Attack(battle.Executor, battle.PlayerUnit, battle.EnemyUnit);

    Assert.True(battle.EnemyUnit.State.IsDead);
    Assert.Equal(battle.PlayerUnit.State, recorder.OfType<UnitDamagedBattleEvent>().Single().MaybeCause.RequireSome());
    var killedEvent = recorder.OfType<UnitKilledBattleEvent>().Single();
    Assert.Equal(battle.EnemyUnit.State, killedEvent.Unit);
    Assert.Equal(battle.PlayerUnit.State, killedEvent.MaybeCause.RequireSome());
  }

  [TestCase(TestName = "Direct damage without an attacker leaves the cause empty on damage and kill events")]
  public void DirectDamageLeavesCauseEmpty()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, health: 20), new Vector3I(0, 0, 0));
    StartBattle(session);

    var recorder = new BattleEventRecorder(session);

    ApplyDamage(session, unit, 3);
    ApplyDamage(session, unit, 999);

    Assert.True(recorder.OfType<UnitDamagedBattleEvent>().All(damagedEvent => damagedEvent.MaybeCause.IsNone));
    Assert.True(recorder.OfType<UnitKilledBattleEvent>().Single().MaybeCause.IsNone);
  }
}
