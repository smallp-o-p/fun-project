using FunProject.Battle;
using GdUnit4;
using Godot;
using System;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleMapAuthoringTest
{
  [TestCase]
  public void GroundFloorAndTallVolumeKeepIndependentSightFlags()
  {
    var footprint = new Godot.Collections.Dictionary<Godot.Vector3I, BattleFootprintData>
    {
      [Godot.Vector3I.Zero] = new BattleFootprintData { HasFloor = true, BlocksMovement = true, BlocksLineOfSight = true, TopBlocksVerticalLineOfSight = true },
      [new(1, 1, 0)] = new BattleFootprintData { BlocksLineOfSight = true }
    };
    var map = BattleMapAuthoring.BuildMap([(footprint, Transform3D.Identity)]);
    Assert.False(map.GetTile(Godot.Vector3I.Zero).Walkable);
    Assert.False(map.GetTile(new(0, 1, 0)).Walkable);
    Assert.True(map.GetTile(new(0, 1, 0)).BlocksVerticalLineOfSight);
    Assert.False(map.GetTile(Godot.Vector3I.Zero).BlocksVerticalLineOfSight);
    Assert.True(map.GetTile(new(1, 1, 0)).BlocksLineOfSight);
    Assert.False(map.GetTile(new(1, 1, 0)).Walkable);
    Assert.Equal(new Godot.Vector3I(2, 2, 1), map.Dimensions);
    Assert.True(footprint[Godot.Vector3I.Zero].HasFloor);
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
      foreach (var amount in BattleMapAuthoring.BuildMap(pieces).TileCoverAmounts) Assert.Equal(0, (int)amount);
    }
    else if (!sameSide) Assert.Throws<InvalidOperationException>(() => BattleMapAuthoring.BuildMap(pieces));
    else
    {
      var tile = BattleMapAuthoring.BuildMap(pieces).GetTile(new(1, 0, 1));
      Assert.Equal(CoverDirections.West, tile.CoverDirections);
      Assert.Equal(60, tile.CoverAmount);
    }
  }
}
