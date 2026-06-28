using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Tests;
using GdUnit4;
using Godot;
using System.Collections.Generic;
using System.Linq;
using static BattleActionTestHelper;

[TestSuite]
[RequireGodotRuntime]
public class ObjectiveBattleTest
{
  [TestCase(TestName = "Completing the player's operation wins the battle at turn end")]
  public void PlayerOperationCompletedWinsAtTurnEnd()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    session.AddObjective(player, new FakeObjective { Complete = true });
    var raised = new List<BattleEvent>();
    session.BattleEventCommitted += raised.Add;
    StartBattle(session);

    AdvanceTurn(session); // end the player's turn

    Assert.Equal(BattlePhase.Ended, session.Phase);
    Assert.Equal(BattleOutcome.Victory, session.Outcome.RequireSome());
    Assert.Equal(BattleOutcome.Victory, raised.OfType<SessionEndedBattleEvent>().Single().Outcome);
  }

  [TestCase(TestName = "A SurviveUntilTurn objective wins once the target turn is reached")]
  public void SurviveUntilTurnObjectiveWins()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    session.AddObjective(player, new SurviveUntilTurnObjective(new ObjectiveData(), targetTurn: 2));
    StartBattle(session);

    AdvanceTurn(session); // player turn 1 ends — TurnNumber still 1, not yet complete
    Assert.Equal(BattlePhase.InProgress, session.Phase);
    AdvanceTurn(session); // enemy turn 1 ends — round rolls over to TurnNumber 2
    AdvanceTurn(session); // player turn 2 ends — SurviveUntilTurn(2) completes

    Assert.Equal(BattlePhase.Ended, session.Phase);
    Assert.Equal(BattleOutcome.Victory, session.Outcome.RequireSome());
  }

  [TestCase(TestName = "Failing the player's (non-wipe) operation loses the battle at turn end")]
  public void PlayerOperationFailedLosesAtTurnEnd()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    session.AddObjective(player, new FakeObjective { Failed = true });
    StartBattle(session);

    AdvanceTurn(session);

    Assert.Equal(BattlePhase.Ended, session.Phase);
    Assert.Equal(BattleOutcome.Defeat, session.Outcome.RequireSome());
  }

  [TestCase(TestName = "A wiped player loses immediately, before the turn ends")]
  public void PlayerWipeEndsImmediately()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    var playerUnit = SpawnUnit(session, BattleTestFactory.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    var raised = new List<BattleEvent>();
    session.BattleEventCommitted += raised.Add;
    StartBattle(session);

    // Kill the player's only unit directly (no EndFactionTurn / AdvanceTurn).
    new BattleActionExecutor(session).Submit(BattleAction.ApplyDamage(playerUnit.State, 999)).RequireSingleResult();

    Assert.Equal(BattlePhase.Ended, session.Phase);
    Assert.Equal(BattleOutcome.Defeat, session.Outcome.RequireSome());
    Assert.Equal(BattleOutcome.Defeat, raised.OfType<SessionEndedBattleEvent>().Single().Outcome);
  }

  [TestCase(TestName = "A failed operation reactivated with a new objective can still win")]
  public void FailedOperationReactivatedCanWin()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));

    var task = new FakeObjective { Failed = true };
    var exfil = new FakeObjective { Complete = false };
    session.AddObjective(player, task);
    // When the operation fails, hand the player an exfiltrate objective inline.
    session.RegisterListener<OperationFailedBattleEvent>(new AddObjectiveOnOperationFailed(player, exfil));
    StartBattle(session);

    AdvanceTurn(session); // player's task fails -> exfil added -> operation reactivated (Active)
    Assert.Equal(BattlePhase.InProgress, session.Phase);
    Assert.Equal(OperationStatus.Active, session.GetOperation(player).RequireSome().Status);

    exfil.Complete = true; // player reaches the extraction point
    // The player's reactivated operation is now evaluated at EVERY turn end, not only the
    // player's own. So it completes at the enemy's turn end (cross-faction evaluation) ->
    // Victory, a full round earlier than the old owner-only timing.
    AdvanceTurn(session); // enemy turn end -> exfil completes -> Victory

    Assert.Equal(BattlePhase.Ended, session.Phase);
    Assert.Equal(BattleOutcome.Victory, session.Outcome.RequireSome());
  }

  [TestCase(TestName = "The player's eliminate-all completes at the enemy's turn end when a reaction kills the last enemy")]
  public void CrossFactionCompletionResolvesAtTheTurnItHappens()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy], playerFaction: Some(player));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    var enemyUnit = SpawnUnit(session, BattleTestFactory.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    // The player wins by eliminating all opposing forces; the enemy gets the auto-filled objective.
    session.AddObjective(player, new EliminateAllOpposingForcesObjective(new ObjectiveData()));
    var raised = new List<BattleEvent>();
    session.BattleEventCommitted += raised.Add;
    StartBattle(session);

    AdvanceTurn(session); // player's turn ends: an enemy still lives, so nothing resolves
    Assert.Equal(BattlePhase.InProgress, session.Phase);
    Assert.Equal(enemy, session.ActiveSide); // it is now the enemy's turn

    // A reaction (overwatch/mine) kills the last enemy DURING the enemy's own turn. The
    // player's eliminate-all becomes complete, but it is not the player's turn.
    new BattleActionExecutor(session).Submit(BattleAction.ApplyDamage(enemyUnit.State, 999)).RequireSingleResult();
    Assert.False(session.HasLivingUnits(enemy));
    Assert.Equal(BattlePhase.InProgress, session.Phase); // a non-player wipe does not end the battle immediately

    AdvanceTurn(session); // enemy's turn ends: the player's operation is evaluated cross-faction -> Victory

    // Old (owner-only) timing would not resolve this until the player's NEXT turn end, a full
    // round later. With global evaluation it resolves at the turn the kill happened on.
    Assert.Equal(BattlePhase.Ended, session.Phase);
    Assert.Equal(BattleOutcome.Victory, session.Outcome.RequireSome());
    Assert.Equal(1, raised.OfType<ObjectiveCompletedBattleEvent>().Count(e => e.Faction == player));
    Assert.Equal(1, raised.OfType<OperationCompletedBattleEvent>().Count(e => e.Faction == player));
    Assert.Equal(BattleOutcome.Victory, raised.OfType<SessionEndedBattleEvent>().Single().Outcome);
  }

  [TestCase(TestName = "StartBattle throws when a faction has no objective")]
  public void StartBattleThrowsWhenAFactionHasNoObjective()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [player, enemy]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    session.AddObjective(player, new FakeObjective());
    // enemy has no objective; bypass the auto-filling StartBattle helper to exercise the guard.

    Assert.Throws<InvalidOperationException>(() => session.StartBattle());
  }

  private sealed class AddObjectiveOnOperationFailed : BattleEventListener
  {
    private readonly Faction _faction;
    private readonly Objective _toAdd;
    private bool _added;

    public AddObjectiveOnOperationFailed(Faction faction, Objective toAdd)
    {
      _faction = faction;
      _toAdd = toAdd;
    }

    public override void OnEventCommitted(BattleSession session, BattleEvent battleEvent)
    {
      if (_added)
        return;
      if (battleEvent is OperationFailedBattleEvent failed && failed.Faction == _faction)
      {
        _added = true;
        session.AddObjective(_faction, _toAdd);
      }
    }
  }

  private static void AdvanceTurn(BattleSession session)
  {
    var result = new BattleActionExecutor(session).Submit(BattleAction.EndFactionTurn(session.ActiveSide)).RequireSingleResult();
    Assert.True(result.Succeeded);
  }
}
