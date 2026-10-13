using FunProject.Battle;
using FunProject.Geoscape;
using FunProject.Weapons;
using Godot;
using GdUnit4;
using System.Threading.Tasks;
using static FunProject.Tests.GeoscapeTestScenes;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class MissionResultsViewTest
{
  [TestCase(800, 600)]
  [TestCase(1600, 900)]
  public static async Task TallyUsesFinalBattleCountsAfterRuntimeDisposal(int width, int height)
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var battle = BattleFixture.Duel(playerControlled: true);
    var fallen = battle.Spawn(MakeCombatant("Fallen", battle.PlayerFaction), new Vector3I(0, 0, 0));
    var unconscious = battle.Spawn(MakeCombatant("Unconscious", battle.PlayerFaction), new Vector3I(1, 0, 0));
    var extraEnemy = battle.Spawn(MakeCombatant("Extra enemy", battle.EnemyFaction), new Vector3I(2, 0, 0));
    var captive = battle.Spawn(MakeCombatant("Captive", battle.EnemyFaction), new Vector3I(3, 0, 0));
    battle.EndFactionTurn(battle.PlayerFaction);
    battle.EndFactionTurn(battle.EnemyFaction);
    battle.ApplyDamage(battle.PlayerUnit, 1);
    battle.ApplyDamage(fallen, 999);
    battle.ApplyDamage(unconscious, 999, DamageKind.Stun);
    battle.ApplyDamage(battle.EnemyUnit, 999);
    battle.ApplyDamage(extraEnemy, 999);
    battle.ApplyDamage(captive, 999, DamageKind.Stun);
    var summary = battle.Query(new GetFactionEndOfBattleSummary(battle.PlayerFaction)).RequireRight();
    var result = battle.Query(new GetBattleResultQuery()).RequireRight();
    Assert.Equal(0, summary.DefeatedPerCombatant.Count, "These enemy deaths have no soldier kill credit.");
    battle.Runtime.Dispose();

    var view = GD.Load<PackedScene>("res://scenes/geoscape/results/MissionResultsView.tscn")
      .Instantiate<MissionResultsView>();
    CreateScaledWindow(view, new Vector2I(width, height));
    view.Bind("Bomb Defusal", summary, result);
    await WaitForLayout(view);

    Assert.Equal("Bomb Defusal", view.GetNode<Label>("%MissionType").Text);
    Assert.Equal("2", view.GetNode<Label>("%TurnsTaken").Text);
    Assert.Equal("1", view.GetNode<Label>("%SoldiersWounded").Text);
    Assert.Equal("1", view.GetNode<Label>("%SoldiersKilled").Text);
    Assert.Equal("2", view.GetNode<Label>("%EnemiesKilled").Text);
    Rect2 screen = ScreenRect(view);
    foreach (string name in new[] { "MissionType", "TurnsTaken", "SoldiersWounded", "SoldiersKilled", "EnemiesKilled", "ContinueButton" })
      Assert.True(screen.Encloses(ScreenRect(view.GetNode<Control>($"%{name}"))), $"{name} must fit the results screen.");
  }
}
