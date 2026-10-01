#nullable disable warnings
using System;
using FunProject.Battle;
using FunProject.Core;
using FunProject.Weapons;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class StunRecoveryTest
{
  [TestCase(8, 5u, 3)]
  [TestCase(3, 3u, 0)]
  public void DefaultRecoveryReducesOnlyTheOwnersStun(int initialStun, uint recovered, int remaining)
  {
    using var battle = BattleFixture.Duel();
    battle.ApplyDamage(battle.PlayerUnit, initialStun, DamageKind.Stun);
    battle.ApplyDamage(battle.EnemyUnit, 8, DamageKind.Stun);
    battle.ClearEvents();

    battle.EndFactionTurn(battle.PlayerFaction);

    Assert.Equal(remaining, battle.PlayerUnit.CurrentStun);
    Assert.Equal(8, battle.EnemyUnit.CurrentStun);
    Assert.Equal(recovered, battle.Events.SingleEvent<UnitStunRecoveredBattleEvent>().AmountRecovered);
  }

  [TestCase]
  public void UnsignedMaximumRecoveryClampsToCurrentStun()
  {
    using var battle = BattleFixture.Duel();
    battle.ApplyDamage(battle.PlayerUnit, 8, DamageKind.Stun);

    uint recovered = battle.PlayerUnit.RecoverStun(uint.MaxValue);

    Assert.Equal(0, battle.PlayerUnit.CurrentStun);
    Assert.Equal(8u, recovered);
  }

  [TestCase]
  public void ZeroStunEmitsNoRecoveryEvent()
  {
    using var battle = BattleFixture.Duel();
    battle.ClearEvents();

    battle.EndFactionTurn(battle.PlayerFaction);

    Assert.False(battle.Events.EventsOf<UnitStunRecoveredBattleEvent>().AsValueEnumerable().Any());
  }

  [TestCase]
  public void UnconsciousUnitDoesNotRecoverAcrossItsFactionsTurns()
  {
    using var battle = BattleFixture.Duel();
    battle.Spawn(TestData.MakeCombatant("Support", battle.PlayerFaction), new Vector3I(0, 0, 0));
    battle.ApplyDamage(battle.PlayerUnit, 20, DamageKind.Stun);
    battle.ClearEvents();

    battle.EndFactionTurn(battle.PlayerFaction);
    battle.EndFactionTurn(battle.EnemyFaction);
    battle.EndFactionTurn(battle.PlayerFaction);

    Assert.Equal(20, battle.PlayerUnit.CurrentStun);
    Assert.False(battle.Events.EventsOf<UnitStunRecoveredBattleEvent>().AsValueEnumerable().Any());
  }

  [TestCase]
  public void DotCausesUnconsciousnessBeforeRecovery()
  {
    using var battle = BattleFixture.Duel();
    battle.Spawn(TestData.MakeCombatant("Support", battle.PlayerFaction), new Vector3I(0, 0, 0));
    battle.ApplyDamage(battle.PlayerUnit, 10, DamageKind.Stun);
    battle.PlayerUnit.ApplyStatusEffect(TestData.MakeBurn(duration: 2, tickDamage: 10));
    battle.ClearEvents();

    battle.EndFactionTurn(battle.PlayerFaction);

    Assert.Equal(10, battle.PlayerUnit.CurrentStun);
    Assert.True(battle.PlayerUnit.IsUnconscious);
    Assert.False(battle.Events.EventsOf<UnitStunRecoveredBattleEvent>().AsValueEnumerable().Any());
  }

  [TestCase]
  public void DeadUnitsDoNotRecover()
  {
    using var battle = BattleFixture.Duel();
    battle.ApplyDamage(battle.PlayerUnit, 8, DamageKind.Stun);
    battle.ApplyDamage(battle.PlayerUnit, 20);

    Assert.Equal(0u, battle.PlayerUnit.RecoverStun(5));
    Assert.Equal(8, battle.PlayerUnit.CurrentStun);
  }

  [TestCase]
  public void ExhaustedAndImmobilizedUnitsStillRecover()
  {
    using var battle = BattleFixture.Duel();
    battle.ApplyDamage(battle.PlayerUnit, 8, DamageKind.Stun);
    battle.PlayerUnit.SpendActionPoints(battle.PlayerUnit.CurrentActionPoints);
    battle.PlayerUnit.ApplyStatusEffect(TestData.MakeStun(duration: 2));

    battle.EndFactionTurn(battle.PlayerFaction);

    Assert.Equal(3, battle.PlayerUnit.CurrentStun);
  }

  [TestCase]
  public void RecoveryFollowsArmorRegeneration()
  {
    var armor = TestData.MakeArmor("Shield", element: Element.Thermal, regenPerTurn: 3);
    using var battle = BattleFixture.Duel(player: new("Alpha", Armor: armor));
    battle.ApplyDamage(battle.PlayerUnit, 4);
    battle.ApplyDamage(battle.PlayerUnit, 8, DamageKind.Stun);
    battle.ClearEvents();

    battle.EndFactionTurn(battle.PlayerFaction);

    battle.Events.EventBefore<UnitArmorRegeneratedBattleEvent, UnitStunRecoveredBattleEvent>();
  }

  [TestCase]
  public void EndedSessionIgnoresRecoveryHook()
  {
    using var battle = BattleFixture.Duel(playerControlled: true, start: false);
    battle.AddObjective(battle.PlayerFaction, new SurviveUntilTurnObjectiveData
    {
      TargetTurn = 1,
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    }.Instantiate());
    battle.Damage(battle.PlayerUnit, 8, DamageKind.Stun);
    battle.Start();   // the terminal opening objective completes before any turn-end upkeep runs
    BattleHook hook = new StunRecoverySystem();

    // A completed event context carries no running receiver, so upkeep skips recovery.
    hook.OnEvent(new HookContext(battle.Read, None),
      new TurnEndedBattleEvent(battle.PlayerFaction, 1));

    Assert.Equal(8, battle.PlayerUnit.CurrentStun);
  }

  [TestCase(TestName = "A pending terminal outcome still recovers conscious survivors during turn-end upkeep")]
  public void PendingTerminalOutcomeStillRecoversConsciousSurvivors()
  {
    var armor = TestData.MakeArmor("Shield", regenPerTurn: 3);
    using var battle = BattleFixture.Duel(playerControlled: true, start: false,
      player: new("Alpha", Armor: armor));
    battle.Start();
    battle.ApplyDamage(battle.PlayerUnit, 4); // armor damage so regeneration has work
    battle.ApplyDamage(battle.PlayerUnit, 8, DamageKind.Stun);
    battle.PlayerUnit.ApplyStatusEffect(TestData.MakeStun(duration: 2));
    // A runtime subscriber requests the end before the upkeep hooks fire: the request lands
    // ahead of recovery and must not cancel it.
    battle.Runtime.BattleEventCommitted += battleEvent =>
    {
      if (battleEvent is TurnEndedBattleEvent)
        battle.Session.RequestEnd(BattleOutcome.Victory);
    };
    battle.ClearEvents();

    battle.EndFactionTurn(battle.PlayerFaction);

    Assert.Equal(3, battle.PlayerUnit.CurrentStun);
    Assert.Equal(BattleOutcome.Victory, battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
    battle.Events.EventBefore<UnitStatusEffectTickedBattleEvent, UnitArmorRegeneratedBattleEvent>();
    battle.Events.EventBefore<UnitArmorRegeneratedBattleEvent, UnitStunRecoveredBattleEvent>();
    battle.Events.EventBefore<UnitStunRecoveredBattleEvent, SessionEndedBattleEvent>();
    Assert.True(battle.Query(new GetCurrentTurnQuery()).IsNone);
    Assert.Equal(0, battle.Events.EventsOf<TurnStartedBattleEvent>().AsValueEnumerable().Count());
  }

  [TestCase]
  public void RecoveryEventRejectsInvalidPayloads()
  {
    using var battle = BattleFixture.Duel();
    Assert.Throws<ArgumentNullException>(() => new UnitStunRecoveredBattleEvent(null, 1, 0));
    Assert.Throws<ArgumentOutOfRangeException>(() => new UnitStunRecoveredBattleEvent(battle.PlayerUnit, 0, 0));
    Assert.Throws<ArgumentOutOfRangeException>(() => new UnitStunRecoveredBattleEvent(battle.PlayerUnit, 1, -1));
  }
}
