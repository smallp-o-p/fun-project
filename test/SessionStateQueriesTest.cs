using FunProject.Battle;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class SessionStateQueriesTest
{
  [TestCase(TestName = "Turn and completion queries read live lifecycle truth")]
  public void ActiveSideAndPhaseQueriesReadSessionTruth()
  {
    using var battle = BattleFixture.Duel(player: new("Hero"), enemy: new("Goon"));

    BattleTurn turn = battle.Query(new GetCurrentTurnQuery()).RequireSome();
    Assert.Equal(battle.PlayerFaction, turn.ActiveFaction);
    Assert.True(battle.Query(new GetCompletedBattleQuery()).IsNone);

    battle.EndFactionTurn(battle.PlayerFaction);

    Assert.Equal(battle.EnemyFaction, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
  }
}
