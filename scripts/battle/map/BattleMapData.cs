using Godot;
using System;

namespace FunProject.Battle;

[GlobalClass]
public partial class BattleMapData : Resource
{
  [Export] public Vector3I Dimensions { get; set; } = new(8, 1, 8);

  // Present cells keyed by position. A cell not in the dictionary is a hole (unwalkable). Each
  // value is a BattleMapTileData — the same type authored as an editor brush.
  [Export] public Godot.Collections.Dictionary<Vector3I, BattleMapTileData> Tiles { get; set; } = [];

  public BattleBoardState CreateBoardState()
  {
    if (!HasValidDimensions(Dimensions))
      throw new InvalidOperationException($"{nameof(BattleMapData)} requires positive dimensions.");

    BattleBoardState board = new(Dimensions);

    foreach (BattleBoardState.ValidatedPoint point in board.EnumerateBoardPoints())
      board.GetTile(point).IsWalkable = false;

    foreach (var entry in Tiles)
    {
      Vector3I coordinate = entry.Key;
      BattleMapTileData data = entry.Value;
      if (data is null)
        continue;

      board.ValidatePoint(coordinate).Match(
        point =>
        {
          BattleTileState tile = board.GetTile(point);
          tile.IsWalkable = data.Walkable;
          tile.BlocksLineOfSight = data.BlocksLineOfSight;
          tile.Cover = new TileCover(data.CoverDirections, data.CoverAmount);
        },
        () =>
        {
          GD.PushError($"{nameof(BattleMapData)} tile at '{coordinate}' is out of bounds for dimensions '{Dimensions}'.");
        });
    }

    return board;
  }

  public static bool HasValidDimensions(Vector3I dimensions)
  {
    return dimensions.X > 0 && dimensions.Y > 0 && dimensions.Z > 0;
  }
}
