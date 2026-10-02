using FunProject.Battle;
using Godot;
using GdUnit4;
using System;
using static FunProject.Tests.GeoscapeTestScenes;
using static GdUnit4.Assertions;

namespace FunProject.Tests;

// Exercises the BattleLauncher standalone host: an exported BattleTypeData must resolve, start,
// and present a fully wired BattleScene (map, unit meshes, HUD) through the Present door, and
// Present must guard its required inputs.
[TestSuite]
[RequireGodotRuntime]
public partial class BattleLauncherTest
{
  [TestCase(TestName = "Launcher boots the battle scene with map, unit meshes, and populated HUD")]
  public void LauncherBootsBattleSceneWithMapUnitMeshesAndHud()
  {
    var type = TestData.MakeDuelBattleType();
    var launcher = new BattleLauncher { BattleType = type };
    AddToTree(launcher);

    Assert.Equal(1, launcher.GetChildCount());
    var battle = (BattleScene)launcher.GetChild(0);
    Assert.True(battle.GetNodeOrNull("Map") is not null);
    var unitMeshes = battle.GetNode<Node3D>("UnitMeshes");
    // The duel type deploys exactly one unit per side.
    Assert.Equal(2, unitMeshes.GetChildren().AsValueEnumerable().OfType<MeshInstance3D>().Count());
    var status = battle.GetNode<BattleHud>("BattleUI/BattleHud").GetNode<Label>("%UnitStatus");
    Assert.True(status.Text.Contains("HP"));
  }

  [TestCase(TestName = "A battle completed during factory startup presents with the end banner shown")]
  public void StartupCompletedBattleShowsTheEndBanner()
  {
    var type = TestData.MakeDuelBattleType();
    type.Factions[0].Objectives.Clear();
    type.Factions[0].Objectives.Add(new SurviveUntilTurnObjectiveData
    {
      TargetTurn = 1,
      OnComplete = new EndBattleDirectiveData { Outcome = BattleOutcome.Victory },
    });
    var launcher = new BattleLauncher { BattleType = type };
    AddToTree(launcher);

    var battle = (BattleScene)launcher.GetChild(0);
    var banner = battle.GetNode<Label>("BattleUI/BattleHud/BattleOverBanner");

    // The UI constructor entered BattleOver before the scene subscribed, so the scene must
    // show the banner from the initial state itself; the VICTORY text proves the frozen
    // completion backed it.
    Assert.True(banner.Visible);
    Assert.Equal("VICTORY", banner.Text);
  }

  [TestCase(TestName = "Present guards against nulls")]
  public void PresentGuardsAgainstNulls()
  {
    var scene = GD.Load<PackedScene>("res://scenes/battle/BattleScene.tscn")
      .Instantiate<BattleScene>();
    AutoFree(scene);

    Assert.Throws<ArgumentNullException>(() => scene.Present(null!, null!));
  }
}
