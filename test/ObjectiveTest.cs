using FunProject.Battle;
using FunProject.Weapons;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public class ObjectiveTest
{
  [TestCase(TestName = "EliminateAll passes only once all other factions are wiped")]
  public void EliminateAllPassesWhenOpponentsWiped()
  {
    using var battle = BattleFixture.Duel(
      dimensions: new Vector3I(5, 1, 5),
      player: new("P1", Position: new Vector3I(0, 0, 0)),
      enemy: new("E1", Position: new Vector3I(2, 0, 0)));

    var objective = new EliminateAllOpposingForcesObjective(new EliminateAllOpposingForcesObjectiveData());
    var anyEvent = new TurnEndedBattleEvent(battle.PlayerFaction, 1);

    Assert.Equal(ObjectiveResult.Ongoing, objective.Check(battle.PlayerFaction, anyEvent, battle.Read));

    battle.ApplyDamage(battle.EnemyUnit, 999);

    Assert.Equal(ObjectiveResult.Passed, objective.Check(battle.PlayerFaction, anyEvent, battle.Read));
  }

  [TestCase(TestName = "EliminateAll fails when the owner is wiped")]
  public void EliminateAllFailsWhenOwnerWiped()
  {
    using var battle = BattleFixture.Duel(
      dimensions: new Vector3I(5, 1, 5),
      player: new("P1", Position: new Vector3I(0, 0, 0)),
      enemy: new("E1", Position: new Vector3I(2, 0, 0)));

    var objective = new EliminateAllOpposingForcesObjective(new EliminateAllOpposingForcesObjectiveData());
    var anyEvent = new TurnEndedBattleEvent(battle.PlayerFaction, 1);

    battle.ApplyDamage(battle.PlayerUnit, 999);

    Assert.Equal(ObjectiveResult.Failed, objective.Check(battle.PlayerFaction, anyEvent, battle.Read));
  }

  [TestCase]
  public void EliminationIgnoresActionPointExhaustionAndImmobilization()
  {
    using var battle = BattleFixture.Duel();
    var objective = new EliminateAllOpposingForcesObjectiveData().Instantiate();
    var turnEnd = new TurnEndedBattleEvent(battle.PlayerFaction, 1);
    battle.PlayerUnit.ApplyStatusEffect(MakeStun());
    battle.EnemyUnit.SpendActionPoints(battle.EnemyUnit.CurrentActionPoints);

    Assert.Equal(ObjectiveResult.Ongoing, objective.Check(battle.PlayerFaction, turnEnd, battle.Read));
    Assert.Equal(ObjectiveResult.Ongoing, objective.Check(battle.EnemyFaction, turnEnd, battle.Read));
  }

  [TestCase]
  public void SurvivalStillCountsAnUnconsciousLivingOwner()
  {
    using var battle = BattleFixture.Duel();
    var objective = new SurviveUntilTurnObjectiveData { TargetTurn = 1 }.Instantiate();
    battle.ApplyDamage(battle.PlayerUnit, 20, DamageKind.Stun);

    Assert.Equal(ObjectiveResult.Passed, objective.Check(battle.PlayerFaction,
      new TurnStartedBattleEvent(battle.PlayerFaction, 1), battle.Read));
  }

  [TestCase(TestName = "SurviveUntilTurn passes once the target turn is reached")]
  public void SurviveUntilTurnPassesAtTargetTurn()
  {
    var player = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [player]);
    battle.Spawn(TestData.MakeCombatant("P1", player), new Vector3I(0, 0, 0));
    battle.Start();
    int roundNumber = battle.Query(new GetCurrentTurnQuery()).RequireSome().RoundNumber;
    var turnStart = new TurnStartedBattleEvent(player, roundNumber);

    var objective = new SurviveUntilTurnObjective(new SurviveUntilTurnObjectiveData { TargetTurn = 1 });
    Assert.Equal(ObjectiveResult.Passed, objective.Check(player, turnStart, battle.Read)); // round 1 is installed

    var later = new SurviveUntilTurnObjective(new SurviveUntilTurnObjectiveData { TargetTurn = 5 });
    Assert.Equal(ObjectiveResult.Ongoing, later.Check(player, turnStart, battle.Read));
  }
}
