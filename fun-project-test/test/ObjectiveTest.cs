using FunProject.Battle;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public class ObjectiveTest
{
  [TestCase(TestName = "EliminateAll is complete only once all other factions are wiped")]
  public void EliminateAllCompletesWhenOpponentsWiped()
  {
    var battle = new BattleDuelBuilder
    {
      Dimensions = new Vector3I(5, 1, 5),
      Player = new("P1", Position: new Vector3I(0, 0, 0)),
      Enemy = new("E1", Position: new Vector3I(2, 0, 0)),
    }.Start();

    var objective = new EliminateAllOpposingForcesObjective(new ObjectiveData()) { Owner = battle.PlayerFaction };

    Assert.False(objective.IsComplete(battle.Session));
    Assert.False(objective.IsFailed(battle.Session));

    ApplyDamage(battle.Session, battle.EnemyUnit, 999);

    Assert.True(objective.IsComplete(battle.Session));
    Assert.False(objective.IsFailed(battle.Session));
  }

  [TestCase(TestName = "EliminateAll fails when the owner is wiped")]
  public void EliminateAllFailsWhenOwnerWiped()
  {
    var battle = new BattleDuelBuilder
    {
      Dimensions = new Vector3I(5, 1, 5),
      Player = new("P1", Position: new Vector3I(0, 0, 0)),
      Enemy = new("E1", Position: new Vector3I(2, 0, 0)),
    }.Start();

    var objective = new EliminateAllOpposingForcesObjective(new ObjectiveData()) { Owner = battle.PlayerFaction };
    ApplyDamage(battle.Session, battle.PlayerUnit, 999);

    Assert.True(objective.IsFailed(battle.Session));
    Assert.False(objective.IsComplete(battle.Session));
  }

  [TestCase(TestName = "SurviveUntilTurn completes once the target turn is reached")]
  public void SurviveUntilTurnCompletesAtTargetTurn()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [player]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    StartBattle(session);

    var objective = new SurviveUntilTurnObjective(new ObjectiveData(), targetTurn: 1) { Owner = player };
    Assert.True(objective.IsComplete(session)); // TurnNumber starts at 1

    var later = new SurviveUntilTurnObjective(new ObjectiveData(), targetTurn: 5) { Owner = player };
    Assert.False(later.IsComplete(session));
  }
}
