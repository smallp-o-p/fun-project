using Godot;
using System;

namespace FunProject.Battle;

[Tool, GlobalClass]
public partial class BattleMapData : Resource
{
  [Export] public Godot.Vector3I Dimensions { get; set; }
  // PackedByteArray requires byte[]; enum[] exports are rejected with GD0102.
  // Godot enum Arrays use Variants rather than packed bytes.
  [Export] internal byte[] TileFlags { get; set; } = [];
  [Export] internal byte[] TileCoverDirections { get; set; } = [];
  [Export] internal byte[] TileCoverAmounts { get; set; } = [];
  [Export] public Godot.Collections.Dictionary<Godot.Vector3I, int> SpawnSlots { get; set; } = [];

  public void InitializeTiles()
  {
    int count = checked(Dimensions.X * Dimensions.Z * Dimensions.Y);
    TileFlags = new byte[count];
    TileCoverDirections = new byte[count];
    TileCoverAmounts = new byte[count];
  }

  public bool Contains(Godot.Vector3I cell) => cell.X >= 0 && cell.Y >= 0 && cell.Z >= 0 &&
    cell.X < Dimensions.X && cell.Y < Dimensions.Y && cell.Z < Dimensions.Z;

  // Materializes one authored cell; callers own and dispose the returned Resource.
  public BattleMapTileData GetTile(Godot.Vector3I cell)
  {
    int index = Index(cell);
    var flags = TileFlags.Length == 0 ? BattleTileFlags.None : (BattleTileFlags)TileFlags[index];
    return new BattleMapTileData
    {
      Walkable = (flags & BattleTileFlags.Walkable) != BattleTileFlags.None,
      BlocksLineOfSight = (flags & BattleTileFlags.BlocksLineOfSight) != BattleTileFlags.None,
      BlocksVerticalLineOfSight = (flags & BattleTileFlags.BlocksVerticalLineOfSight) != BattleTileFlags.None,
      CoverDirections = TileFlags.Length == 0 ? CoverDirections.None : (CoverDirections)TileCoverDirections[index],
      CoverAmount = TileFlags.Length == 0 ? 0 : TileCoverAmounts[index],
      SpawnFactionSlot = SpawnSlots.TryGetValue(cell, out int slot) ? slot : -1
    };
  }

  public void SetTile(Godot.Vector3I cell, BattleMapTileData tile)
  {
    int index = Index(cell);
    if (TileFlags.Length == 0) InitializeTiles();
    var flags = (tile.Walkable ? BattleTileFlags.Walkable : BattleTileFlags.None) |
      (tile.BlocksLineOfSight ? BattleTileFlags.BlocksLineOfSight : BattleTileFlags.None) |
      (tile.BlocksVerticalLineOfSight ? BattleTileFlags.BlocksVerticalLineOfSight : BattleTileFlags.None);
    TileFlags[index] = (byte)flags;
    TileCoverDirections[index] = checked((byte)tile.CoverDirections);
    TileCoverAmounts[index] = checked((byte)tile.CoverAmount);
    if (tile.SpawnFactionSlot >= 0) SpawnSlots[cell] = tile.SpawnFactionSlot;
    else SpawnSlots.Remove(cell);
  }

  private int Index(Godot.Vector3I cell)
  {
    if (!Contains(cell)) throw new ArgumentOutOfRangeException(nameof(cell));
    return cell.X + Dimensions.X * (cell.Z + Dimensions.Z * cell.Y);
  }
}

[Flags]
internal enum BattleTileFlags : byte
{
  None = 0,
  Walkable = 1 << 0,
  BlocksLineOfSight = 1 << 1,
  BlocksVerticalLineOfSight = 1 << 2
}
