using FunProject.Battle;
using FunProject.Tests;
using GdUnit4;
using Godot;
using static BattleActionTestHelper;

[TestSuite]
[RequireGodotRuntime]
public class ObjectiveDataTest
{
  [TestCase(TestName = "EliminateAll is complete only once all other factions are wiped")]
  public void EliminateAllCompletesWhenOpponentsWiped()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    var enemyUnit = SpawnUnit(session, BattleTestFactory.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    StartBattle(session);

    var objective = new Objective(new EliminateAllOpposingForcesObjectiveData()) { Owner = player };

    Assert.False(objective.IsComplete(session));
    Assert.False(objective.IsFailed(session));

    new BattleActionExecutor(session).Submit(BattleAction.ApplyDamage(enemyUnit.State, 999)).RequireSingleResult();

    Assert.True(objective.IsComplete(session));
    Assert.False(objective.IsFailed(session));
  }

  [TestCase(TestName = "EliminateAll fails when the owner is wiped")]
  public void EliminateAllFailsWhenOwnerWiped()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player, enemy]);
    var playerUnit = SpawnUnit(session, BattleTestFactory.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("E1", enemy), new Vector3I(2, 0, 0));
    StartBattle(session);

    var objective = new Objective(new EliminateAllOpposingForcesObjectiveData()) { Owner = player };
    new BattleActionExecutor(session).Submit(BattleAction.ApplyDamage(playerUnit.State, 999)).RequireSingleResult();

    Assert.True(objective.IsFailed(session));
    Assert.False(objective.IsComplete(session));
  }

  [TestCase(TestName = "SurviveUntilTurn completes once the target turn is reached")]
  public void SurviveUntilTurnCompletesAtTargetTurn()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    StartBattle(session);

    var objective = new Objective(new SurviveUntilTurnObjectiveData { TargetTurn = 1 }) { Owner = player };
    Assert.True(objective.IsComplete(session)); // TurnNumber starts at 1

    var later = new Objective(new SurviveUntilTurnObjectiveData { TargetTurn = 5 }) { Owner = player };
    Assert.False(later.IsComplete(session));
  }
}
