using FunProject.Battle;
using FunProject.Core;
using FunProject.Items.Effects;
using FunProject.Weapons;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public class BattleOutcomeTest
{
  [TestCase(false, BattleOutcome.Victory, TestName = "Knocking out the last enemy wins the battle")]
  [TestCase(true, BattleOutcome.Defeat, TestName = "Knocking out the last player unit loses the battle")]
  public void LastConsciousUnitKnockoutEndsTheBattle(bool playerWiped, BattleOutcome expected)
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    battle.ClearEvents();

    battle.ApplyDamage(playerWiped ? battle.PlayerUnit : battle.EnemyUnit, 20, DamageKind.Stun);

    Assert.Equal(expected, battle.Session.Outcome.RequireSome());
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

    battle.ApplyDamage(battle.EnemyUnit, amount, kind);
    battle.ApplyDamage(battle.PlayerUnit, amount, kind);
    Assert.True(battle.Session.Outcome.IsNone);

    if (kind == DamageKind.Stun)
      battle.EndFactionTurn(battle.PlayerFaction);
    else
      battle.AdvanceTurn();

    Assert.True(battle.Session.Outcome.IsSome);
    Assert.Equal(BattleOutcome.Draw, battle.Session.Outcome.RequireSome());
    var ended = battle.Events.EventsOf<SessionEndedBattleEvent>().AsValueEnumerable().Single();
    Assert.Equal(BattleOutcome.Draw, ended.Outcome);
  }

  [TestCase(TestName = "A session exposes its declared player faction and no outcome until it ends")]
  public void SessionExposesPlayerFactionAndNoOutcomeUntilEnded()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    var board = new BattleBoardState(new Vector3I(5, 1, 5));
    var p1 = TestData.PlacedUnit(board, 0, TestData.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    var e1 = TestData.PlacedUnit(board, 1, TestData.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    var session = new BattleSession(board, [player, enemy], [p1, e1], [], [], playerFaction: Some(player));

    Assert.Equal(player, session.PlayerFaction.RequireSome());
    Assert.True(session.Outcome.IsNone);
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

    Assert.True(battle.Session.Outcome.IsSome);
    Assert.Equal(expected, battle.Session.Outcome.RequireSome());
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

    Assert.True(battle.Session.Outcome.IsNone);
    Assert.False(battle.Events.EventsOf<SessionEndedBattleEvent>().AsValueEnumerable().Any());

    // A lone survivor keeps taking turns across the round boundary — the path
    // adjacent to StartNextRound's impossible-state guard — without ending.
    battle.AdvanceTurn();

    Assert.True(battle.Session.Outcome.IsNone);
    Assert.Equal(3, battle.Session.TurnNumber);
    Assert.True(battle.Session.Outcome.IsNone);
    Assert.False(battle.Events.EventsOf<SessionEndedBattleEvent>().AsValueEnumerable().Any());
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

    Assert.True(battle.Session.Outcome.IsSome);
    Assert.Equal(BattleOutcome.Defeat, battle.Session.Outcome.RequireSome());
    var ended = battle.Events.EventsOf<SessionEndedBattleEvent>().AsValueEnumerable().Single();
    Assert.Equal(BattleOutcome.Defeat, ended.Outcome);
  }
}
