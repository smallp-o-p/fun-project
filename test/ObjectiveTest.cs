using FunProject.Battle;
using FunProject.Weapons;
using GdUnit4;
using Godot;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class ObjectiveTest
{
  [TestCase(TestName = "EliminateAll passes only once all other factions are wiped")]
  public static void EliminateAllPassesWhenOpponentsWiped()
  {
    using var battle = BattleFixture.Duel(
      dimensions: new Vector3I(5, 1, 5),
      player: new("P1", Position: new Vector3I(0, 0, 0)),
      enemy: new("E1", Position: new Vector3I(2, 0, 0)));

    var data = new EliminateAllOpposingForcesObjectiveData();
    var objective = data.Instantiate();
    Assert.True(objective is EliminateAllOpposingForcesObjective);
    Assert.True(ReferenceEquals(data, objective.Data));
    var anyEvent = new TurnEndedBattleEvent(battle.PlayerFaction, 1);

    Assert.Equal(ObjectiveResult.Ongoing, objective.Check(battle.PlayerFaction, anyEvent, battle.Session));

    battle.ApplyDamage(battle.EnemyUnit, 999);

    Assert.Equal(ObjectiveResult.Passed, objective.Check(battle.PlayerFaction, anyEvent, battle.Session));
  }

  [TestCase(TestName = "EliminateAll fails when the owner is wiped")]
  public static void EliminateAllFailsWhenOwnerWiped()
  {
    using var battle = BattleFixture.Duel(
      dimensions: new Vector3I(5, 1, 5),
      player: new("P1", Position: new Vector3I(0, 0, 0)),
      enemy: new("E1", Position: new Vector3I(2, 0, 0)));

    var objective = new EliminateAllOpposingForcesObjective(new EliminateAllOpposingForcesObjectiveData());
    var anyEvent = new TurnEndedBattleEvent(battle.PlayerFaction, 1);

    battle.ApplyDamage(battle.PlayerUnit, 999);

    Assert.Equal(ObjectiveResult.Failed, objective.Check(battle.PlayerFaction, anyEvent, battle.Session));
  }

  [TestCase]
  public static void EliminationIgnoresActionPointExhaustionAndImmobilization()
  {
    using var battle = BattleFixture.Duel();
    var objective = new EliminateAllOpposingForcesObjectiveData().Instantiate();
    var turnEnd = new TurnEndedBattleEvent(battle.PlayerFaction, 1);
    battle.PlayerUnit.ApplyStatusEffect(MakeStun());
    battle.EnemyUnit.SpendActionPoints(battle.EnemyUnit.CurrentActionPoints);

    Assert.Equal(ObjectiveResult.Ongoing, objective.Check(battle.PlayerFaction, turnEnd, battle.Session));
    Assert.Equal(ObjectiveResult.Ongoing, objective.Check(battle.EnemyFaction, turnEnd, battle.Session));
  }

  [TestCase]
  public static void SurvivalStillCountsAnUnconsciousLivingOwner()
  {
    using var battle = BattleFixture.Duel();
    var data = new SurviveUntilTurnObjectiveData { TargetTurn = 1 };
    var objective = data.Instantiate();
    Assert.True(objective is SurviveUntilTurnObjective);
    Assert.True(ReferenceEquals(data, objective.Data));
    battle.ApplyDamage(battle.PlayerUnit, 20, DamageKind.Stun);

    Assert.Equal(ObjectiveResult.Passed, objective.Check(battle.PlayerFaction,
      new TurnStartedBattleEvent(battle.PlayerFaction, 1), battle.Session));
  }

}
