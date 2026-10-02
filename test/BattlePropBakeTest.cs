using FunProject.Battle;
using GdUnit4;
using System;
using Cell = Godot.Vector3I;

[TestSuite]
[RequireGodotRuntime]
public partial class BattlePropBakeTest
{
  [TestCase(true, 0, 3, 4)]
  [TestCase(false, 0, 3, 4)]
  [TestCase(true, 1, 4, 3)]
  [TestCase(true, 2, 3, 2)]
  [TestCase(true, 3, 2, 3)]
  [TestCase(true, -1, 2, 3)]
  [TestCase(true, 5, 4, 3)]
  public void FourIndependentSideStrengthsRotateWithTheThreeCellPropFootprint(bool blocksMovement, int turns, int secondX, int secondZ)
  {
    var cells = TestData.MakeOpenBattleMap(7, 7).Tiles;
    var prop = new BattleMapTileData
    {
      PropFootprint = [Cell.Zero, new(0, 0, 1), new(0, 0, 2)],
      PropBlocksMovement = blocksMovement,
      PropCoverNorth = 10,
      PropCoverEast = 20,
      PropCoverSouth = 30,
      PropCoverWest = 40
    };
    var anchor = new Cell(3, 0, 3);
    var baked = Bake(cells, (prop, anchor, turns));
    Assert.Equal(cells.Count, baked.Tiles.Count);
    Assert.True(baked.Tiles[new Cell(4, 0, 4)].Walkable);
    var step = new Cell(secondX, 0, secondZ) - anchor;
    Cell[] occupied = [anchor, anchor + step, anchor + step * 2];
    Cell[] offsets = [new(0, 0, -1), new(1, 0, 0), new(0, 0, 1), new(-1, 0, 0)];
    int[] strengths = [10, 20, 30, 40];
    foreach (var cell in occupied)
    {
      var tile = baked.Tiles[cell];
      Assert.Equal(!blocksMovement, tile.Walkable);
      Assert.False(tile.BlocksLineOfSight);
      Assert.True(cells[cell].Walkable);
      Assert.Equal(0, tile.CoverNorth + tile.CoverEast + tile.CoverSouth + tile.CoverWest);
      for (int side = 0; side < 4; side++)
      {
        var neighbor = cell + offsets[side];
        if (System.Array.IndexOf(occupied, neighbor) >= 0) continue;
        tile = baked.Tiles[neighbor];
        int facingCover = side switch { 0 => tile.CoverSouth, 1 => tile.CoverWest, 2 => tile.CoverNorth, _ => tile.CoverEast };
        Assert.Equal(strengths[((turns + side) % 4 + 4) % 4], facingCover);
        Assert.Equal(facingCover, tile.CoverNorth + tile.CoverEast + tile.CoverSouth + tile.CoverWest);
        Assert.Equal(0, cells[neighbor].CoverNorth + cells[neighbor].CoverEast + cells[neighbor].CoverSouth + cells[neighbor].CoverWest);
      }
    }
  }

  [TestCase(true, 2)]
  [TestCase(false, -1)]
  public void BakeClonesEveryFieldAndUsesMaximumCoordinatesForDimensions(bool walkable, int spawnSlot)
  {
    var brush = new BattleMapTileData
    {
      Walkable = walkable,
      BlocksLineOfSight = true,
      BlocksVerticalLineOfSight = true,
      GroundSurfaceOffset = 0.25f,
      SpawnFactionSlot = spawnSlot,
      CoverNorth = 10,
      CoverEast = 30,
      CoverSouth = 50,
      CoverWest = 70,
    };
    var baked = Bake(new() { [new(4, 2, 5)] = brush, [new(5, 2, 5)] = brush });
    BattleMapTileData first = baked.Tiles[new Cell(4, 2, 5)];
    BattleMapTileData second = baked.Tiles[new Cell(5, 2, 5)];

    Assert.Equal(new Cell(6, 3, 6), baked.Dimensions);
    Assert.False(ReferenceEquals(brush, first));
    Assert.False(ReferenceEquals(first, second));
    Assert.Equal(walkable, first.Walkable);
    Assert.True(first.BlocksLineOfSight);
    Assert.True(first.BlocksVerticalLineOfSight);
    Assert.Equal(0.25f, first.GroundSurfaceOffset);
    Assert.Equal(spawnSlot, first.SpawnFactionSlot);
    Assert.Equal(10, first.CoverNorth);
    Assert.Equal(30, first.CoverEast);
    Assert.Equal(50, first.CoverSouth);
    Assert.Equal(70, first.CoverWest);
  }

  [TestCase]
  public void CoverMergesPerSideWithoutReducingExistingCover()
  {
    var cells = TestData.MakeOpenBattleMap(3, 3).Tiles;
    cells[new Cell(1, 0, 1)].CoverNorth = 80;
    cells[new Cell(1, 0, 1)].CoverEast = 20;
    var eastEdge = new BattleMapTileData { PropFootprint = [Cell.Zero], PropCoverEast = 45 };
    var southEdge = new BattleMapTileData { PropFootprint = [Cell.Zero], PropCoverSouth = 55 };

    BattleMapTileData tile = Bake(cells,
      (eastEdge, new Cell(0, 0, 1), 0), (southEdge, new Cell(1, 0, 0), 0)).Tiles[new Cell(1, 0, 1)];

    Assert.Equal(80, tile.CoverNorth);
    Assert.Equal(20, tile.CoverEast);
    Assert.Equal(45, tile.CoverWest);
    Assert.Equal(0, tile.CoverSouth);
  }

  [TestCase(false)]
  [TestCase(true)]
  public void CoverSkipsMissingAndMovementBlockedNeighborsRegardlessOfOrder(bool blockerFirst)
  {
    var cells = TestData.MakeOpenBattleMap(2, 1).Tiles;
    var cover = new BattleMapTileData { PropFootprint = [Cell.Zero], PropCoverNorth = 50, PropCoverEast = 50 };
    var blocker = new BattleMapTileData { PropFootprint = [Cell.Zero], PropBlocksMovement = true };
    var props = new[] { (cover, Cell.Zero, 0), (blocker, new Cell(1, 0, 0), 0) };
    if (blockerFirst) System.Array.Reverse(props);

    var baked = Bake(cells, props);

    Assert.Equal(2, baked.Tiles.Count);
    Assert.Equal(new Cell(2, 1, 1), baked.Dimensions);
    Assert.Equal(0, baked.Tiles[new Cell(1, 0, 0)].CoverWest);
  }

  [TestCase(false)]
  [TestCase(true)]
  public void OnlyMovementBlockingFootprintsCannotOverlap(bool blocksMovement)
  {
    var cells = TestData.MakeOpenBattleMap(3, 1).Tiles;
    var solid = new BattleMapTileData { PropFootprint = [Cell.Zero, new(1, 0, 0)], PropBlocksMovement = true };
    var overlapping = new BattleMapTileData { PropFootprint = [Cell.Zero, new(-1, 0, 0)], PropBlocksMovement = blocksMovement };
    var props = new[] { (solid, Cell.Zero, 0), (overlapping, new Cell(1, 0, 0), 0) };

    if (blocksMovement) Assert.Throws<InvalidOperationException>(() => Bake(cells, props));
    else Assert.False(Bake(cells, props).Tiles[Cell.Zero].Walkable);
    Assert.True(cells[Cell.Zero].Walkable);
  }

  [TestCase(false, true, 0f, 0, TestName = "Every footprint cell needs ground")]
  [TestCase(true, false, 0f, 0, TestName = "Ground must be walkable before stamping")]
  [TestCase(true, true, 0.5f, 0, TestName = "Ground surfaces must be level")]
  [TestCase(true, true, 0f, 1, TestName = "Rotated footprints cannot leave the board")]
  public void InvalidSupportIsRejected(bool hasSecondCell, bool walkable, float surfaceOffset, int turns)
  {
    var cells = TestData.MakeOpenBattleMap(2, 1).Tiles;
    if (!hasSecondCell) cells.Remove(new Cell(1, 0, 0));
    else
    {
      cells[new Cell(1, 0, 0)].Walkable = walkable;
      cells[new Cell(1, 0, 0)].GroundSurfaceOffset = surfaceOffset;
    }
    var prop = new BattleMapTileData { PropFootprint = [Cell.Zero, new(1, 0, 0)] };

    Assert.Throws<InvalidOperationException>(() => Bake(cells, (prop, Cell.Zero, turns)));
  }

  [TestCase(-1, true, -1, TestName = "Negative ground coordinates are invalid")]
  [TestCase(0, false, 0, TestName = "Spawn terrain must be walkable even without props")]
  public void InvalidGroundIsRejectedWithoutProps(int x, bool walkable, int spawnSlot)
  {
    var tile = new BattleMapTileData { Walkable = walkable, SpawnFactionSlot = spawnSlot };
    Assert.Throws<InvalidOperationException>(() => Bake(new() { [new(x, 0, 0)] = tile }));
  }

  [TestCase(false)]
  [TestCase(true)]
  public void SpawnsAllowSightBlockingDecorationButNotMovementBlockers(bool blocksMovement)
  {
    var cells = TestData.MakeMapData(new(1, 1, 1), (Vector3I.Zero, TestData.SpawnTile(0))).Tiles;
    var prop = new BattleMapTileData { PropFootprint = [Cell.Zero], PropBlocksMovement = blocksMovement, BlocksLineOfSight = true };
    if (blocksMovement) Assert.Throws<InvalidOperationException>(() => Bake(cells, (prop, Cell.Zero, 0)));
    else
    {
      var tile = Bake(cells, (prop, Cell.Zero, 0)).Tiles[Cell.Zero];
      Assert.True(tile.Walkable);
      Assert.True(tile.BlocksLineOfSight);
      Assert.False(tile.BlocksVerticalLineOfSight);
      Assert.Equal(0, tile.SpawnFactionSlot);
    }
    Assert.True(cells[Cell.Zero].Walkable);
    Assert.False(cells[Cell.Zero].BlocksLineOfSight);
  }

  [TestCase(0, TestName = "An empty footprint is invalid")]
  [TestCase(1, TestName = "A footprint without its anchor is invalid")]
  [TestCase(2, TestName = "A duplicate footprint cell is invalid")]
  [TestCase(3, TestName = "A footprint spanning Y levels is invalid")]
  public void InvalidPropFootprintIsRejected(int scenario)
  {
    var prop = new BattleMapTileData
    {
      PropFootprint = scenario switch
      {
        0 => [],
        1 => [new(1, 0, 0)],
        2 => [Cell.Zero, Cell.Zero],
        _ => [Cell.Zero, new(0, 1, 0)],
      },
    };
    Assert.Throws<InvalidOperationException>(() => Bake(TestData.MakeOpenBattleMap().Tiles, (prop, Cell.Zero, 0)));
  }

  [TestCase(-1)]
  [TestCase(101)]
  public void InvalidCoverStrengthIsRejected(int strength)
  {
    var prop = new BattleMapTileData { PropFootprint = [Cell.Zero], PropCoverNorth = strength };
    Assert.Throws<InvalidOperationException>(() => Bake(TestData.MakeOpenBattleMap().Tiles, (prop, Cell.Zero, 0)));
  }

  private static BattleMapData Bake(Godot.Collections.Dictionary<Cell, BattleMapTileData> cells,
    params (BattleMapTileData Prop, Cell Anchor, int Turns)[] props)
  {
    var map = GdUnit4.Assertions.AutoFree(TestData.MakeMapAuthoring(cells))!;
    foreach (var (source, anchor, turns) in props)
      TestData.PaintProp(map, source, anchor, turns);
    using var scene = map.BuildScene();
    return GdUnit4.Assertions.AutoFree(scene.Instantiate<BattleMap>())!.MapData;
  }
}
