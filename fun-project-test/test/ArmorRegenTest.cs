using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Core;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Tests;
using GdUnit4;
using Godot;
using System.Collections.Generic;
using System.Linq;
using static BattleActionTestHelper;

[TestSuite]
[RequireGodotRuntime]
public partial class ArmorRegenTest
{
  private sealed record Fixture(
    BattleSession Session,
    BattleActionExecutor Executor,
    Faction PlayerFaction,
    Faction EnemyFaction,
    BattleUnitState Unit,
    ItemWith<ArmorCapability> Armor);

  private static Fixture MakeFixture(int armor = 10, int regenDelayTurns = 2, int regenPerTurn = 3)
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(8, 1, 8), [playerFaction, enemyFaction]);
    // Thermal armor vs ApplyDamage's Kinetic packets: depletion stays at 1x.
    var armorItem = BattleTestFactory.MakeArmor("Recharger", armor: armor, element: Element.Thermal, regenDelayTurns: regenDelayTurns, regenPerTurn: regenPerTurn);
    var executor = new BattleActionExecutor(session);
    var unit = executor.Submit(BattleAction.SpawnUnit(
        BattleTestFactory.MakeCombatant("Alpha", playerFaction, health: 30),
        new Vector3I(1, 0, 1), BattleTestFactory.MakeWeapon("Rifle"), armorItem))
      .RequireSingleResult().AffectedUnit.RequireSome();
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Hostile", enemyFaction), new Vector3I(6, 0, 6));
    StartBattle(session);
    return new Fixture(session, executor, playerFaction, enemyFaction, unit, armorItem);
  }

  private static void EndTurn(Fixture fixture, Faction side)
  {
    var result = fixture.Executor.Submit(BattleAction.EndFactionTurn(side)).RequireSingleResult();
    Assert.True(result.Succeeded);
  }

  [TestCase(TestName = "Regen waits out the delay then restores per owning-faction turn end")]
  public void RegenWaitsOutDelayThenRestores()
  {
    var fixture = MakeFixture(armor: 10, regenDelayTurns: 2, regenPerTurn: 3);
    fixture.Executor.Submit(BattleAction.ApplyDamage(fixture.Unit, 5)).RequireSingleResult();
    Assert.Equal(5, fixture.Armor.Capability.Current);
    Assert.Equal(2, fixture.Armor.Capability.RegenDelayRemaining);

    EndTurn(fixture, fixture.PlayerFaction);   // delay 2 -> 1
    Assert.Equal(1, fixture.Armor.Capability.RegenDelayRemaining);
    Assert.Equal(5, fixture.Armor.Capability.Current);

    EndTurn(fixture, fixture.EnemyFaction);
    EndTurn(fixture, fixture.PlayerFaction);   // delay 1 -> 0
    Assert.Equal(0, fixture.Armor.Capability.RegenDelayRemaining);
    Assert.Equal(5, fixture.Armor.Capability.Current);

    EndTurn(fixture, fixture.EnemyFaction);
    EndTurn(fixture, fixture.PlayerFaction);   // regen 3: 5 -> 8
    Assert.Equal(8, fixture.Armor.Capability.Current);

    EndTurn(fixture, fixture.EnemyFaction);
    EndTurn(fixture, fixture.PlayerFaction);   // regen clamped: 8 -> 10
    Assert.Equal(10, fixture.Armor.Capability.Current);

    var raisedEvents = new List<BattleEvent>();
    fixture.Session.BattleEventCommitted += raisedEvents.Add;
    EndTurn(fixture, fixture.EnemyFaction);
    EndTurn(fixture, fixture.PlayerFaction);   // at max: no regen event
    Assert.False(raisedEvents.OfType<UnitArmorRegeneratedBattleEvent>().Any());
  }

  [TestCase(TestName = "Enemy turn end does not tick the player's regen")]
  public void EnemyTurnEndDoesNotTickPlayerRegen()
  {
    var fixture = MakeFixture(regenDelayTurns: 2);
    fixture.Executor.Submit(BattleAction.ApplyDamage(fixture.Unit, 6)).RequireSingleResult();

    EndTurn(fixture, fixture.PlayerFaction);   // delay 2 -> 1
    EndTurn(fixture, fixture.EnemyFaction);    // must not tick

    Assert.Equal(1, fixture.Armor.Capability.RegenDelayRemaining);
  }

  [TestCase(TestName = "Damage mid-countdown re-arms the delay")]
  public void DamageMidCountdownReArmsDelay()
  {
    var fixture = MakeFixture(regenDelayTurns: 2);
    fixture.Executor.Submit(BattleAction.ApplyDamage(fixture.Unit, 3)).RequireSingleResult();
    EndTurn(fixture, fixture.PlayerFaction);   // delay 2 -> 1

    fixture.Executor.Submit(BattleAction.ApplyDamage(fixture.Unit, 1)).RequireSingleResult();

    Assert.Equal(2, fixture.Armor.Capability.RegenDelayRemaining);
  }

  [TestCase(TestName = "Armor without a regen rate never regenerates")]
  public void ArmorWithoutRegenRateNeverRegenerates()
  {
    var fixture = MakeFixture(armor: 10, regenDelayTurns: 0, regenPerTurn: 0);
    fixture.Executor.Submit(BattleAction.ApplyDamage(fixture.Unit, 6)).RequireSingleResult();

    var raisedEvents = new List<BattleEvent>();
    fixture.Session.BattleEventCommitted += raisedEvents.Add;
    EndTurn(fixture, fixture.PlayerFaction);
    EndTurn(fixture, fixture.EnemyFaction);
    EndTurn(fixture, fixture.PlayerFaction);

    Assert.Equal(4, fixture.Armor.Capability.Current);
    Assert.False(raisedEvents.OfType<UnitArmorRegeneratedBattleEvent>().Any());
  }

  [TestCase(TestName = "Dead units are not ticked")]
  public void DeadUnitsAreNotTicked()
  {
    var fixture = MakeFixture(armor: 5, regenDelayTurns: 0, regenPerTurn: 5);
    fixture.Executor.Submit(BattleAction.ApplyDamage(fixture.Unit, 50)).RequireSingleResult();
    Assert.True(fixture.Unit.IsDead);

    var raisedEvents = new List<BattleEvent>();
    fixture.Session.BattleEventCommitted += raisedEvents.Add;
    // The player is still the active side (death does not auto-advance the turn);
    // ending the player's own turn is also the tick that would regen this unit.
    EndTurn(fixture, fixture.PlayerFaction);

    Assert.False(raisedEvents.OfType<UnitArmorRegeneratedBattleEvent>().Any());
  }

  [TestCase(TestName = "Regen event lands between turn-ended and the next turn-started")]
  public void RegenEventLandsAfterTurnEnded()
  {
    var fixture = MakeFixture(armor: 10, regenDelayTurns: 0, regenPerTurn: 3);
    fixture.Executor.Submit(BattleAction.ApplyDamage(fixture.Unit, 6)).RequireSingleResult();

    var raisedEvents = new List<BattleEvent>();
    fixture.Session.BattleEventCommitted += raisedEvents.Add;
    EndTurn(fixture, fixture.PlayerFaction);

    var regenEvent = raisedEvents.OfType<UnitArmorRegeneratedBattleEvent>().Single();
    Assert.Equal(3, regenEvent.AmountRegenerated);
    Assert.Equal(7, regenEvent.CurrentArmor);
    int turnEndedIndex = raisedEvents.FindIndex(battleEvent => battleEvent is TurnEndedBattleEvent);
    int regenIndex = raisedEvents.FindIndex(battleEvent => battleEvent is UnitArmorRegeneratedBattleEvent);
    int turnStartedIndex = raisedEvents.FindIndex(battleEvent => battleEvent is TurnStartedBattleEvent);
    Assert.True(regenIndex > turnEndedIndex);
    Assert.True(turnStartedIndex > regenIndex);
  }

  [TestCase(TestName = "Health-only damage with depleted armor still re-arms the delay")]
  public void HealthOnlyDamageWithDepletedArmorReArmsDelay()
  {
    var fixture = MakeFixture(armor: 4, regenDelayTurns: 2, regenPerTurn: 3);
    fixture.Executor.Submit(BattleAction.ApplyDamage(fixture.Unit, 4)).RequireSingleResult();
    Assert.Equal(0, fixture.Armor.Capability.Current);

    EndTurn(fixture, fixture.PlayerFaction);   // delay 2 -> 1
    Assert.Equal(1, fixture.Armor.Capability.RegenDelayRemaining);

    // Armor is depleted: this damage is health-only, and must still reset the countdown.
    fixture.Executor.Submit(BattleAction.ApplyDamage(fixture.Unit, 2)).RequireSingleResult();

    Assert.Equal(2, fixture.Armor.Capability.RegenDelayRemaining);
  }
}
