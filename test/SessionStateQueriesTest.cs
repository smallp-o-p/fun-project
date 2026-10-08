using FunProject.Battle;
using GdUnit4;
using Godot;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class SessionStateQueriesTest
{
  [TestCase(TestName = "Active side and outcome queries read live session truth")]
  public static void ActiveSideAndOutcomeQueriesReadSessionTruth()
  {
    using var battle = BattleFixture.Duel(player: new("Hero"), enemy: new("Goon"));

    Assert.True(battle.Query(new GetBattleOutcomeQuery()).IsNone);
    Assert.Equal(battle.PlayerFaction, battle.Query(new GetActiveSideQuery()));

    battle.EndFactionTurn(battle.PlayerFaction);

    Assert.Equal(battle.EnemyFaction, battle.Query(new GetActiveSideQuery()));
  }

  [TestCase(TestName = "Outcome query reads absence during play and the decided outcome after completion")]
  public static void BattleOutcomeQueryReadsPlayAndCompletion()
  {
    using var battle = BattleFixture.Duel(playerControlled: true);

    Assert.True(battle.Query(new GetBattleOutcomeQuery()).IsNone);

    battle.ApplyDamage(battle.EnemyUnit, 999); // the player's eliminate-all objective ends the battle

    Assert.Equal(BattleOutcome.Victory, battle.Query(new GetBattleOutcomeQuery()).RequireSome());
  }
}
