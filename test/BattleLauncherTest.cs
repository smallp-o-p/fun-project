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

  [TestCase(TestName = "A failing standalone startup leaves no host and a disposed runtime")]
  public void FailingStartupLeavesNoHostAndDisposedRuntime()
  {
    var capture = new HookCaptureSystemData();
    var type = TestData.MakeDuelBattleType();
    type.Systems.Add(capture);
    type.Systems.Add(new ThrowingSystemData());
    var launcher = new BattleLauncher { BattleType = type };
    AddToTree(launcher); // the bridge logs the _Ready failure; nothing may remain half-built

    Assert.Equal(0, launcher.GetChildCount());
    AssertBattleDisposed(capture);
  }

  [TestCase(TestName = "A factory result that already has an outcome shows the battle-over banner at startup")]
  public void LauncherShowsBannerForStartupTerminalBattle()
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
    var banner = battle.GetNode<BattleHud>("BattleUI/BattleHud").GetNode<Label>("%BattleOverBanner");
    Assert.True(banner.Visible);
    Assert.Equal("VICTORY", banner.Text);
    Assert.False(ReturnButton(battle).Visible);
    int returns = 0;
    battle.ReturnRequested += () => returns++;
    ReturnButton(battle).EmitSignal(Button.SignalName.Pressed);
    Assert.Equal(0, returns); // terminal standalone battles still cannot return to a campaign
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
