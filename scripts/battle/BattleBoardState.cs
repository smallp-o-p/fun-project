#nullable enable
using Godot;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed class BattleBoardState
{
  private readonly BattleTileState[,,] _tiles;

  public int Width { get; }
  public int Length { get; }
  public int Levels { get; }
  public IEnumerable<BattleTileState> Tiles
  {
    get
    {
      for (int y = 0; y < Levels; y++)
      {
        for (int z = 0; z < Length; z++)
        {
          for (int x = 0; x < Width; x++)
            yield return _tiles[x, y, z];
        }
      }
    }
  }

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
    _tiles = new BattleTileState[width, levels, length];

    for (int y = 0; y < levels; y++)
    {
      for (int z = 0; z < length; z++)
      {
        for (int x = 0; x < width; x++)
        {
          var coordinates = new Vector3I(x, y, z);
          _tiles[x, y, z] = new BattleTileState(coordinates);
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

  public bool CanOccupy(Vector3I coordinates)
  {
    var tile = GetTileOrNull(coordinates);
    return tile is { IsWalkable: true, IsOccupied: false };
  }
}
