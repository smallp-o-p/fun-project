using FunProject.Battle;
using FunProject.Core;
using FunProject.Weapons;
using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public partial class ArmorRegenTest
{
  [TestCase(TestName = "Regen waits out the delay then restores per owning-faction turn end")]
  public static void RegenWaitsOutDelayThenRestores()
  {
    // Thermal armor vs ApplyDamage's Kinetic packets: depletion stays at 1x.
    var armor = TestData.MakeArmor("Recharger", armor: 10, element: Element.Thermal, regenDelayTurns: 2, regenPerTurn: 3);
    using var battle = BattleFixture.Duel(
      player: new("Alpha", Health: 30, Weapon: TestData.MakeWeapon("Rifle"), Armor: armor));
    battle.ApplyDamage(battle.PlayerUnit, 5);
    Assert.Equal(5, armor.Capability.Current);
    Assert.Equal(2, armor.Capability.RegenDelayRemaining);

    battle.EndFactionTurn(battle.PlayerFaction);   // delay 2 -> 1
    Assert.Equal(1, armor.Capability.RegenDelayRemaining);
    Assert.Equal(5, armor.Capability.Current);

    battle.EndFactionTurn(battle.EnemyFaction);
    Assert.Equal(1, armor.Capability.RegenDelayRemaining);
    battle.EndFactionTurn(battle.PlayerFaction);   // delay 1 -> 0
    Assert.Equal(0, armor.Capability.RegenDelayRemaining);
    Assert.Equal(5, armor.Capability.Current);

    battle.EndFactionTurn(battle.EnemyFaction);
    battle.EndFactionTurn(battle.PlayerFaction);   // regen 3: 5 -> 8
    Assert.Equal(8, armor.Capability.Current);

    battle.ClearEvents();
    battle.EndFactionTurn(battle.EnemyFaction);
    battle.EndFactionTurn(battle.PlayerFaction);   // regen clamped: 8 -> 10
    Assert.Equal(10, armor.Capability.Current);
    var cappedRegen = battle.Events.SingleEvent<UnitArmorRegeneratedBattleEvent>();
    Assert.Equal(2, cappedRegen.AmountRegenerated);
    Assert.Equal(10, cappedRegen.CurrentArmor);

    battle.ClearEvents();
    battle.EndFactionTurn(battle.EnemyFaction);
    battle.EndFactionTurn(battle.PlayerFaction);   // at max: no regen event
    Assert.False(battle.Events.EventsOf<UnitArmorRegeneratedBattleEvent>().AsValueEnumerable().Any());
  }

  [TestCase(TestName = "Damage re-arms the regen delay: mid-countdown, stun-only, and health-only over depleted armor")]
  public static void DamageRearmRows()
  {
    (string Name, int ArmorMax, int InitialDamage, int? CurrentAfterInitial, int? DelayAfterTurn,
      int NextDamage, DamageKind NextKind, int FinalDelay)[] rows =
    [
      ("damage mid-countdown re-arms the delay", 10, 3, null, null, 1, DamageKind.Health, 2),
      ("stun-only damage preserves the existing regen delay", 10, 3, null, 1, 8, DamageKind.Stun, 1),
      // Armor is depleted: this damage is health-only, and must still reset the countdown.
      ("health-only damage with depleted armor re-arms the delay", 4, 4, 0, 1, 2, DamageKind.Health, 2),
    ];

    foreach (var row in rows)
    {
      var armor = TestData.MakeArmor("Recharger", armor: row.ArmorMax, element: Element.Thermal,
        regenDelayTurns: 2, regenPerTurn: 3);
      using var battle = BattleFixture.Duel(
        player: new("Alpha", Health: 30, Weapon: TestData.MakeWeapon("Rifle"), Armor: armor));
      battle.ApplyDamage(battle.PlayerUnit, row.InitialDamage);
      if (row.CurrentAfterInitial is int current)
        Assert.Equal(current, armor.Capability.Current, row.Name);

      battle.EndFactionTurn(battle.PlayerFaction);   // delay 2 -> 1
      if (row.DelayAfterTurn is int delay)
        Assert.Equal(delay, armor.Capability.RegenDelayRemaining, row.Name);

      battle.ApplyDamage(battle.PlayerUnit, row.NextDamage, row.NextKind);

      Assert.Equal(row.FinalDelay, armor.Capability.RegenDelayRemaining, row.Name);
    }
  }

  [TestCase(TestName = "Armor without a regen rate never regenerates")]
  public static void ArmorWithoutRegenRateNeverRegenerates()
  {
    var armor = TestData.MakeArmor("Recharger", armor: 10, element: Element.Thermal, regenDelayTurns: 0, regenPerTurn: 0);
    using var battle = BattleFixture.Duel(
      player: new("Alpha", Health: 30, Weapon: TestData.MakeWeapon("Rifle"), Armor: armor));
    battle.ApplyDamage(battle.PlayerUnit, 6);

    battle.ClearEvents();
    battle.EndFactionTurn(battle.PlayerFaction);
    battle.EndFactionTurn(battle.EnemyFaction);
    battle.EndFactionTurn(battle.PlayerFaction);

    Assert.Equal(4, armor.Capability.Current);
    Assert.False(battle.Events.EventsOf<UnitArmorRegeneratedBattleEvent>().AsValueEnumerable().Any());
  }

  [TestCase(TestName = "Dead units are not ticked")]
  public static void DeadUnitsAreNotTicked()
  {
    var armor = TestData.MakeArmor("Recharger", armor: 5, element: Element.Thermal, regenDelayTurns: 0, regenPerTurn: 5);
    using var battle = BattleFixture.Duel(
      player: new("Alpha", Health: 30, Weapon: TestData.MakeWeapon("Rifle"), Armor: armor));
    battle.ApplyDamage(battle.PlayerUnit, 50);
    Assert.True(battle.PlayerUnit.IsDead);

    battle.ClearEvents();
    // The player is still the active side (death does not auto-advance the turn);
    // ending the player's own turn is also the tick that would regen this unit.
    battle.EndFactionTurn(battle.PlayerFaction);

    Assert.False(battle.Events.EventsOf<UnitArmorRegeneratedBattleEvent>().AsValueEnumerable().Any());
  }

  [TestCase(TestName = "Regen event lands between turn-ended and the next turn-started")]
  public static void RegenEventLandsAfterTurnEnded()
  {
    var armor = TestData.MakeArmor("Recharger", armor: 10, element: Element.Thermal, regenDelayTurns: 0, regenPerTurn: 3);
    using var battle = BattleFixture.Duel(
      player: new("Alpha", Health: 30, Weapon: TestData.MakeWeapon("Rifle"), Armor: armor));
    battle.ApplyDamage(battle.PlayerUnit, 6);

    battle.ClearEvents();
    battle.EndFactionTurn(battle.PlayerFaction);

    var regenEvent = battle.Events.SingleEvent<UnitArmorRegeneratedBattleEvent>();
    Assert.Equal(3, regenEvent.AmountRegenerated);
    Assert.Equal(7, regenEvent.CurrentArmor);
    battle.Events.EventBefore<TurnEndedBattleEvent, UnitArmorRegeneratedBattleEvent>();
    battle.Events.EventBefore<UnitArmorRegeneratedBattleEvent, TurnStartedBattleEvent>();
  }

}
