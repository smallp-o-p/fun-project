using FunProject.Battle;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public class BattleCausalityTest
{
  [TestCase(TestName = "An attack hit carries the attacker as the damage event's cause")]
  public void AttackHitCarriesAttackerAsDamageCause()
  {
    using var battle = BattleFixture.Duel(
      hitChanceCalculator: new AlwaysHitCalculator(),
      player: new("Alpha", Weapon: TestData.MakeWeapon("Rifle", damage: 5)),
      enemy: new("Hostile"));
    battle.ClearEvents();

    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);

    var damagedEvent = battle.Events.SingleEvent<UnitDamagedBattleEvent>();
    Assert.Equal(battle.PlayerUnit, damagedEvent.MaybeCause.RequireSome());
  }

  [TestCase(TestName = "A killing blow carries the attacker on the kill event alone")]
  public void KillingBlowCarriesAttackerOnTheKillEventAlone()
  {
    using var battle = BattleFixture.Duel(
      hitChanceCalculator: new AlwaysHitCalculator(),
      player: new("Alpha", Weapon: TestData.MakeWeapon("Rifle", damage: 5)),
      enemy: new("Hostile", Health: 5));
    battle.ClearEvents();

    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);

    Assert.True(battle.EnemyUnit.IsDead);
    // A killing blow commits death only: no damage event accompanies the kill.
    Assert.False(battle.Events.EventsOf<UnitDamagedBattleEvent>().AsValueEnumerable().Any());
    var killedEvent = battle.Events.SingleEvent<UnitKilledBattleEvent>();
    Assert.Equal(battle.EnemyUnit, killedEvent.Unit);
    Assert.Equal(battle.PlayerUnit, killedEvent.MaybeCause.RequireSome());
  }

  [TestCase(TestName = "Direct damage without an attacker leaves the cause empty on damage and kill events")]
  public void DirectDamageLeavesCauseEmpty()
  {
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), new Vector3I(0, 0, 0), health: 20);
    battle.ClearEvents();

    battle.ApplyDamage(battle.Unit, 3);
    battle.ApplyDamage(battle.Unit, 999);

    Assert.True(battle.Events.EventsOf<UnitDamagedBattleEvent>().AsValueEnumerable().All(damagedEvent => damagedEvent.MaybeCause.IsNone));
    Assert.True(battle.Events.EventsOf<UnitKilledBattleEvent>().AsValueEnumerable().Single().MaybeCause.IsNone);
  }
}
