using FunProject.Battle;
using GdUnit4;
using System;
using Cell = Godot.Vector3I;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleMapBakerTest
{
  [TestCase(0, 1, 0, TestName = "No quarter turn preserves an east footprint cell")]
  [TestCase(1, 0, -1, TestName = "Positive Y quarter turn rotates east toward north")]
  [TestCase(2, -1, 0, TestName = "Two quarter turns rotate east toward west")]
  [TestCase(3, 0, 1, TestName = "Three quarter turns rotate east toward south")]
  [TestCase(-1, 0, 1, TestName = "Negative quarter turns normalize")]
  [TestCase(5, 0, -1, TestName = "Quarter turns wrap after a full rotation")]
  public void RotateCellUsesGodotPositiveYConvention(int turns, int x, int z)
  {
    Assert.Equal(new Cell(x, 2, z), BattleMapBaker.Rotate(new Cell(1, 2, 0), turns));
  }

  [TestCase(0, CoverDirections.East)]
  [TestCase(1, CoverDirections.North)]
  [TestCase(2, CoverDirections.West)]
  [TestCase(3, CoverDirections.South)]
  public void RotateCoverUsesTheSameCompass(int turns, CoverDirections expected)
  {
    Assert.Equal(expected, BattleMapBaker.Rotate(CoverDirections.East, turns));
  }

  [TestCase(TestName = "A three-cell prop rotates and stamps movement separately from sight")]
  public void RotatedFootprintBlocksEveryOccupiedCellWithoutInventingGround()
  {
    var cells = TestData.MakeOpenBattleMap(5, 5).Tiles;
    var prop = new BattlePropData
    {
      Footprint = [Cell.Zero, new(1, 0, 0), new(2, 0, 0)],
      BlocksMovement = true,
    };

    BattleMapData baked = BattleMapBaker.Bake(cells, [new(prop, new Cell(2, 0, 3), 1)]);

    Assert.Equal(cells.Count, baked.Tiles.Count);
    for (int z = 1; z <= 3; z++)
    {
      Assert.False(baked.Tiles[new Cell(2, 0, z)].Walkable);
      Assert.False(baked.Tiles[new Cell(2, 0, z)].BlocksLineOfSight);
      Assert.True(cells[new Cell(2, 0, z)].Walkable);
    }
    Assert.True(baked.Tiles[new Cell(3, 0, 3)].Walkable);
  }

  [TestCase(TestName = "Sight-only props leave movement and vertical sight independent")]
  public void SightOnlyPropDoesNotBlockMovementOrVerticalSight()
  {
    var cells = TestData.MakeOpenBattleMap().Tiles;
    var prop = new BattlePropData { Footprint = [Cell.Zero], BlocksLineOfSight = true };

    BattleMapTileData tile = BattleMapBaker.Bake(cells, [new(prop, Cell.Zero, 0)]).Tiles[Cell.Zero];

    Assert.True(tile.Walkable);
    Assert.True(tile.BlocksLineOfSight);
    Assert.False(tile.BlocksVerticalLineOfSight);
    Assert.False(cells[Cell.Zero].BlocksLineOfSight);
  }

  [TestCase(TestName = "Every baked cell is fresh and dimensions include the empty origin region")]
  public void BakeClonesCellsAndUsesMaximumCoordinatesForDimensions()
  {
    var brush = new BattleMapTileData
    {
      BlocksLineOfSight = true,
      BlocksVerticalLineOfSight = true,
      GroundSurfaceOffset = 0.25f,
      SpawnFactionSlot = 2,
      CoverNorth = 30,
      CoverEast = 70,
    };
    var cells = TestData.MakeMapData(new Vector3I(1, 1, 1),
      (new Vector3I(4, 2, 5), brush), (new Vector3I(5, 2, 5), brush)).Tiles;

    BattleMapData baked = BattleMapBaker.Bake(cells, []);
    BattleMapTileData first = baked.Tiles[new Cell(4, 2, 5)];
    BattleMapTileData second = baked.Tiles[new Cell(5, 2, 5)];

    Assert.Equal(new Cell(6, 3, 6), baked.Dimensions);
    Assert.False(ReferenceEquals(brush, first));
    Assert.False(ReferenceEquals(first, second));
    Assert.True(first.Walkable);
    Assert.True(first.BlocksLineOfSight);
    Assert.True(first.BlocksVerticalLineOfSight);
    Assert.Equal(0.25f, first.GroundSurfaceOffset);
    Assert.Equal(2, first.SpawnFactionSlot);
    Assert.Equal(30, first.CoverNorth);
    Assert.Equal(70, first.CoverEast);
  }

  [TestCase(TestName = "A cover edge protects the adjacent standing cell facing the prop")]
  public void CoverGoesOutsideTheFootprintAndRotatesWithIt()
  {
    var cells = TestData.MakeOpenBattleMap(5, 5).Tiles;
    var prop = new BattlePropData
    {
      Footprint = [Cell.Zero, new(1, 0, 0)],
      BlocksMovement = true,
      CoverEdges = [new() { Cell = new Cell(1, 0, 0), Direction = CoverDirections.East, Amount = 60 }],
    };

    BattleMapData baked = BattleMapBaker.Bake(cells, [new(prop, new Cell(2, 0, 3), 1)]);

    Assert.Equal(60, baked.Tiles[new Cell(2, 0, 1)].CoverSouth);
    Assert.Equal(0, baked.Tiles[new Cell(2, 0, 1)].CoverNorth);
    Assert.Equal(0, baked.Tiles[new Cell(2, 0, 2)].CoverSouth);
    Assert.Equal(0, cells[new Cell(2, 0, 1)].CoverSouth);
  }

  [TestCase(TestName = "Cover contributions merge by the maximum on each independent side")]
  public void CoverMergesPerSideWithoutReducingExistingCover()
  {
    var cells = TestData.MakeOpenBattleMap(3, 3).Tiles;
    cells[new Cell(1, 0, 1)].CoverNorth = 80;
    cells[new Cell(1, 0, 1)].CoverEast = 20;
    var eastEdge = new BattlePropData
    {
      Footprint = [Cell.Zero],
      CoverEdges = [new() { Direction = CoverDirections.East, Amount = 45 }],
    };
    var southEdge = new BattlePropData
    {
      Footprint = [Cell.Zero],
      CoverEdges = [new() { Direction = CoverDirections.South, Amount = 55 }],
    };

    BattleMapTileData tile = BattleMapBaker.Bake(cells,
      [new(eastEdge, new Cell(0, 0, 1), 0), new(southEdge, new Cell(1, 0, 0), 0)]).Tiles[new Cell(1, 0, 1)];

    Assert.Equal(80, tile.CoverNorth);
    Assert.Equal(20, tile.CoverEast);
    Assert.Equal(45, tile.CoverWest);
    Assert.Equal(0, tile.CoverSouth);
  }

  [TestCase(TestName = "Cover never creates a missing neighboring cell")]
  public void CoverDoesNotCreateGroundOutsideTheMap()
  {
    var cells = TestData.MakeMapData(new Vector3I(1, 1, 1), (Vector3I.Zero, TestData.FloorTile())).Tiles;
    var prop = new BattlePropData
    {
      Footprint = [Cell.Zero],
      CoverEdges = [new() { Direction = CoverDirections.North, Amount = 50 }],
    };

    BattleMapData baked = BattleMapBaker.Bake(cells, [new(prop, Cell.Zero, 0)]);

    Assert.Equal(1, baked.Tiles.Count);
    Assert.Equal(new Cell(1, 1, 1), baked.Dimensions);
  }

  [TestCase(TestName = "Cover skips cells occupied by another prop regardless of input order")]
  public void CoverSkipsAllMovementBlockedStandingCells()
  {
    var cells = TestData.MakeOpenBattleMap().Tiles;
    var cover = new BattlePropData
    {
      Footprint = [Cell.Zero],
      CoverEdges = [new() { Direction = CoverDirections.East, Amount = 50 }],
    };
    var blocker = new BattlePropData { Footprint = [Cell.Zero], BlocksMovement = true };

    BattleMapData baked = BattleMapBaker.Bake(cells,
      [new(cover, Cell.Zero, 0), new(blocker, new Cell(1, 0, 0), 0)]);

    Assert.Equal(0, baked.Tiles[new Cell(1, 0, 0)].CoverWest);
  }

  [TestCase(TestName = "Multiple nonblocking decorations can share a solid prop footprint")]
  public void NonblockingDecorationOverlapIsAllowed()
  {
    var cells = TestData.MakeOpenBattleMap().Tiles;
    var solid = new BattlePropData { Footprint = [Cell.Zero], BlocksMovement = true };
    var decoration = new BattlePropData { Footprint = [Cell.Zero] };

    BattleMapData baked = BattleMapBaker.Bake(cells,
      [new(solid, Cell.Zero, 0), new(decoration, Cell.Zero, 0), new(decoration, Cell.Zero, 0)]);

    Assert.False(baked.Tiles[Cell.Zero].Walkable);
  }

  [TestCase(TestName = "Two solid footprints cannot overlap")]
  public void OverlappingMovementBlockersAreRejected()
  {
    var cells = TestData.MakeOpenBattleMap().Tiles;
    var prop = new BattlePropData { Footprint = [Cell.Zero], BlocksMovement = true };

    Assert.Throws<InvalidOperationException>(() => BattleMapBaker.Bake(cells,
      [new(prop, Cell.Zero, 0), new(prop, Cell.Zero, 0)]));
    Assert.True(cells[Cell.Zero].Walkable);
  }

  [TestCase(TestName = "Footprints require existing ground for every cell")]
  public void MissingSupportIsRejected()
  {
    var cells = TestData.MakeMapData(new Vector3I(2, 1, 1), (Vector3I.Zero, TestData.FloorTile())).Tiles;
    var prop = new BattlePropData { Footprint = [Cell.Zero, new(1, 0, 0)] };

    Assert.Throws<InvalidOperationException>(() => BattleMapBaker.Bake(cells, [new(prop, Cell.Zero, 0)]));
  }

  [TestCase(TestName = "Footprints require walkable ground before prop stamping")]
  public void NonwalkableSupportIsRejected()
  {
    var cells = TestData.MakeMapData(new Vector3I(1, 1, 1), (Vector3I.Zero, TestData.WallTile())).Tiles;
    var prop = new BattlePropData { Footprint = [Cell.Zero] };

    Assert.Throws<InvalidOperationException>(() => BattleMapBaker.Bake(cells, [new(prop, Cell.Zero, 0)]));
  }

  [TestCase(TestName = "A prop cannot straddle mismatched authored ground surfaces")]
  public void UnequalSupportSurfacesAreRejected()
  {
    var cells = TestData.MakeOpenBattleMap().Tiles;
    cells[new Cell(1, 0, 0)].GroundSurfaceOffset = 0.5f;
    var prop = new BattlePropData { Footprint = [Cell.Zero, new(1, 0, 0)] };

    Assert.Throws<InvalidOperationException>(() => BattleMapBaker.Bake(cells, [new(prop, Cell.Zero, 0)]));
  }

  [TestCase(TestName = "A rotated footprint cannot leave the nonnegative board")]
  public void NegativeOccupiedCoordinatesAreRejected()
  {
    var cells = TestData.MakeOpenBattleMap().Tiles;
    var prop = new BattlePropData { Footprint = [Cell.Zero, new(1, 0, 0)] };

    Assert.Throws<InvalidOperationException>(() => BattleMapBaker.Bake(cells, [new(prop, Cell.Zero, 1)]));
  }

  [TestCase(TestName = "Base cells cannot have negative coordinates")]
  public void NegativeGroundCoordinatesAreRejected()
  {
    var cells = TestData.MakeMapData(new Vector3I(1, 1, 1), (new Vector3I(-1, 0, 0), TestData.FloorTile())).Tiles;

    Assert.Throws<InvalidOperationException>(() => BattleMapBaker.Bake(cells, []));
  }

  [TestCase(TestName = "Solid props cannot block tagged spawn cells")]
  public void MovementBlockedSpawnIsRejected()
  {
    var cells = TestData.MakeMapData(new Vector3I(1, 1, 1), (Vector3I.Zero, TestData.SpawnTile(0))).Tiles;
    var prop = new BattlePropData { Footprint = [Cell.Zero], BlocksMovement = true };

    Assert.Throws<InvalidOperationException>(() => BattleMapBaker.Bake(cells, [new(prop, Cell.Zero, 0)]));
    Assert.True(cells[Cell.Zero].Walkable);
  }

  [TestCase(TestName = "Decorative and sight-blocking props can occupy spawns")]
  public void NonblockingSpawnDecorationIsAllowed()
  {
    var cells = TestData.MakeMapData(new Vector3I(1, 1, 1), (Vector3I.Zero, TestData.SpawnTile(0))).Tiles;
    var prop = new BattlePropData { Footprint = [Cell.Zero], BlocksLineOfSight = true };

    BattleMapTileData tile = BattleMapBaker.Bake(cells, [new(prop, Cell.Zero, 0)]).Tiles[Cell.Zero];

    Assert.True(tile.Walkable);
    Assert.Equal(0, tile.SpawnFactionSlot);
  }

  [TestCase(0, TestName = "An empty footprint is invalid")]
  [TestCase(1, TestName = "A footprint without its anchor is invalid")]
  [TestCase(2, TestName = "A duplicate footprint cell is invalid")]
  [TestCase(3, TestName = "A footprint spanning Y levels is invalid")]
  public void InvalidFootprintIsRejected(int scenario)
  {
    var cells = TestData.MakeOpenBattleMap().Tiles;
    var prop = new BattlePropData
    {
      Footprint = scenario switch
      {
        0 => [],
        1 => [new(1, 0, 0)],
        2 => [Cell.Zero, Cell.Zero],
        _ => [Cell.Zero, new(0, 1, 0)],
      },
    };

    Assert.Throws<InvalidOperationException>(() => BattleMapBaker.Bake(cells, [new(prop, Cell.Zero, 0)]));
  }

  [TestCase(0, TestName = "A cover edge must originate inside its footprint")]
  [TestCase(1, TestName = "A cover edge cannot face an internal footprint neighbor")]
  [TestCase(2, TestName = "A cover edge must select one cardinal direction")]
  [TestCase(3, TestName = "A cover edge cannot omit its direction")]
  [TestCase(4, TestName = "A cover amount cannot be negative")]
  [TestCase(5, TestName = "A cover amount cannot exceed one hundred")]
  public void InvalidCoverEdgeIsRejected(int scenario)
  {
    var cells = TestData.MakeOpenBattleMap().Tiles;
    var edge = new BattlePropCoverEdgeData
    {
      Cell = scenario == 0 ? new Cell(2, 0, 0) : Cell.Zero,
      Direction = scenario switch
      {
        1 => CoverDirections.East,
        2 => CoverDirections.North | CoverDirections.West,
        3 => CoverDirections.None,
        _ => CoverDirections.North,
      },
      Amount = scenario switch { 4 => -1, 5 => 101, _ => 50 },
    };
    var prop = new BattlePropData { Footprint = [Cell.Zero, new(1, 0, 0)], CoverEdges = [edge] };

    Assert.Throws<InvalidOperationException>(() => BattleMapBaker.Bake(cells, [new(prop, new Cell(1, 0, 1), 0)]));
  }
}
