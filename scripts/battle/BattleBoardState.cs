#nullable enable
using Godot;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed class BattleBoardState
{
  private readonly Dictionary<Vector3I, BattleTileState> _tiles = [];

  public int Width { get; }
  public int Length { get; }
  public int Levels { get; }
  public IReadOnlyCollection<BattleTileState> Tiles => _tiles.Values;

  public BattleBoardState(int width, int length, int levels = 1)
  {
    if (width <= 0)
      throw new ArgumentOutOfRangeException(nameof(width));
    if (length <= 0)
      throw new ArgumentOutOfRangeException(nameof(length));
    if (levels <= 0)
      throw new ArgumentOutOfRangeException(nameof(levels));

    Width = width;
    Length = length;
    Levels = levels;

    for (int y = 0; y < levels; y++)
    {
      for (int z = 0; z < length; z++)
      {
        for (int x = 0; x < width; x++)
        {
          var coordinates = new Vector3I(x, y, z);
          _tiles[coordinates] = new BattleTileState(coordinates);
        }
      }
    }
  }

  public bool IsInBounds(Vector3I coordinates)
  {
    return coordinates.X >= 0
      && coordinates.Y >= 0
      && coordinates.Z >= 0
      && coordinates.X < Width
      && coordinates.Y < Levels
      && coordinates.Z < Length;
  }

  public bool TryGetTile(Vector3I coordinates, out BattleTileState? tile)
  {
    if (!IsInBounds(coordinates))
    {
      tile = null;
      return false;
    }

    return _tiles.TryGetValue(coordinates, out tile);
  }

  public BattleTileState GetTile(Vector3I coordinates)
  {
    if (!TryGetTile(coordinates, out var tile) || tile == null)
      throw new ArgumentOutOfRangeException(nameof(coordinates));

    return tile;
  }

  public bool CanOccupy(Vector3I coordinates)
  {
    return TryGetTile(coordinates, out var tile)
      && tile != null
      && tile.IsWalkable
      && !tile.IsOccupied;
  }
}
