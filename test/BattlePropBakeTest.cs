using FunProject.Battle;
using GdUnit4;
using System;
using Cell = Godot.Vector3I;

[TestSuite]
[RequireGodotRuntime]
public partial class BattlePropBakeTest
{
  [TestCase(true, 0, 2, 3)]
  [TestCase(false, 0, 2, 3)]
  [TestCase(true, 1, 3, 2)]
  [TestCase(true, 2, 2, 1)]
  [TestCase(true, 3, 1, 2)]
  [TestCase(true, -1, 1, 2)]
  [TestCase(true, 5, 3, 2)]
  public void FourIndependentSideStrengthsRotateWithTheTwoCellFootprint(bool blocksMovement, int turns, int secondX, int secondZ)
  {
    var prop = new BattlePropAuthoring
    {
      Footprint = [Cell.Zero, new(0, 0, 1)],
      BlocksMovement = blocksMovement,
      CoverNorth = 10,
      CoverEast = 20,
      CoverSouth = 30,
      CoverWest = 40
    };
    var baked = Bake(TestData.MakeOpenBattleMap(5, 5).Tiles, [new(prop, new Cell(2, 0, 2), turns)]);
    Cell[] occupied = [new(2, 0, 2), new(secondX, 0, secondZ)];
    Cell[] offsets = [new(0, 0, -1), new(1, 0, 0), new(0, 0, 1), new(-1, 0, 0)];
    int[] strengths = [10, 20, 30, 40];
    foreach (var cell in occupied)
    {
      var tile = baked.Tiles[cell];
      Assert.Equal(!blocksMovement, tile.Walkable);
      Assert.Equal(0, tile.CoverNorth + tile.CoverEast + tile.CoverSouth + tile.CoverWest);
      for (int side = 0; side < 4; side++)
      {
        var neighbor = cell + offsets[side];
        if (neighbor == occupied[0] || neighbor == occupied[1]) continue;
        tile = baked.Tiles[neighbor];
        int facingCover = side switch { 0 => tile.CoverSouth, 1 => tile.CoverWest, 2 => tile.CoverNorth, _ => tile.CoverEast };
        Assert.Equal(strengths[((turns + side) % 4 + 4) % 4], facingCover);
      }
    }
  }

  [TestCase]
  public void NonwalkableSpawnTerrainIsRejectedWithoutProps()
  {
    var tile = TestData.WallTile();
    tile.SpawnFactionSlot = 0;
    Assert.Throws<InvalidOperationException>(() => Bake(TestData.MakeMapData(new(1, 1, 1), (new Vector3I(0, 0, 0), tile)).Tiles, []));
  }

  [TestCase(TestName = "A three-cell prop rotates and stamps movement separately from sight")]
  public void RotatedFootprintBlocksEveryOccupiedCellWithoutInventingGround()
  {
    var cells = TestData.MakeOpenBattleMap(5, 5).Tiles;
    var prop = new BattlePropAuthoring
    {
      Footprint = [Cell.Zero, new(1, 0, 0), new(2, 0, 0)],
      BlocksMovement = true,
    };

    BattleMapData baked = Bake(cells, [new(prop, new Cell(2, 0, 3), 1)]);

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
    var prop = new BattlePropAuthoring { Footprint = [Cell.Zero], BlocksLineOfSight = true };

    BattleMapTileData tile = Bake(cells, [new(prop, Cell.Zero, 0)]).Tiles[Cell.Zero];

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

    BattleMapData baked = Bake(cells, []);
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
    var prop = new BattlePropAuthoring
    {
      Footprint = [Cell.Zero, new(1, 0, 0)],
      BlocksMovement = true,
      CoverEast = 60,
    };

    BattleMapData baked = Bake(cells, [new(prop, new Cell(2, 0, 3), 1)]);

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
    var eastEdge = new BattlePropAuthoring
    {
      Footprint = [Cell.Zero],
      CoverEast = 45,
    };
    var southEdge = new BattlePropAuthoring
    {
      Footprint = [Cell.Zero],
      CoverSouth = 55,
    };

    BattleMapTileData tile = Bake(cells,
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
    var prop = new BattlePropAuthoring
    {
      Footprint = [Cell.Zero],
      CoverNorth = 50,
    };

    BattleMapData baked = Bake(cells, [new(prop, Cell.Zero, 0)]);

    Assert.Equal(1, baked.Tiles.Count);
    Assert.Equal(new Cell(1, 1, 1), baked.Dimensions);
  }

  [TestCase(TestName = "Cover skips cells occupied by another prop regardless of input order")]
  public void CoverSkipsAllMovementBlockedStandingCells()
  {
    var cells = TestData.MakeOpenBattleMap().Tiles;
    var cover = new BattlePropAuthoring
    {
      Footprint = [Cell.Zero],
      CoverEast = 50,
    };
    var blocker = new BattlePropAuthoring { Footprint = [Cell.Zero], BlocksMovement = true };

    BattleMapData baked = Bake(cells,
      [new(cover, Cell.Zero, 0), new(blocker, new Cell(1, 0, 0), 0)]);

    Assert.Equal(0, baked.Tiles[new Cell(1, 0, 0)].CoverWest);
  }

  [TestCase(TestName = "Multiple nonblocking decorations can share a solid prop footprint")]
  public void NonblockingDecorationOverlapIsAllowed()
  {
    var cells = TestData.MakeOpenBattleMap().Tiles;
    var solid = new BattlePropAuthoring { Footprint = [Cell.Zero], BlocksMovement = true };
    var decoration = new BattlePropAuthoring { Footprint = [Cell.Zero] };

    BattleMapData baked = Bake(cells,
      [new(solid, Cell.Zero, 0), new(decoration, Cell.Zero, 0), new(decoration, Cell.Zero, 0)]);

    Assert.False(baked.Tiles[Cell.Zero].Walkable);
  }

  [TestCase(TestName = "Two solid footprints cannot overlap")]
  public void OverlappingMovementBlockersAreRejected()
  {
    var cells = TestData.MakeOpenBattleMap().Tiles;
    var prop = new BattlePropAuthoring { Footprint = [Cell.Zero], BlocksMovement = true };

    Assert.Throws<InvalidOperationException>(() => Bake(cells,
      [new(prop, Cell.Zero, 0), new(prop, Cell.Zero, 0)]));
    Assert.True(cells[Cell.Zero].Walkable);
  }

  [TestCase(TestName = "Footprints require existing ground for every cell")]
  public void MissingSupportIsRejected()
  {
    var cells = TestData.MakeMapData(new Vector3I(2, 1, 1), (Vector3I.Zero, TestData.FloorTile())).Tiles;
    var prop = new BattlePropAuthoring { Footprint = [Cell.Zero, new(1, 0, 0)] };

    Assert.Throws<InvalidOperationException>(() => Bake(cells, [new(prop, Cell.Zero, 0)]));
  }

  [TestCase(TestName = "Footprints require walkable ground before prop stamping")]
  public void NonwalkableSupportIsRejected()
  {
    var cells = TestData.MakeMapData(new Vector3I(1, 1, 1), (Vector3I.Zero, TestData.WallTile())).Tiles;
    var prop = new BattlePropAuthoring { Footprint = [Cell.Zero] };

    Assert.Throws<InvalidOperationException>(() => Bake(cells, [new(prop, Cell.Zero, 0)]));
  }

  [TestCase(TestName = "A prop cannot straddle mismatched authored ground surfaces")]
  public void UnequalSupportSurfacesAreRejected()
  {
    var cells = TestData.MakeOpenBattleMap().Tiles;
    cells[new Cell(1, 0, 0)].GroundSurfaceOffset = 0.5f;
    var prop = new BattlePropAuthoring { Footprint = [Cell.Zero, new(1, 0, 0)] };

    Assert.Throws<InvalidOperationException>(() => Bake(cells, [new(prop, Cell.Zero, 0)]));
  }

  [TestCase(TestName = "A rotated footprint cannot leave the nonnegative board")]
  public void NegativeOccupiedCoordinatesAreRejected()
  {
    var cells = TestData.MakeOpenBattleMap().Tiles;
    var prop = new BattlePropAuthoring { Footprint = [Cell.Zero, new(1, 0, 0)] };

    Assert.Throws<InvalidOperationException>(() => Bake(cells, [new(prop, Cell.Zero, 1)]));
  }

  [TestCase(TestName = "Base cells cannot have negative coordinates")]
  public void NegativeGroundCoordinatesAreRejected()
  {
    var cells = TestData.MakeMapData(new Vector3I(1, 1, 1), (new Vector3I(-1, 0, 0), TestData.FloorTile())).Tiles;

    Assert.Throws<InvalidOperationException>(() => Bake(cells, []));
  }

  [TestCase(TestName = "Solid props cannot block tagged spawn cells")]
  public void MovementBlockedSpawnIsRejected()
  {
    var cells = TestData.MakeMapData(new Vector3I(1, 1, 1), (Vector3I.Zero, TestData.SpawnTile(0))).Tiles;
    var prop = new BattlePropAuthoring { Footprint = [Cell.Zero], BlocksMovement = true };

    Assert.Throws<InvalidOperationException>(() => Bake(cells, [new(prop, Cell.Zero, 0)]));
    Assert.True(cells[Cell.Zero].Walkable);
  }

  [TestCase(TestName = "Decorative and sight-blocking props can occupy spawns")]
  public void NonblockingSpawnDecorationIsAllowed()
  {
    var cells = TestData.MakeMapData(new Vector3I(1, 1, 1), (Vector3I.Zero, TestData.SpawnTile(0))).Tiles;
    var prop = new BattlePropAuthoring { Footprint = [Cell.Zero], BlocksLineOfSight = true };

    BattleMapTileData tile = Bake(cells, [new(prop, Cell.Zero, 0)]).Tiles[Cell.Zero];

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
    var prop = new BattlePropAuthoring
    {
      Footprint = scenario switch
      {
        0 => [],
        1 => [new(1, 0, 0)],
        2 => [Cell.Zero, Cell.Zero],
        _ => [Cell.Zero, new(0, 1, 0)],
      },
    };

    Assert.Throws<InvalidOperationException>(() => Bake(cells, [new(prop, Cell.Zero, 0)]));
  }

  [TestCase(-1)]
  [TestCase(101)]
  public void InvalidCoverStrengthIsRejected(int strength)
  {
    var prop = new BattlePropAuthoring { Footprint = [Cell.Zero], CoverNorth = strength };
    Assert.Throws<InvalidOperationException>(() => Bake(TestData.MakeOpenBattleMap().Tiles, [new(prop, Cell.Zero, 0)]));
  }

  private readonly record struct Placement(BattlePropAuthoring Prop, Cell Anchor, int Turns);

  private static BattleMapData Bake(Godot.Collections.Dictionary<Cell, BattleMapTileData> cells, Placement[] props)
  {
    var map = TestData.MakeMapAuthoring(cells);
    try
    {
      foreach (var placement in props)
      {
        var prop = placement.Prop;
        // Multiple placements can share authored metadata, but scene instances are distinct.
        if (prop.GetParent() != null) prop = (BattlePropAuthoring)prop.Duplicate();
        map.AddChild(prop);
        prop.Position = map.MapToLocal(placement.Anchor) + Godot.Vector3.Up * cells[placement.Anchor].GroundSurfaceOffset;
        prop.Rotation = new(0, placement.Turns * Godot.Mathf.Pi / 2, 0);
      }
      using var scene = map.BuildScene();
      var exported = scene.Instantiate<BattleMap>();
      var result = exported.MapData;
      exported.Free();
      return result;
    }
    finally { map.Free(); }
  }
}
