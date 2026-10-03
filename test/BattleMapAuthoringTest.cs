using FunProject.Battle;
using GdUnit4;
using Godot;
using System;
using Cell = Godot.Vector3I;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleMapAuthoringTest
{
  [TestCase]
  public void SeparateFloorAndVolumeContributionsBakeRoofAboveSolidCell()
  {
    var footprint = new BattleFootprintData
    {
      Cells = {
      [Cell.Zero] = new BattleFootprintCellData { HasFloor = true, BlocksMovement = true, BlocksLineOfSight = true, WalkableTop = true, TopBlocksVerticalLineOfSight = true },
      [new(1, 1, 0)] = new BattleFootprintCellData { BlocksLineOfSight = true } }
    };
    var map = BattleMapAuthoring.BuildMap([(footprint, Transform3D.Identity)], new());
    Assert.False(map.Tiles[Cell.Zero].Walkable);
    Assert.True(map.Tiles[new(0, 1, 0)].Walkable);
    Assert.True(map.Tiles[new(0, 1, 0)].BlocksVerticalLineOfSight);
    Assert.False(map.Tiles[Cell.Zero].BlocksVerticalLineOfSight);
    Assert.True(map.Tiles[new(1, 1, 0)].BlocksLineOfSight);
    Assert.False(map.Tiles[new(1, 1, 0)].Walkable);
    Assert.Equal(new Cell(2, 2, 1), map.Dimensions);
    Assert.True(footprint.Cells[Cell.Zero].HasFloor);
  }

  [TestCase(0)]
  [TestCase(1)]
  [TestCase(2)]
  [TestCase(3)]
  public void RotatedTwoCellObjectContributesOnlyExternalCover(int turn)
  {
    var floors = TestData.MakeFloorFootprint(7, 7);
    var prop = new BattleFootprintData
    {
      Cells = {
      [Cell.Zero] = new() { BlocksMovement = true, CoverDirections = CoverDirections.North | CoverDirections.East | CoverDirections.South | CoverDirections.West, CoverAmount = 40 },
      [new(0, 0, 1)] = new() { BlocksMovement = true, CoverDirections = CoverDirections.North | CoverDirections.East | CoverDirections.South | CoverDirections.West, CoverAmount = 40 } }
    };
    var transform = new Transform3D(new Basis(Vector3.Up, turn * Mathf.Pi / 2), new(3, 0, 3));
    var map = BattleMapAuthoring.BuildMap([(floors, Transform3D.Identity), (prop, transform)], new());
    int blocked = 0, covered = 0;
    foreach (var (_, cell) in map.Tiles)
    {
      if (!cell.Walkable) { blocked++; Assert.Equal(0, cell.CoverAmount); }
      if (cell.CoverAmount > 0) { covered++; Assert.Equal(40, cell.CoverAmount); }
    }
    Assert.Equal(2, blocked);
    Assert.Equal(6, covered);
  }

  [TestCase]
  public void NonzeroOriginAndDimensionsPreserveDistantCoordinates()
  {
    var metrics = new BattleMapData { GridOrigin = new(10, 2, 20), CellWidth = 2, LevelHeight = 3 };
    var floor = TestData.MakeFloorFootprint(1, 1);
    var result = BattleMapAuthoring.BuildMap([(floor, new(Basis.Identity, new(14, 5, 26)))], metrics);
    Assert.True(result.Tiles[new(2, 1, 3)].Walkable);
    Assert.Equal(new Cell(3, 2, 4), result.Dimensions);
  }

  [TestCase(0)]
  [TestCase(1)]
  [TestCase(2)]
  [TestCase(3)]
  public void InvalidAuthoredClaimsAreRejected(int scenario)
  {
    var cell = new BattleFootprintCellData { HasFloor = true };
    var piece = new BattleFootprintData { Cells = { [Cell.Zero] = cell } };
    var transform = Transform3D.Identity;
    if (scenario == 0) transform.Origin = new(0.1f, 0, 0);
    if (scenario == 1) transform.Basis = Basis.FromScale(new(2, 1, 1));
    if (scenario == 2) transform.Origin = Vector3.Left;
    if (scenario == 3) { cell.BlocksMovement = true; cell.SpawnFactionSlot = 0; }
    Assert.Throws<InvalidOperationException>(() => BattleMapAuthoring.BuildMap([(piece, transform)], new()));
  }

  [TestCase(false, 0)]
  [TestCase(true, 0)]
  [TestCase(false, 1)]
  [TestCase(false, 2)]
  public void CoverMergesSameSideButRejectsDifferentStrengthsAcrossWalkableSides(bool sameSide, int receiver)
  {
    var floors = TestData.MakeFloorFootprint(3, 3);
    if (receiver == 1) floors.Cells[new(1, 0, 1)].BlocksMovement = true;
    if (receiver == 2) floors.Cells.Remove(new(1, 0, 1));
    var west = new BattleFootprintData { Cells = { [Cell.Zero] = new() { CoverDirections = CoverDirections.East, CoverAmount = 20 } } };
    var other = new BattleFootprintData { Cells = { [Cell.Zero] = new() { CoverDirections = sameSide ? CoverDirections.East : CoverDirections.South, CoverAmount = 60 } } };
    (BattleFootprintData, Transform3D)[] pieces = [(west, new(Basis.Identity, new(0, 0, 1))),
      (other, new(Basis.Identity, sameSide ? new(0, 0, 1) : new(1, 0, 0))), (floors, Transform3D.Identity)];
    if (receiver != 0)
    {
      foreach (var tile in BattleMapAuthoring.BuildMap(pieces, new()).Tiles.Values) Assert.Equal(0, tile.CoverAmount);
    }
    else if (!sameSide) Assert.Throws<InvalidOperationException>(() => BattleMapAuthoring.BuildMap(pieces, new()));
    else
    {
      var tile = BattleMapAuthoring.BuildMap(pieces, new()).Tiles[new(1, 0, 1)];
      Assert.Equal(CoverDirections.West, tile.CoverDirections);
      Assert.Equal(60, tile.CoverAmount);
    }
  }

  [TestCase]
  public void DuplicateFloorsAreRejected()
  {
    var floor = TestData.MakeFloorFootprint(1, 1);
    Assert.Throws<InvalidOperationException>(() => BattleMapAuthoring.BuildMap([(floor, Transform3D.Identity), (floor, Transform3D.Identity)], new()));
  }
}
