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
    using var battle = BattleFixture.Duel(player: new("Hero"), enemy: new("Goon"));

    Assert.Equal(BattlePhase.InProgress, battle.Query(new GetBattlePhaseQuery()));
    Assert.Equal(battle.PlayerFaction, battle.Query(new GetActiveSideQuery()));

    battle.EndFactionTurn(battle.PlayerFaction);

    Assert.Equal(battle.EnemyFaction, battle.Query(new GetActiveSideQuery()));
  }
}
