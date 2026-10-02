using FunProject.Battle;
using GdUnit4;
using Godot;
using static FunProject.Tests.GeoscapeTestScenes;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleMapPresentationTest
{
  [TestCase]
  public void ScaledTranslatedGridDrivesUnitAndHighlightPlacement()
  {
    using var battle = BattleFixture.Solo(new Vector3I(5, 2, 5), new Vector3I(2, 1, 3));
    var grid = new BoardCoordinates(new BattleMapData { GridOrigin = new(10, 2, 20), CellWidth = 2, LevelHeight = 3 }, Transform3D.Identity);
    var director = AddToTree(new EventPlaybackDirector { Coordinates = grid });
    var units = AddToTree(new BattleUnitMeshes { Coordinates = grid });
    units.Initialize(battle.Runtime, director, battle.PlayerFaction);
    var unit = units.GetChild<MeshInstance3D>(0);
    Assert.Equal(new Vector3(15, 6.5f, 27), unit.GlobalPosition);
    Assert.Equal(3f, ((CapsuleMesh)unit.Mesh).Height);
    var highlights = AddToTree(new ReachableTileHighlighter { Coordinates = grid });
    highlights.Show([new Vector3I(2, 1, 3)]);
    var marker = highlights.GetChild<MeshInstance3D>(0);
    Assert.True(marker.GlobalPosition.IsEqualApprox(new(15, 5.06f, 27)));
    Assert.Equal(Vector2.One * 1.8f, ((PlaneMesh)marker.Mesh).Size);
    Assert.Equal(new Vector3I(2, 1, 3), grid.WorldToTile(marker.GlobalPosition));
  }

  [TestCase]
  public void ExistingVerticalStepsStillConnectStackedWalkableFloors()
  {
    var board = new BattleBoardState(TestData.MakeMapData(new(1, 2, 1),
      (new(0, 0, 0), TestData.FloorTile()), (new(0, 1, 0), TestData.FloorTile())));
    Assert.Equal(2, board.FindPath(board.At(0, 0, 0), board.At(0, 1, 0)).Length);
  }
  [TestCase]
  public void BattleSceneScalesCameraOrbitAndMovementRibbonWithGridWidth()
  {
    using var battle = BattleFixture.UiBattle();
    var map = TestData.MakeOpenBattleMap(10, 10);
    map.CellWidth = 2;
    using var scene = TestData.MakeMapScene(map);
    var host = GD.Load<PackedScene>("res://scenes/battle/BattleScene.tscn").Instantiate<BattleScene>();
    host.Present(battle.Runtime, new BattleSetup(map, [], 1, Some(battle.PlayerFaction)) { MapScene = Some(scene) });
    AddToTree(host);
    var rig = host.GetNode<GameCamera>("%GameCamera");
    Assert.Equal(new Vector3(24, 0, 0), rig.GetNode<Path3D>("CameraPath").Curve.GetPointPosition(0));
    var view = host.GetNode<BattleUI>("%BattleUI");
    view.ShowPreview(new PathPreview([new(0, 0, 0), new(1, 0, 0)]));
    var line = view.GetNode<MovementLine>("MovementLine");
    Assert.True(Mathf.IsEqualApprox(0.01f, line.Width));
    Assert.True(Mathf.IsEqualApprox(0.5f, line.CornerRadius));
  }

}
