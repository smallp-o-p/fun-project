using FunProject.Battle;
using FunProject.Core;
using System.Collections.Generic;
using FunProject.Items.Effects;
using FunProject.Stats;
using FunProject.Weapons;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleOutcomeTest
{
  private sealed partial class ActiveOnRound : FunProject.Buffs.BuffCondition
  {
    public int Round { get; set; }

    internal override bool IsMet(BattleReadContext context, BattleUnitState unit) =>
      context.RunningSession.Match(session => session.RoundNumber == Round, () => false);
  }

  [TestCase(false, BattleOutcome.Victory, TestName = "Knocking out the last enemy wins the battle")]
  [TestCase(true, BattleOutcome.Defeat, TestName = "Knocking out the last player unit loses the battle")]
  public void LastConsciousUnitKnockoutEndsTheBattle(bool playerWiped, BattleOutcome expected)
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    battle.ClearEvents();

    battle.ApplyDamage(playerWiped ? battle.PlayerUnit : battle.EnemyUnit, 20, DamageKind.Stun);

    Assert.Equal(expected, battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
    battle.Events.EventBefore<UnitUnconsciousBattleEvent, SessionEndedBattleEvent>();
  }

  [TestCase(DamageKind.Stun, 20, TestName = "Without a player faction total knockout draws only after the current side ends")]
  [TestCase(DamageKind.Health, 999, TestName = "Without a player faction total annihilation resolves to Draw at the end of the turn")]
  public void NoPlayerTotalLossResolvesToDraw(DamageKind kind, int amount)
  {
    using var battle = kind == DamageKind.Stun
      ? BattleFixture.Duel()
      : BattleFixture.Duel(
        dimensions: new Vector3I(5, 1, 5),
        player: new("A1", Position: new Vector3I(0, 0, 0)),
        enemy: new("B1", Position: new Vector3I(2, 0, 0)));
    battle.ClearEvents();

    // Retained options stay materialized across the boundary; completion disables them.
    IReadOnlyList<UnitAction> retained = battle.Query(
      new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)));
    Assert.True(retained.AsValueEnumerable().All(action => action.IsAvailable));

    battle.ApplyDamage(battle.EnemyUnit, amount, kind);
    battle.ApplyDamage(battle.PlayerUnit, amount, kind);
    Assert.True(battle.Query(new GetCurrentTurnQuery()).IsSome);
    Assert.True(battle.Query(new GetCompletedBattleQuery()).IsNone);
    // Opposite lifecycle answers across the settlement boundary, observed in-stream:
    // the outgoing turn is still Some during the step, and both switch inside the end event.
    Option<BattleTurn> turnAtTurnEnd = None;
    Option<CompletedBattle> completionAtTurnEnd = None;
    Option<BattleTurn> turnAtSessionEnd = None;
    Option<CompletedBattle> completionAtSessionEnd = None;
    battle.OnCommitted(battleEvent =>
    {
      switch (battleEvent)
      {
        case TurnEndedBattleEvent:
          turnAtTurnEnd = battle.Query(new GetCurrentTurnQuery());
          completionAtTurnEnd = battle.Query(new GetCompletedBattleQuery());
          break;
        case SessionEndedBattleEvent:
          turnAtSessionEnd = battle.Query(new GetCurrentTurnQuery());
          completionAtSessionEnd = battle.Query(new GetCompletedBattleQuery());
          break;
      }
    });

    if (kind == DamageKind.Stun)
      battle.EndFactionTurn(battle.PlayerFaction);
    else
      battle.AdvanceTurn();

    Assert.True(turnAtTurnEnd.IsSome);
    Assert.True(completionAtTurnEnd.IsNone);
    Assert.True(turnAtSessionEnd.IsNone);
    Assert.Equal(BattleOutcome.Draw, completionAtSessionEnd.RequireSome().Outcome);
    Assert.True(battle.Query(new GetCurrentTurnQuery()).IsNone);
    Assert.Equal(BattleOutcome.Draw, battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
    var ended = battle.Events.EventsOf<SessionEndedBattleEvent>().AsValueEnumerable().Single();
    Assert.Equal(BattleOutcome.Draw, ended.Outcome);
    Assert.True(retained.AsValueEnumerable().All(action => !action.IsAvailable));
  }

  [TestCase(TestName = "A prepared battle exposes its declared player faction and no completion until it ends")]
  public void SessionExposesPlayerFactionAndNoOutcomeUntilEnded()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    battle.Spawn(TestData.MakeCombatant("A1", player), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", enemy), new Vector3I(2, 0, 0));
    battle.Start();

    Assert.Equal(player, battle.Query(new GetPlayerFactionQuery()).RequireSome());
    Assert.True(battle.Query(new GetCompletedBattleQuery()).IsNone);
  }

  [TestCase(true, BattleOutcome.Defeat, TestName = "A wiped player loses immediately")]
  [TestCase(false, BattleOutcome.Victory, TestName = "Player becoming the sole surviving side resolves to Victory the instant the last enemy dies")]
  public void PlayerWipeResolvesOutcomeInstantly(bool playerWiped, BattleOutcome expected)
  {
    using var battle = BattleFixture.Duel(
      dimensions: new Vector3I(5, 1, 5),
      playerControlled: true,
      player: new("Alpha", Position: new Vector3I(0, 0, 0)),
      enemy: new("Bandit", Position: new Vector3I(2, 0, 0)));
    battle.ClearEvents();

    battle.ApplyDamage(playerWiped ? battle.PlayerUnit : battle.EnemyUnit, 999);

    Assert.True(battle.Query(new GetCurrentTurnQuery()).IsNone);
    Assert.Equal(expected, battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
    var ended = battle.Events.EventsOf<SessionEndedBattleEvent>().AsValueEnumerable().Single();
    Assert.Equal(expected, ended.Outcome);
  }

  [TestCase(TestName = "Without a player faction a sole survivor keeps playing and the battle does not end")]
  public void NoPlayerSoleSurvivorKeepsPlaying()
  {
    using var battle = BattleFixture.Duel(
      dimensions: new Vector3I(5, 1, 5),
      player: new("A1", Position: new Vector3I(0, 0, 0)),
      enemy: new("B1", Position: new Vector3I(2, 0, 0)));
    battle.ClearEvents();

    battle.ApplyDamage(battle.EnemyUnit, 999);
    battle.AdvanceTurn();

    Assert.True(battle.Query(new GetCurrentTurnQuery()).IsSome);
    Assert.True(battle.Query(new GetCompletedBattleQuery()).IsNone);
    Assert.False(battle.Events.EventsOf<SessionEndedBattleEvent>().AsValueEnumerable().Any());

    // A lone survivor keeps taking turns across the round boundary — the path
    // adjacent to the empty-round guard — without ending.
    battle.AdvanceTurn();

    Assert.True(battle.Query(new GetCurrentTurnQuery()).IsSome);
    Assert.Equal(3, battle.Query(new GetCurrentTurnQuery()).RequireSome().RoundNumber);
    Assert.True(battle.Query(new GetCompletedBattleQuery()).IsNone);
    Assert.False(battle.Events.EventsOf<SessionEndedBattleEvent>().AsValueEnumerable().Any());
  }

  [TestCase(TestName = "A final conscious player knocked out by a buff clamp requests Defeat with no fabricated damage")]
  public void BuffClampKnockoutOfFinalConsciousPlayerResolvesDefeat()
  {
    // Collapse halves max health (20 -> 10) at the round-2 turn start while the player is
    // stunned to 15: the flip itself knocks the last conscious player unit out, so the
    // player-wipe backstop owns the defeat — no damage cause and no kill event exist.
    var collapse = TestData.MakeBuff(
      "Collapse",
      new ActiveOnRound { Round = 2 },
      statMods: [new HealthStatMod { Modifiers = [StatModifier.Add(-10)] }]);
    using var battle = BattleFixture.Duel(
      playerControlled: true,
      player: new("Alpha", Buffs: [collapse]));
    battle.ClearEvents();

    battle.ApplyDamage(battle.PlayerUnit, 15, DamageKind.Stun);
    Assert.False(battle.PlayerUnit.IsUnconscious);

    battle.AdvanceTurn(); // round 1: enemy turn, buff still inactive
    battle.AdvanceTurn(); // round 2: the player's turn starts and Collapse activates

    Assert.Equal(BattleOutcome.Defeat, battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
    Assert.True(battle.Query(new GetCurrentTurnQuery()).IsNone);
    battle.Events.EventBefore<UnitUnconsciousBattleEvent, SessionEndedBattleEvent>();
    var unconscious = battle.Events.SingleEvent<UnitUnconsciousBattleEvent>();
    Assert.True(unconscious.MaybeCause.IsNone);
    Assert.False(battle.Events.EventsOf<UnitKilledBattleEvent>().AsValueEnumerable().Any());
  }

  [TestCase(TestName = "A final conscious opponent knocked out by a buff clamp drives the elimination objective")]
  public void BuffClampKnockoutOfFinalConsciousOpponentResolvesVictory()
  {
    // The enemy carries the clamp buff: its genuine UnitUnconscious notification at the
    // round-2 turn start completes the player's elimination objective and ends the battle,
    // instead of leaving combat running until an unrelated later event.
    var collapse = TestData.MakeBuff(
      "Collapse",
      new ActiveOnRound { Round = 2 },
      statMods: [new HealthStatMod { Modifiers = [StatModifier.Add(-10)] }]);
    using var battle = BattleFixture.Duel(
      playerControlled: true,
      enemy: new("Hostile", Buffs: [collapse]));
    battle.ClearEvents();

    battle.ApplyDamage(battle.EnemyUnit, 15, DamageKind.Stun);
    Assert.False(battle.EnemyUnit.IsUnconscious);

    battle.AdvanceTurn(); // round 1: enemy turn, buff still inactive
    battle.AdvanceTurn(); // round 2: the player's turn starts and Collapse activates

    Assert.Equal(BattleOutcome.Victory, battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
    Assert.True(battle.Query(new GetCurrentTurnQuery()).IsNone);
    battle.Events.EventBefore<UnitUnconsciousBattleEvent, SessionEndedBattleEvent>();
    var unconscious = battle.Events.SingleEvent<UnitUnconsciousBattleEvent>();
    Assert.True(unconscious.MaybeCause.IsNone);
    Assert.False(battle.Events.EventsOf<UnitKilledBattleEvent>().AsValueEnumerable().Any());
  }

  [TestCase(TestName = "An end-of-turn DoT kill that wipes the player resolves to Defeat without throwing")]
  public void EndOfTurnDotKillResolvesOutcome()
  {
    using var battle = BattleFixture.Duel(
      dimensions: new Vector3I(5, 1, 5),
      playerControlled: true,
      player: new("Alpha", Position: new Vector3I(0, 0, 0), Health: 2),
      enemy: new("Bandit", Position: new Vector3I(2, 0, 0)));
    battle.ClearEvents();

    battle.PlayerUnit.ApplyStatusEffect(new DamageOverTimeStatusSpecData
    {
      Name = "Burn",
      DurationTurns = 2,
      TickDamage = 5,
      TickElement = Element.Kinetic,
    });

    battle.AdvanceTurn();

    Assert.True(battle.Query(new GetCurrentTurnQuery()).IsNone);
    Assert.Equal(BattleOutcome.Defeat, battle.Query(new GetCompletedBattleQuery()).RequireSome().Outcome);
    var ended = battle.Events.EventsOf<SessionEndedBattleEvent>().AsValueEnumerable().Single();
    Assert.Equal(BattleOutcome.Defeat, ended.Outcome);
  }
}
