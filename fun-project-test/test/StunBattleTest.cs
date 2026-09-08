using System;
using FunProject.Battle;
using FunProject.Weapons;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public partial class StunBattleTest
{
  [TestCase]
  public void UnconsciousUnitsRetainTheirBoardOccupancy()
  {
    using var battle = BattleFixture.Duel();
    var point = battle.Alive(battle.EnemyUnit).Position;

    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);

    Assert.Equal(point, battle.Alive(battle.EnemyUnit).Position);
    Assert.False(battle.Board.CanOccupy(point));
  }

  [TestCase]
  public void OnlyNewTransitionsEmitUnconsciousOrKilledEvents()
  {
    using var battle = BattleFixture.Duel();
    battle.ClearEvents();
    battle.ApplyDamage(battle.EnemyUnit, 20, DamageKind.Stun);
    battle.ApplyDamage(battle.EnemyUnit, 5, DamageKind.Stun);
    battle.ApplyDamage(battle.EnemyUnit, 20);
    Assert.Equal(1, battle.Events.EventsOf<UnitUnconsciousBattleEvent>().Length);
    Assert.Equal(1, battle.Events.EventsOf<UnitKilledBattleEvent>().Length);
    battle.Events.EventBefore<UnitUnconsciousBattleEvent, UnitKilledBattleEvent>();
    Assert.Equal(5, battle.Events.SingleEvent<UnitDamagedBattleEvent>().StunDamage);
  }

  [TestCase(19, false)]
  [TestCase(20, true)]
  [TestCase(21, true)]
  public void StunThresholdUsesCurrentHealth(int stun, bool unconscious)
  {
    using var battle = BattleFixture.Duel();

    battle.ApplyDamage(battle.EnemyUnit, stun, DamageKind.Stun);

    Assert.Equal(unconscious, battle.EnemyUnit.IsUnconscious);
    Assert.Equal(!unconscious, battle.EnemyUnit.CanAct());
  }

  [TestCase]
  public void HealthDamageCanCrossTheStunThreshold()
  {
    using var battle = BattleFixture.Duel();
    battle.ApplyDamage(battle.EnemyUnit, 10, DamageKind.Stun);
    Assert.False(battle.EnemyUnit.IsUnconscious);
    battle.ClearEvents();

    battle.ApplyDamage(battle.EnemyUnit, 10);

    Assert.True(battle.EnemyUnit.IsUnconscious);
    Assert.Equal(battle.EnemyUnit, battle.Events.SingleEvent<UnitUnconsciousBattleEvent>().Unit);
  }

  [TestCase]
  public void LethalMixedAttackEmitsKilledOnly()
  {
    using var battle = BattleFixture.Duel(
      hitChanceCalculator: new AlwaysHitCalculator(),
      player: new("Alpha", Weapon: TestData.MakeWeapon("Mixed", damage: 20, frame: new WeaponFrameData
      {
        Packets = [new DamagePacketData(), new DamagePacketData { Kind = DamageKind.Stun }],
      })));
    battle.ClearEvents();

    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);

    Assert.Equal(battle.EnemyUnit, battle.Events.SingleEvent<UnitKilledBattleEvent>().Unit);
    Assert.Equal(0, battle.Events.EventsOf<UnitUnconsciousBattleEvent>().Length);
  }

  [TestCase]
  public void KnockoutEventCarriesTheUnitPositionAndAttacker()
  {
    using var battle = BattleFixture.Duel(
      hitChanceCalculator: new AlwaysHitCalculator(),
      player: new("Alpha", Weapon: TestData.MakeStunWeapon(damage: 20)));
    var position = battle.Alive(battle.EnemyUnit).Position;
    battle.ClearEvents();

    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);

    var unconscious = battle.Events.SingleEvent<UnitUnconsciousBattleEvent>();
    Assert.Equal(battle.EnemyUnit, unconscious.Unit);
    Assert.Equal(position, unconscious.Position);
    Assert.Equal(battle.PlayerUnit, unconscious.MaybeCause.RequireSome());
  }

  [TestCase]
  public void KilledAndUnconsciousEventsRejectNullUnit()
  {
    using var battle = BattleFixture.Duel();
    Assert.Throws<ArgumentNullException>(() => new UnitKilledBattleEvent(null, battle.At(0, 0, 0), None));
    Assert.Throws<ArgumentNullException>(() => new UnitUnconsciousBattleEvent(null, battle.At(0, 0, 0), None));
  }

  [TestCase]
  public void StunAttacksBypassArmorAndHealth()
  {
    var armor = TestData.MakeArmor("Plating", armor: 10);
    using var battle = BattleFixture.Duel(
      hitChanceCalculator: new AlwaysHitCalculator(),
      player: new("Alpha", Weapon: TestData.MakeStunWeapon(damage: 8)),
      enemy: new("Hostile", Armor: armor));

    battle.Attack(battle.PlayerUnit, battle.EnemyUnit);

    Assert.Equal(10, armor.Capability.Current);
    Assert.Equal(20, battle.EnemyUnit.CurrentHealth);
    Assert.Equal(8, battle.EnemyUnit.CurrentStun);
  }

  [TestCase]
  public void KnockingOutAnAvailableUnitConsumesItsActivation()
  {
    using var battle = BattleFixture.Duel();
    Assert.True(battle.Query(new IsUnitStillAvailableThisTurn(battle.PlayerUnit)));

    battle.ApplyDamage(battle.PlayerUnit, 20, DamageKind.Stun);

    Assert.False(battle.Query(new IsUnitStillAvailableThisTurn(battle.PlayerUnit)));
  }

  [TestCase]
  public void StunRejectsNegativeAmountsAndOverflowWithoutWrapping()
  {
    using var battle = BattleFixture.Duel();
    Assert.Throws<ArgumentOutOfRangeException>(() => battle.EnemyUnit.ReceiveStun(-1));
    Assert.Equal(0, battle.EnemyUnit.CurrentStun);
    battle.EnemyUnit.ReceiveStun(int.MaxValue);
    Assert.Throws<OverflowException>(() => battle.EnemyUnit.ReceiveStun(1));
    Assert.Equal(int.MaxValue, battle.EnemyUnit.CurrentStun);
  }
}
