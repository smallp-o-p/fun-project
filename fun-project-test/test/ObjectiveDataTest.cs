using FunProject.Battle;
using GdUnit4;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public class ObjectiveDataTest
{
  [TestCase(TestName = "EliminateAll data instantiates the runtime objective bound to itself")]
  public void EliminateAllInstantiatesRuntimeObjective()
  {
    var data = new EliminateAllOpposingForcesObjectiveData();

    var runtime = data.Instantiate();

    AssertThat(runtime).IsInstanceOf<EliminateAllOpposingForcesObjective>();
    AssertThat(data).IsSame(runtime.Data);
  }

  [TestCase(TestName = "SurviveUntilTurn data carries its target turn into the runtime objective")]
  public void SurviveUntilTurnInstantiatesRuntimeObjective()
  {
    var data = new SurviveUntilTurnObjectiveData { TargetTurn = 4 };

    var runtime = data.Instantiate();

    AssertThat(runtime).IsInstanceOf<SurviveUntilTurnObjective>();
    AssertThat(data).IsSame(runtime.Data);
  }

  [TestCase(TestName = "Directives expose their authored payloads")]
  public void DirectivesExposePayloads()
  {
    var end = new EndBattleDirectiveData { Outcome = BattleOutcome.Defeat };
    var followUp = new SurviveUntilTurnObjectiveData { TargetTurn = 2 };
    var queue = new QueueDirectiveData { FollowUps = [followUp] };

    Assert.Equal(BattleOutcome.Defeat, end.Outcome);
    Assert.Equal(1, queue.FollowUps.Length);
    AssertThat(followUp).IsSame(queue.FollowUps[0]);
  }
}
