using FunProject.Battle;
using GdUnit4;
using Godot;
using System;
using static GdUnit4.Assertions;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleMapGridMapTest
{
  [TestCase]
  public void GroundPropReplacesEveryFloorFlagAndKeepsStandingSurface()
  {
    var map = AutoFree(TestData.MakeMapAuthoring())!;
    var floor = TestData.MakeFloorFootprint(1, 1);
    floor[Godot.Vector3I.Zero].BlocksMovement = true;
    floor[Godot.Vector3I.Zero].BlocksLineOfSight = true;
    floor[Godot.Vector3I.Zero].BlocksVerticalLineOfSight = true;
    floor[Godot.Vector3I.Zero].CoverDirections = CoverDirections.North;
    floor[Godot.Vector3I.Zero].CoverAmount = 60;
    Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData> prop = new() { [Godot.Vector3I.Zero] = new() };
    map.Floors.SetCellItem(Godot.Vector3I.Zero, 0);
    map.Props.SetCellItem(Godot.Vector3I.Zero, 1);
    var data = map.BuildData(new() { [0] = floor, [1] = prop });
    var tile = data.Tiles[Godot.Vector3I.Zero];
    Assert.True(tile.Walkable);
    Assert.False(tile.BlocksLineOfSight);
    Assert.False(tile.BlocksVerticalLineOfSight);
    Assert.Equal(CoverDirections.None, tile.CoverDirections);
    Assert.Equal(0, tile.CoverAmount);
    Assert.False(prop[Godot.Vector3I.Zero].HasFloor);
    Assert.True(floor[Godot.Vector3I.Zero].BlocksMovement);
  }

  [TestCase(0)]
  [TestCase(1)]
  [TestCase(2)]
  [TestCase(3)]
  public void RotatedMultiCellPropsKeepAnchorAndExternalCover(int turn)
  {
    var map = AutoFree(TestData.MakeMapAuthoring())!;
    Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData> prop = new()
    {
      [Godot.Vector3I.Zero] = new() { BlocksMovement = true, CoverDirections = (CoverDirections)15, CoverAmount = 40 },
      [new(0, 0, 1)] = new() { BlocksMovement = true, CoverDirections = (CoverDirections)15, CoverAmount = 40 }
    };
    map.Floors.SetCellItem(Godot.Vector3I.Zero, 0);
    var anchor = new Godot.Vector3I(3, 0, 3);
    var basis = new Basis(Vector3.Up, turn * Mathf.Pi / 2);
    map.Props.SetCellItem(anchor, 1, map.Props.GetOrthogonalIndexFromBasis(basis));
    var data = map.BuildData(new() { [0] = TestData.MakeFloorFootprint(7, 7), [1] = prop });
    Assert.False(data.Tiles[anchor].Walkable);
    Assert.False(data.Tiles[anchor + (Godot.Vector3I)(basis * Vector3.Back).Round()].Walkable);
    Assert.Equal(2, data.Tiles.Values.AsValueEnumerable().Count(tile => !tile.Walkable));
    Assert.Equal(6, data.Tiles.Values.AsValueEnumerable().Count(tile => tile.CoverAmount == 40));
  }

  [TestCase]
  public void OnePropsStackedVolumeIsAllowedAndElevatedOnlyVolumeKeepsFloorFlags()
  {
    var map = AutoFree(TestData.MakeMapAuthoring())!;
    var floor = TestData.MakeFloorFootprint(2, 1);
    floor[new(1, 0, 0)].BlocksVerticalLineOfSight = true;
    Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData> prop = new()
    {
      [Godot.Vector3I.Zero] = new() { BlocksMovement = true },
      [new(0, 1, 0)] = new() { BlocksLineOfSight = true },
      [new(1, 2, 0)] = new() { BlocksLineOfSight = true }
    };
    map.Floors.SetCellItem(Godot.Vector3I.Zero, 0);
    map.Props.SetCellItem(Godot.Vector3I.Zero, 1);
    var data = map.BuildData(new() { [0] = floor, [1] = prop });
    Assert.False(data.Tiles[Godot.Vector3I.Zero].Walkable);
    Assert.True(data.Tiles[new(0, 1, 0)].BlocksLineOfSight);
    Assert.False(data.Tiles[new(0, 1, 0)].Walkable);
    Assert.True(data.Tiles[new(1, 0, 0)].Walkable);
    Assert.True(data.Tiles[new(1, 0, 0)].BlocksVerticalLineOfSight);
    Assert.Equal(new Godot.Vector3I(2, 3, 1), data.Dimensions);
  }

  [TestCase(false)]
  [TestCase(true)]
  public void EveryVolumeCellNeedsGroundSupportIncludingElevatedCells(bool elevated)
  {
    var map = AutoFree(TestData.MakeMapAuthoring())!;
    Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData> prop = new() { [new(1, elevated ? 1 : 0, 0)] = new() };
    map.Floors.SetCellItem(Godot.Vector3I.Zero, 0);
    map.Props.SetCellItem(Godot.Vector3I.Zero, 1);
    Assert.Throws<InvalidOperationException>(() => map.BuildData(new() { [0] = TestData.MakeFloorFootprint(1, 1), [1] = prop }));
  }

  [TestCase(false)]
  [TestCase(true)]
  public void DifferentPropsCannotClaimTheSameVolumeEvenWithFalseBlockingFlags(bool elevated)
  {
    var map = AutoFree(TestData.MakeMapAuthoring())!;
    map.Floors.SetCellItem(Godot.Vector3I.Zero, 0);
    map.Props.SetCellItem(Godot.Vector3I.Zero, 1);
    map.Props.SetCellItem(Godot.Vector3I.Right, 2);
    Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData> first = new() { [new(1, elevated ? 1 : 0, 0)] = new() };
    Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData> second = new() { [new(0, elevated ? 1 : 0, 0)] = new() };
    Assert.Throws<InvalidOperationException>(() => map.BuildData(new() { [0] = TestData.MakeFloorFootprint(2, 1), [1] = first, [2] = second }));
  }

  [TestCase(0)]
  [TestCase(1)]
  [TestCase(2)]
  [TestCase(3)]
  [TestCase(4)]
  public void FloorLayerRequiresNonemptyUniqueGroundFloorClaims(int scenario)
  {
    var map = AutoFree(TestData.MakeMapAuthoring())!;
    var floor = TestData.MakeFloorFootprint(2, 1);
    if (scenario == 0) floor.Clear();
    if (scenario == 1) floor[Godot.Vector3I.Zero].HasFloor = false;
    if (scenario == 2) floor[new(0, 1, 0)] = new() { HasFloor = true };
    if (scenario == 3) floor[Godot.Vector3I.Zero].WalkableTop = true;
    map.Floors.SetCellItem(Godot.Vector3I.Zero, 0);
    if (scenario == 4) map.Floors.SetCellItem(Godot.Vector3I.Right, 0);
    Assert.Throws<InvalidOperationException>(() => map.BuildData(new() { [0] = floor }));
  }

  [TestCase(0)]
  [TestCase(1)]
  [TestCase(2)]
  [TestCase(3)]
  [TestCase(4)]
  [TestCase(5)]
  [TestCase(6)]
  [TestCase(7)]
  public void NativeLayerSettingsAnchorsAndItemIdsAreValidated(int scenario)
  {
    var map = AutoFree(TestData.MakeMapAuthoring())!;
    var anchor = Godot.Vector3I.Zero;
    int item = 0, orientation = 0;
    if (scenario == 0) map.Floors.Position = Vector3.Right;
    if (scenario == 1) map.Floors.CellSize = Vector3.One * 2;
    if (scenario == 2) map.Floors.CellCenterY = false;
    if (scenario == 3) map.Floors.CellScale = 2;
    if (scenario == 4) anchor = Godot.Vector3I.Up;
    if (scenario == 5) anchor = Godot.Vector3I.Left;
    if (scenario == 6) item = 999;
    if (scenario == 7) orientation = map.Floors.GetOrthogonalIndexFromBasis(new Basis(Vector3.Right, Mathf.Pi / 2));
    map.Floors.SetCellItem(anchor, item, orientation);
    Assert.Throws<InvalidOperationException>(() => map.BuildData(new() { [0] = TestData.MakeFloorFootprint(1, 1) }));
  }

  [TestCase]
  public void DecorativeEmptyPropDoesNotAddGameplayCells()
  {
    var map = AutoFree(TestData.MakeMapAuthoring())!;
    map.Floors.SetCellItem(Godot.Vector3I.Zero, 0);
    map.Props.SetCellItem(new(10, 0, 10), 1);
    var data = map.BuildData(new() { [0] = TestData.MakeFloorFootprint(1, 1), [1] = [] });
    Assert.Equal(1, data.Tiles.Count);
    Assert.Equal(new Godot.Vector3I(1, 1, 1), data.Dimensions);
  }
}
