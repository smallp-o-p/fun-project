using FunProject.Battle;
using FunProject.Core;
using FunProject.Weapons;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public partial class ArmorRegenTest
{
  [TestCase(TestName = "Regen waits out the delay then restores per owning-faction turn end")]
  public void RegenWaitsOutDelayThenRestores()
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
    battle.EndFactionTurn(battle.PlayerFaction);   // delay 1 -> 0
    Assert.Equal(0, armor.Capability.RegenDelayRemaining);
    Assert.Equal(5, armor.Capability.Current);

    battle.EndFactionTurn(battle.EnemyFaction);
    battle.EndFactionTurn(battle.PlayerFaction);   // regen 3: 5 -> 8
    Assert.Equal(8, armor.Capability.Current);

    battle.EndFactionTurn(battle.EnemyFaction);
    battle.EndFactionTurn(battle.PlayerFaction);   // regen clamped: 8 -> 10
    Assert.Equal(10, armor.Capability.Current);

    battle.ClearEvents();
    battle.EndFactionTurn(battle.EnemyFaction);
    battle.EndFactionTurn(battle.PlayerFaction);   // at max: no regen event
    Assert.False(battle.Events.EventsOf<UnitArmorRegeneratedBattleEvent>().AsValueEnumerable().Any());
  }

  [TestCase(TestName = "Enemy turn end does not tick the player's regen")]
  public void EnemyTurnEndDoesNotTickPlayerRegen()
  {
    var armor = TestData.MakeArmor("Recharger", armor: 10, element: Element.Thermal, regenDelayTurns: 2, regenPerTurn: 3);
    using var battle = BattleFixture.Duel(
      player: new("Alpha", Health: 30, Weapon: TestData.MakeWeapon("Rifle"), Armor: armor));
    battle.ApplyDamage(battle.PlayerUnit, 6);

    battle.EndFactionTurn(battle.PlayerFaction);   // delay 2 -> 1
    battle.EndFactionTurn(battle.EnemyFaction);    // must not tick

    Assert.Equal(1, armor.Capability.RegenDelayRemaining);
  }

  [TestCase(TestName = "Damage mid-countdown re-arms the delay")]
  public void DamageMidCountdownReArmsDelay()
  {
    var armor = TestData.MakeArmor("Recharger", armor: 10, element: Element.Thermal, regenDelayTurns: 2, regenPerTurn: 3);
    using var battle = BattleFixture.Duel(
      player: new("Alpha", Health: 30, Weapon: TestData.MakeWeapon("Rifle"), Armor: armor));
    battle.ApplyDamage(battle.PlayerUnit, 3);
    battle.EndFactionTurn(battle.PlayerFaction);   // delay 2 -> 1

    battle.ApplyDamage(battle.PlayerUnit, 1);

    Assert.Equal(2, armor.Capability.RegenDelayRemaining);
  }

  [TestCase]
  public void StunOnlyDamagePreservesTheExistingRegenDelay()
  {
    var armor = TestData.MakeArmor("Recharger", armor: 10, element: Element.Thermal, regenDelayTurns: 2, regenPerTurn: 3);
    using var battle = BattleFixture.Duel(
      player: new("Alpha", Health: 30, Weapon: TestData.MakeWeapon("Rifle"), Armor: armor));
    battle.ApplyDamage(battle.PlayerUnit, 3);
    battle.EndFactionTurn(battle.PlayerFaction);   // delay 2 -> 1
    Assert.Equal(1, armor.Capability.RegenDelayRemaining);

    battle.ApplyDamage(battle.PlayerUnit, 8, DamageKind.Stun);

    Assert.Equal(1, armor.Capability.RegenDelayRemaining);
  }

  [TestCase(TestName = "Armor without a regen rate never regenerates")]
  public void ArmorWithoutRegenRateNeverRegenerates()
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
  public void DeadUnitsAreNotTicked()
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
  public void RegenEventLandsAfterTurnEnded()
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

  [TestCase(TestName = "Health-only damage with depleted armor still re-arms the delay")]
  public void HealthOnlyDamageWithDepletedArmorReArmsDelay()
  {
    var armor = TestData.MakeArmor("Recharger", armor: 4, element: Element.Thermal, regenDelayTurns: 2, regenPerTurn: 3);
    using var battle = BattleFixture.Duel(
      player: new("Alpha", Health: 30, Weapon: TestData.MakeWeapon("Rifle"), Armor: armor));
    battle.ApplyDamage(battle.PlayerUnit, 4);
    Assert.Equal(0, armor.Capability.Current);

    battle.EndFactionTurn(battle.PlayerFaction);   // delay 2 -> 1
    Assert.Equal(1, armor.Capability.RegenDelayRemaining);

    // Armor is depleted: this damage is health-only, and must still reset the countdown.
    battle.ApplyDamage(battle.PlayerUnit, 2);

    Assert.Equal(2, armor.Capability.RegenDelayRemaining);
  }

  [TestCase(TestName = "A turn-end DoT tick re-arms the delay and suppresses that turn's regen")]
  public void DotTickSuppressesSameTurnRegen()
  {
    var armor = TestData.MakeArmor("Recharger", armor: 10, element: Element.Thermal, regenDelayTurns: 1, regenPerTurn: 3);
    using var battle = BattleFixture.Duel(
      player: new("Alpha", Health: 30, Weapon: TestData.MakeWeapon("Rifle"), Armor: armor));
    battle.ApplyDamage(battle.PlayerUnit, 5);
    Assert.Equal(5, armor.Capability.Current);

    battle.EndFactionTurn(battle.PlayerFaction);   // delay 1 -> 0: next player turn end would regen
    Assert.Equal(0, armor.Capability.RegenDelayRemaining);

    // Kinetic tick vs Thermal armor: 1x depletion, no elemental multiplier.
    battle.PlayerUnit.ApplyStatusEffect(
      TestData.MakeBurn(duration: 2, tickDamage: 2, tickElement: Element.Kinetic));
    battle.EndFactionTurn(battle.EnemyFaction);    // enemy turn end must not tick the player's DoT

    battle.ClearEvents();
    // Pins the StatusEffect -> ArmorRegen commit order: the DoT tick re-arms the delay
    // BEFORE the regen pass runs, so this turn end must not regenerate.
    battle.EndFactionTurn(battle.PlayerFaction);

    Assert.False(battle.Events.EventsOf<UnitArmorRegeneratedBattleEvent>().AsValueEnumerable().Any());
    Assert.Equal(3, armor.Capability.Current);               // 5 - 2 tick, absorbed by armor
    Assert.Equal(0, armor.Capability.RegenDelayRemaining);   // re-armed to 1, then counted down by the same regen pass
  }
}
