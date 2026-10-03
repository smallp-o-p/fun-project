using FunProject.Battle;
using GdUnit4;
using Godot;
using System;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleMapAuthoringTest
{
  [TestCase]
  public void SeparateFloorAndVolumeContributionsBakeRoofAboveSolidCell()
  {
    var footprint = new Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData>
    {
      [Godot.Vector3I.Zero] = new BattleFootprintData { HasFloor = true, BlocksMovement = true, BlocksLineOfSight = true, WalkableTop = true, TopBlocksVerticalLineOfSight = true },
      [new(1, 1, 0)] = new BattleFootprintData { BlocksLineOfSight = true }
    };
    var map = BattleMapAuthoring.BuildMap([(footprint, Transform3D.Identity)]);
    Assert.False(map.Tiles[Godot.Vector3I.Zero].Walkable);
    Assert.True(map.Tiles[new(0, 1, 0)].Walkable);
    Assert.True(map.Tiles[new(0, 1, 0)].BlocksVerticalLineOfSight);
    Assert.False(map.Tiles[Godot.Vector3I.Zero].BlocksVerticalLineOfSight);
    Assert.True(map.Tiles[new(1, 1, 0)].BlocksLineOfSight);
    Assert.False(map.Tiles[new(1, 1, 0)].Walkable);
    Assert.Equal(new Godot.Vector3I(2, 2, 1), map.Dimensions);
    Assert.True(footprint[Godot.Vector3I.Zero].HasFloor);
  }

  [TestCase(0)]
  [TestCase(1)]
  [TestCase(2)]
  [TestCase(3)]
  public void RotatedTwoCellObjectContributesOnlyExternalCover(int turn)
  {
    var floors = TestData.MakeFloorFootprint(7, 7);
    var prop = new Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData>
    {
      [Godot.Vector3I.Zero] = new() { BlocksMovement = true, CoverDirections = CoverDirections.North | CoverDirections.East | CoverDirections.South | CoverDirections.West, CoverAmount = 40 },
      [new(0, 0, 1)] = new() { BlocksMovement = true, CoverDirections = CoverDirections.North | CoverDirections.East | CoverDirections.South | CoverDirections.West, CoverAmount = 40 }
    };
    var transform = new Transform3D(new Basis(Vector3.Up, turn * Mathf.Pi / 2), new(3, 0, 3));
    var map = BattleMapAuthoring.BuildMap([(floors, Transform3D.Identity), (prop, transform)]);
    int blocked = 0, covered = 0;
    foreach (var (_, cell) in map.Tiles)
    {
      if (!cell.Walkable) { blocked++; Assert.Equal(0, cell.CoverAmount); }
      if (cell.CoverAmount > 0) { covered++; Assert.Equal(40, cell.CoverAmount); }
    }
    Assert.Equal(2, blocked);
    Assert.Equal(6, covered);
  }

  [TestCase(0)]
  [TestCase(1)]
  [TestCase(2)]
  public void InvalidAuthoredClaimsAreRejected(int scenario)
  {
    var cell = new BattleFootprintData { HasFloor = true };
    var piece = new Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData> { [Godot.Vector3I.Zero] = cell };
    var transform = Transform3D.Identity;
    if (scenario == 0) transform.Origin = new(0.1f, 0, 0);
    if (scenario == 1) transform.Basis = Basis.FromScale(new(2, 1, 1));
    if (scenario == 2) transform.Origin = Vector3.Left;
    Assert.Throws<InvalidOperationException>(() => BattleMapAuthoring.BuildMap([(piece, transform)]));
  }

  [TestCase(false, 0)]
  [TestCase(true, 0)]
  [TestCase(false, 1)]
  [TestCase(false, 2)]
  public void CoverMergesSameSideButRejectsDifferentStrengthsAcrossWalkableSides(bool sameSide, int receiver)
  {
    var floors = TestData.MakeFloorFootprint(3, 3);
    if (receiver == 1) floors[new(1, 0, 1)].BlocksMovement = true;
    if (receiver == 2) floors.Remove(new(1, 0, 1));
    var west = new Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData> { [Godot.Vector3I.Zero] = new() { CoverDirections = CoverDirections.East, CoverAmount = 20 } };
    var other = new Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData> { [Godot.Vector3I.Zero] = new() { CoverDirections = sameSide ? CoverDirections.East : CoverDirections.South, CoverAmount = 60 } };
    (Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData>, Transform3D)[] pieces = [(west, new(Basis.Identity, new(0, 0, 1))),
      (other, new(Basis.Identity, sameSide ? new(0, 0, 1) : new(1, 0, 0))), (floors, Transform3D.Identity)];
    if (receiver != 0)
    {
      foreach (var tile in BattleMapAuthoring.BuildMap(pieces).Tiles.Values) Assert.Equal(0, tile.CoverAmount);
    }
    else if (!sameSide) Assert.Throws<InvalidOperationException>(() => BattleMapAuthoring.BuildMap(pieces));
    else
    {
      var tile = BattleMapAuthoring.BuildMap(pieces).Tiles[new(1, 0, 1)];
      Assert.Equal(CoverDirections.West, tile.CoverDirections);
      Assert.Equal(60, tile.CoverAmount);
    }
  }

  [TestCase]
  public void DuplicateFloorsAreRejected()
  {
    var floor = TestData.MakeFloorFootprint(1, 1);
    Assert.Throws<InvalidOperationException>(() => BattleMapAuthoring.BuildMap([(floor, Transform3D.Identity), (floor, Transform3D.Identity)]));
  }
}
