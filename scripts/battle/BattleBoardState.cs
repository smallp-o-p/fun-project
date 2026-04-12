#nullable enable
using Godot;
using System;

namespace FunProject.Battle;

public sealed class BattleBoardState
{
  private readonly BattleTileState[,,] _tiles;

  Vector3I Dimensions { get; }

  public BattleBoardState(Vector3I dim)
  {
    if (dim.X <= 0 || dim.Y <= 0 || dim.Z <= 0)
      throw new ArgumentOutOfRangeException(nameof(dim));

    Dimensions = dim;
    _tiles = new BattleTileState[Dimensions.X, Dimensions.Y, Dimensions.Z];

    for (int y = 0; y < Dimensions.Y; y++)
    {
      for (int z = 0; z < Dimensions.Z; z++)
      {
        for (int x = 0; x < Dimensions.X; x++)
        {
          _tiles[x, y, z] = new BattleTileState(new Vector3I(x, y, z));
        }
      }
    }
  }

  public bool IsInBounds(Vector3I coordinates)
  {
    return coordinates.X >= 0
      && coordinates.Y >= 0
      && coordinates.Z >= 0
      && coordinates.X < Dimensions.X
      && coordinates.Y < Dimensions.Y
      && coordinates.Z < Dimensions.Z;
  }

  public BattleTileState? GetTileOrNull(Vector3I coordinates)
  {
    if (!IsInBounds(coordinates))
      return null;

    return _tiles[coordinates.X, coordinates.Y, coordinates.Z];
  }

  public BattleTileState GetTile(Vector3I coordinates)
  {
    var tile = GetTileOrNull(coordinates);
    if (tile == null)
      throw new ArgumentOutOfRangeException(nameof(coordinates));

    return tile;
  }

  public bool TrySetOccupant(Vector3I coords, int unitId)
  {
    BattleTileState tile = GetTile(coords);
    return tile.TrySetOccupant(unitId);
  }

  public bool CanOccupy(Vector3I coordinates)
  {
    var tile = GetTileOrNull(coordinates);
    return tile != null && tile.IsWalkable && !tile.IsOccupied;
  }
}
