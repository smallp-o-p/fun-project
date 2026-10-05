using Godot;
using System;

namespace FunProject.Battle;

[Tool, GlobalClass]
public partial class BattleMapData : Resource
{
  private byte[] _tileFlags = [];
  private byte[] _tileCoverDirections = [];
  private byte[] _tileCoverAmounts = [];
  private Godot.Collections.Dictionary<Godot.Vector3I, int> _spawnSlots = [];
  private Godot.Collections.Dictionary<Godot.Vector3I, BattleMapTileData>? _legacyTiles;

  [Export] public Godot.Vector3I Dimensions { get; set; }
  [Export] public byte[] TileFlags { get { ConvertLegacyTiles(); return _tileFlags; } set => _tileFlags = value; }
  [Export] public byte[] TileCoverDirections { get { ConvertLegacyTiles(); return _tileCoverDirections; } set => _tileCoverDirections = value; }
  [Export] public byte[] TileCoverAmounts { get { ConvertLegacyTiles(); return _tileCoverAmounts; } set => _tileCoverAmounts = value; }
  [Export] public Godot.Collections.Dictionary<Godot.Vector3I, int> SpawnSlots { get { ConvertLegacyTiles(); return _spawnSlots; } set => _spawnSlots = value; }

  // Authoring fills these arrays before publishing; runtimes always take independent copies.
  public void InitializeTiles()
  {
    int count = BattleTileIndex.Count(Dimensions.X, Dimensions.Y, Dimensions.Z);
    _tileFlags = new byte[count];
    _tileCoverDirections = new byte[count];
    _tileCoverAmounts = new byte[count];
    _spawnSlots = [];
  }

  public bool Contains(Godot.Vector3I cell) => cell.X >= 0 && cell.Y >= 0 && cell.Z >= 0 &&
    cell.X < Dimensions.X && cell.Y < Dimensions.Y && cell.Z < Dimensions.Z;

  public BattleMapCell GetTile(Godot.Vector3I cell)
  {
    ValidateStorage();
    int index = Index(cell);
    byte flags = _tileFlags[index];
    return new((flags & BattleTileIndex.Walkable) != 0, (flags & BattleTileIndex.BlocksSight) != 0,
      (flags & BattleTileIndex.BlocksVerticalSight) != 0, (CoverDirections)_tileCoverDirections[index],
      _tileCoverAmounts[index], _spawnSlots.TryGetValue(cell, out int slot) ? slot : -1);
  }

  public void SetTile(Godot.Vector3I cell, BattleMapTileData tile)
  {
    ValidateStorage();
    int index = Index(cell);
    _tileFlags[index] = (byte)((tile.Walkable ? BattleTileIndex.Walkable : 0) |
      (tile.BlocksLineOfSight ? BattleTileIndex.BlocksSight : 0) |
      (tile.BlocksVerticalLineOfSight ? BattleTileIndex.BlocksVerticalSight : 0));
    _tileCoverDirections[index] = checked((byte)tile.CoverDirections);
    _tileCoverAmounts[index] = checked((byte)tile.CoverAmount);
    if (tile.SpawnFactionSlot >= 0) _spawnSlots[cell] = tile.SpawnFactionSlot;
    else _spawnSlots.Remove(cell);
  }

  private int Index(Godot.Vector3I cell)
  {
    if (!Contains(cell)) throw new ArgumentOutOfRangeException(nameof(cell));
    return BattleTileIndex.Of(cell.X, cell.Y, cell.Z, Dimensions.X, Dimensions.Z);
  }

  internal void ValidateStorage()
  {
    ConvertLegacyTiles();
    int count = BattleTileIndex.Count(Dimensions.X, Dimensions.Y, Dimensions.Z);
    if (_tileFlags.Length == 0 && _tileCoverDirections.Length == 0 && _tileCoverAmounts.Length == 0)
    {
      // A dimensions-only map is all holes; a dimensions-only runtime board is all walkable.
      _tileFlags = new byte[count];
      _tileCoverDirections = new byte[count];
      _tileCoverAmounts = new byte[count];
    }
    if (_tileFlags.Length != count || _tileCoverDirections.Length != count || _tileCoverAmounts.Length != count)
      throw new InvalidOperationException("Baked tile arrays must match the map dimensions.");
  }

  // Accept old scenes without saving the Resource-per-cell dictionary again. Defer until
  // read so deserialization property order does not matter.
  public override bool _Set(StringName property, Variant value)
  {
    if (property != "Tiles") return false;
    _legacyTiles = value.AsGodotDictionary<Godot.Vector3I, BattleMapTileData>();
    return true;
  }

  private void ConvertLegacyTiles()
  {
    if (_legacyTiles is not { } legacy) return;
    _legacyTiles = null;
    InitializeTiles();
    foreach (var (cell, tile) in legacy)
    {
      if (tile is null) continue;
      if (Contains(cell)) SetTile(cell, tile);
      else
      {
        if (tile.SpawnFactionSlot >= 0) _spawnSlots[cell] = tile.SpawnFactionSlot;
        GD.PushError($"{nameof(BattleMapData)} tile at '{cell}' is out of bounds for dimensions '{Dimensions}'.");
      }
    }
  }
}

public readonly record struct BattleMapCell(bool Walkable, bool BlocksLineOfSight,
  bool BlocksVerticalLineOfSight, CoverDirections CoverDirections, int CoverAmount, int SpawnFactionSlot);
