using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Core;
using FunProject.Items.Effects;
using FunProject.Tests;
using GdUnit4;
using Godot;
using System.Collections.Generic;
using System.Linq;
using static BattleActionTestHelper;

[TestSuite]
[RequireGodotRuntime]
public class BattleOutcomeTest
{
  [TestCase(TestName = "A session exposes its declared player faction and no outcome until it ends")]
  public void SessionExposesPlayerFactionAndNoOutcomeUntilEnded()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));

    Assert.Equal(player, session.PlayerFaction.RequireSome());
    Assert.True(session.Outcome.IsNone);
  }

  [TestCase(TestName = "A wiped player loses immediately")]
  public void PlayerWipeLosesImmediately()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    var raised = new List<BattleEvent>();
    session.BattleEventCommitted += raised.Add;

    var playerUnit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", player), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Bandit", enemy), new Vector3I(2, 0, 0));
    StartBattle(session);

    ApplyDamage(session, playerUnit.State, 999);

    Assert.Equal(BattlePhase.Ended, session.Phase);
    Assert.Equal(BattleOutcome.Defeat, session.Outcome.RequireSome());
    var ended = raised.OfType<SessionEndedBattleEvent>().Single();
    Assert.Equal(BattleOutcome.Defeat, ended.Outcome);
  }

  [TestCase(TestName = "Player becoming the sole surviving side resolves to Victory at the end of the turn")]
  public void PlayerSoleSurvivorResolvesToVictoryAtTurnEnd()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    var raised = new List<BattleEvent>();
    session.BattleEventCommitted += raised.Add;

    SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", player), new Vector3I(0, 0, 0));
    var enemyUnit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Bandit", enemy), new Vector3I(2, 0, 0));
    StartBattle(session);

    ApplyDamage(session, enemyUnit.State, 999);
    Assert.Equal(BattlePhase.InProgress, session.Phase);
    Assert.True(session.Outcome.IsNone);

    AdvanceTurn(session);

    Assert.Equal(BattlePhase.Ended, session.Phase);
    Assert.Equal(BattleOutcome.Victory, session.Outcome.RequireSome());
    var ended = raised.OfType<SessionEndedBattleEvent>().Single();
    Assert.Equal(BattleOutcome.Victory, ended.Outcome);
  }

  [TestCase(TestName = "Without a player faction a sole survivor keeps playing and the battle does not end")]
  public void NoPlayerSoleSurvivorKeepsPlaying()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [factionA, factionB]);
    var raised = new List<BattleEvent>();
    session.BattleEventCommitted += raised.Add;

    SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    var bUnit = SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", factionB), new Vector3I(2, 0, 0));
    StartBattle(session);

    ApplyDamage(session, bUnit.State, 999);
    AdvanceTurn(session);

    Assert.Equal(BattlePhase.InProgress, session.Phase);
    Assert.True(session.Outcome.IsNone);
    Assert.False(raised.OfType<SessionEndedBattleEvent>().Any());

    // A lone survivor keeps taking turns across the round boundary — the path
    // adjacent to StartNextRound's impossible-state guard — without ending.
    AdvanceTurn(session);

    Assert.Equal(BattlePhase.InProgress, session.Phase);
    Assert.Equal(3, session.TurnNumber);
    Assert.True(session.Outcome.IsNone);
    Assert.False(raised.OfType<SessionEndedBattleEvent>().Any());
  }

  [TestCase(TestName = "Without a player faction total annihilation resolves to Draw at the end of the turn")]
  public void NoPlayerTotalAnnihilationResolvesToDraw()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [factionA, factionB]);
    var raised = new List<BattleEvent>();
    session.BattleEventCommitted += raised.Add;

    var aUnit = SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    var bUnit = SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", factionB), new Vector3I(2, 0, 0));
    StartBattle(session);

    ApplyDamage(session, bUnit.State, 999);
    ApplyDamage(session, aUnit.State, 999);
    Assert.Equal(BattlePhase.InProgress, session.Phase);

    AdvanceTurn(session);

    Assert.Equal(BattlePhase.Ended, session.Phase);
    Assert.Equal(BattleOutcome.Draw, session.Outcome.RequireSome());
    var ended = raised.OfType<SessionEndedBattleEvent>().Single();
    Assert.Equal(BattleOutcome.Draw, ended.Outcome);
  }

  [TestCase(TestName = "An end-of-turn DoT kill that wipes the player resolves to Defeat without throwing")]
  public void EndOfTurnDotKillResolvesOutcome()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    var raised = new List<BattleEvent>();
    session.BattleEventCommitted += raised.Add;

    var playerUnit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", player, health: 2), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Bandit", enemy), new Vector3I(2, 0, 0));
    StartBattle(session);

    playerUnit.State.ApplyStatusEffect(new DamageOverTimeStatusSpecData
    {
      Name = "Burn",
      DurationTurns = 2,
      TickDamage = 5,
      TickElement = Element.Kinetic,
    });

    AdvanceTurn(session);

    Assert.Equal(BattlePhase.Ended, session.Phase);
    Assert.Equal(BattleOutcome.Defeat, session.Outcome.RequireSome());
    var ended = raised.OfType<SessionEndedBattleEvent>().Single();
    Assert.Equal(BattleOutcome.Defeat, ended.Outcome);
  }

  private static void AdvanceTurn(BattleSession session)
  {
    var executor = new BattleActionExecutor(session);
    var result = executor.Submit(BattleAction.EndFactionTurn(session.ActiveSide)).RequireSingleResult();
    Assert.True(result.Succeeded);
  }

  private static void ApplyDamage(BattleSession session, BattleUnitState unit, int amount)
  {
    var executor = new BattleActionExecutor(session);
    var result = executor.Submit(BattleAction.ApplyDamage(unit, amount)).RequireSingleResult();
    Assert.True(result.Succeeded);
  }
}
