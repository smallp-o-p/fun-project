using Godot;
using Godot.Collections;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

[GlobalClass]
public partial class BattleMapData : Resource
{
  [Export] public Vector3I Dimensions { get; set; } = new(8, 1, 8);
  [Export] public float TileSize { get; set; } = 1.0f;
  [Export] public Array<BattleMapTileData> TileOverrides { get; set; } = [];

  public BattleBoardState CreateBoardState()
  {
    if (!HasValidDimensions(Dimensions))
      throw new InvalidOperationException($"{nameof(BattleMapData)} requires positive dimensions.");
    if (TileSize <= 0.0f)
      throw new InvalidOperationException($"{nameof(BattleMapData)} requires a positive tile size.");

    BattleBoardState board = new(Dimensions);

    foreach (BattleMapTileData tileOverride in TileOverrides)
    {
      board.ValidatePoint(tileOverride.Coordinates).Match(
        point =>
        {
          BattleTileState tile = board.GetTile(point);
          tile.IsWalkable = tileOverride.IsPresent && tileOverride.IsWalkable;
          tile.BlocksLineOfSight = tileOverride.IsPresent && tileOverride.BlocksLineOfSight;
        },
        () =>
        {
          GD.PushError($"{nameof(BattleMapData)} tile override at '{tileOverride.Coordinates}' is out of bounds for dimensions '{Dimensions}'.");
        });
    }

    return board;
  }

  public IEnumerable<Vector3I> EnumeratePresentCells()
  {
    if (!HasValidDimensions(Dimensions))
      yield break;

    for (int y = 0; y < Dimensions.Y; y++)
    {
      for (int z = 0; z < Dimensions.Z; z++)
      {
        for (int x = 0; x < Dimensions.X; x++)
        {
          Vector3I coordinates = new(x, y, z);
          Option<BattleMapTileData> tileOverride = GetTileOverrideOrNone(coordinates);
          if (tileOverride.Match(value => !value.IsPresent, () => false))
            continue;

          yield return coordinates;
        }
      }
    }
  }

  public Option<BattleMapTileData> GetTileOverrideOrNone(Vector3I coordinates)
  {
    Option<BattleMapTileData> matchedTile = None;

    foreach (BattleMapTileData tileOverride in TileOverrides)
    {
      if (tileOverride == null)
        continue;
      if (tileOverride.Coordinates != coordinates)
        continue;

      matchedTile = Some(tileOverride);
    }

    return matchedTile;
  }

  public static bool HasValidDimensions(Vector3I dimensions)
  {
    return dimensions.X > 0 && dimensions.Y > 0 && dimensions.Z > 0;
  }
}
