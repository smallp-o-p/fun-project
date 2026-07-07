using FunProject.Battle;
using FunProject.Core;
using FunProject.Items;
using FunProject.Items.Capabilities;
using GdUnit4;
using System.Linq;

[TestSuite]
[RequireGodotRuntime]
public partial class ArmorRegenTest
{
  private static (TwoFactionBattle Battle, ItemWith<ArmorCapability> Armor) MakeFixture(
    int armor = 10, int regenDelayTurns = 2, int regenPerTurn = 3)
  {
    // Thermal armor vs ApplyDamage's Kinetic packets: depletion stays at 1x.
    var armorItem = BattleTestFactory.MakeArmor("Recharger", armor: armor, element: Element.Thermal, regenDelayTurns: regenDelayTurns, regenPerTurn: regenPerTurn);
    var battle = new BattleDuelBuilder
    {
      Player = new DuelSide("Alpha", Health: 30, Weapon: BattleTestFactory.MakeWeapon("Rifle"), Armor: armorItem),
    }.Start();
    return (battle, armorItem);
  }

  [TestCase(TestName = "Regen waits out the delay then restores per owning-faction turn end")]
  public void RegenWaitsOutDelayThenRestores()
  {
    var (battle, armor) = MakeFixture(armor: 10, regenDelayTurns: 2, regenPerTurn: 3);
    battle.Executor.Submit(BattleAction.ApplyDamage(battle.PlayerUnit, 5)).RequireSingleResult();
    Assert.Equal(5, armor.Capability.Current);
    Assert.Equal(2, armor.Capability.RegenDelayRemaining);

    EndFactionTurn(battle.Executor, battle.PlayerFaction);   // delay 2 -> 1
    Assert.Equal(1, armor.Capability.RegenDelayRemaining);
    Assert.Equal(5, armor.Capability.Current);

    EndFactionTurn(battle.Executor, battle.EnemyFaction);
    EndFactionTurn(battle.Executor, battle.PlayerFaction);   // delay 1 -> 0
    Assert.Equal(0, armor.Capability.RegenDelayRemaining);
    Assert.Equal(5, armor.Capability.Current);

    EndFactionTurn(battle.Executor, battle.EnemyFaction);
    EndFactionTurn(battle.Executor, battle.PlayerFaction);   // regen 3: 5 -> 8
    Assert.Equal(8, armor.Capability.Current);

    EndFactionTurn(battle.Executor, battle.EnemyFaction);
    EndFactionTurn(battle.Executor, battle.PlayerFaction);   // regen clamped: 8 -> 10
    Assert.Equal(10, armor.Capability.Current);

    var recorder = new BattleEventRecorder(battle.Session);
    EndFactionTurn(battle.Executor, battle.EnemyFaction);
    EndFactionTurn(battle.Executor, battle.PlayerFaction);   // at max: no regen event
    Assert.False(recorder.OfType<UnitArmorRegeneratedBattleEvent>().Any());
  }

  [TestCase(TestName = "Enemy turn end does not tick the player's regen")]
  public void EnemyTurnEndDoesNotTickPlayerRegen()
  {
    var (battle, armor) = MakeFixture(regenDelayTurns: 2);
    battle.Executor.Submit(BattleAction.ApplyDamage(battle.PlayerUnit, 6)).RequireSingleResult();

    EndFactionTurn(battle.Executor, battle.PlayerFaction);   // delay 2 -> 1
    EndFactionTurn(battle.Executor, battle.EnemyFaction);    // must not tick

    Assert.Equal(1, armor.Capability.RegenDelayRemaining);
  }

  [TestCase(TestName = "Damage mid-countdown re-arms the delay")]
  public void DamageMidCountdownReArmsDelay()
  {
    var (battle, armor) = MakeFixture(regenDelayTurns: 2);
    battle.Executor.Submit(BattleAction.ApplyDamage(battle.PlayerUnit, 3)).RequireSingleResult();
    EndFactionTurn(battle.Executor, battle.PlayerFaction);   // delay 2 -> 1

    battle.Executor.Submit(BattleAction.ApplyDamage(battle.PlayerUnit, 1)).RequireSingleResult();

    Assert.Equal(2, armor.Capability.RegenDelayRemaining);
  }

  [TestCase(TestName = "Armor without a regen rate never regenerates")]
  public void ArmorWithoutRegenRateNeverRegenerates()
  {
    var (battle, armor) = MakeFixture(armor: 10, regenDelayTurns: 0, regenPerTurn: 0);
    battle.Executor.Submit(BattleAction.ApplyDamage(battle.PlayerUnit, 6)).RequireSingleResult();

    var recorder = new BattleEventRecorder(battle.Session);
    EndFactionTurn(battle.Executor, battle.PlayerFaction);
    EndFactionTurn(battle.Executor, battle.EnemyFaction);
    EndFactionTurn(battle.Executor, battle.PlayerFaction);

    Assert.Equal(4, armor.Capability.Current);
    Assert.False(recorder.OfType<UnitArmorRegeneratedBattleEvent>().Any());
  }

  [TestCase(TestName = "Dead units are not ticked")]
  public void DeadUnitsAreNotTicked()
  {
    var (battle, _) = MakeFixture(armor: 5, regenDelayTurns: 0, regenPerTurn: 5);
    battle.Executor.Submit(BattleAction.ApplyDamage(battle.PlayerUnit, 50)).RequireSingleResult();
    Assert.True(battle.PlayerUnit.State.IsDead);

    var recorder = new BattleEventRecorder(battle.Session);
    // The player is still the active side (death does not auto-advance the turn);
    // ending the player's own turn is also the tick that would regen this unit.
    EndFactionTurn(battle.Executor, battle.PlayerFaction);

    Assert.False(recorder.OfType<UnitArmorRegeneratedBattleEvent>().Any());
  }

  [TestCase(TestName = "Regen event lands between turn-ended and the next turn-started")]
  public void RegenEventLandsAfterTurnEnded()
  {
    var (battle, _) = MakeFixture(armor: 10, regenDelayTurns: 0, regenPerTurn: 3);
    battle.Executor.Submit(BattleAction.ApplyDamage(battle.PlayerUnit, 6)).RequireSingleResult();

    var recorder = new BattleEventRecorder(battle.Session);
    EndFactionTurn(battle.Executor, battle.PlayerFaction);

    var regenEvent = recorder.Single<UnitArmorRegeneratedBattleEvent>();
    Assert.Equal(3, regenEvent.AmountRegenerated);
    Assert.Equal(7, regenEvent.CurrentArmor);
    recorder.AssertCommittedBefore<TurnEndedBattleEvent, UnitArmorRegeneratedBattleEvent>();
    recorder.AssertCommittedBefore<UnitArmorRegeneratedBattleEvent, TurnStartedBattleEvent>();
  }

  [TestCase(TestName = "Health-only damage with depleted armor still re-arms the delay")]
  public void HealthOnlyDamageWithDepletedArmorReArmsDelay()
  {
    var (battle, armor) = MakeFixture(armor: 4, regenDelayTurns: 2, regenPerTurn: 3);
    battle.Executor.Submit(BattleAction.ApplyDamage(battle.PlayerUnit, 4)).RequireSingleResult();
    Assert.Equal(0, armor.Capability.Current);

    EndFactionTurn(battle.Executor, battle.PlayerFaction);   // delay 2 -> 1
    Assert.Equal(1, armor.Capability.RegenDelayRemaining);

    // Armor is depleted: this damage is health-only, and must still reset the countdown.
    battle.Executor.Submit(BattleAction.ApplyDamage(battle.PlayerUnit, 2)).RequireSingleResult();

    Assert.Equal(2, armor.Capability.RegenDelayRemaining);
  }

  [TestCase(TestName = "A turn-end DoT tick re-arms the delay and suppresses that turn's regen")]
  public void DotTickSuppressesSameTurnRegen()
  {
    var (battle, armor) = MakeFixture(armor: 10, regenDelayTurns: 1, regenPerTurn: 3);
    battle.Executor.Submit(BattleAction.ApplyDamage(battle.PlayerUnit, 5)).RequireSingleResult();
    Assert.Equal(5, armor.Capability.Current);

    EndFactionTurn(battle.Executor, battle.PlayerFaction);   // delay 1 -> 0: next player turn end would regen
    Assert.Equal(0, armor.Capability.RegenDelayRemaining);

    // Kinetic tick vs Thermal armor: 1x depletion, no elemental multiplier.
    battle.PlayerUnit.State.ApplyStatusEffect(
      BattleTestFactory.MakeBurn(duration: 2, tickDamage: 2, tickElement: Element.Kinetic));
    EndFactionTurn(battle.Executor, battle.EnemyFaction);    // enemy turn end must not tick the player's DoT

    var recorder = new BattleEventRecorder(battle.Session);
    // Pins the StatusEffect -> ArmorRegen commit order: the DoT tick re-arms the delay
    // BEFORE the regen pass runs, so this turn end must not regenerate.
    EndFactionTurn(battle.Executor, battle.PlayerFaction);

    Assert.False(recorder.OfType<UnitArmorRegeneratedBattleEvent>().Any());
    Assert.Equal(3, armor.Capability.Current);               // 5 - 2 tick, absorbed by armor
    Assert.Equal(0, armor.Capability.RegenDelayRemaining);   // re-armed to 1, then counted down by the same regen pass
  }
}
