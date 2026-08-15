using FunProject.Battle;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class SessionStateQueriesTest
{
  [TestCase(TestName = "Active side and phase queries read live session truth")]
  public void ActiveSideAndPhaseQueriesReadSessionTruth()
  {
    var battle = new BattleDuelBuilder
    {
      Player = new("Hero"),
      Enemy = new("Goon"),
    }.Start();
    using var runtime = new BattleRuntime(battle.Session);

    Assert.Equal(BattlePhase.InProgress, runtime.Query(new GetBattlePhaseQuery()));
    Assert.Equal(battle.PlayerFaction, runtime.Query(new GetActiveSideQuery()));

    runtime.ExecuteAction(BattleAction.EndFactionTurn(battle.PlayerFaction));

    Assert.Equal(battle.EnemyFaction, runtime.Query(new GetActiveSideQuery()));
  }
}
