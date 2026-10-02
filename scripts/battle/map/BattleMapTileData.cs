using Godot;
using System;

namespace FunProject.Battle;

// Authored gameplay for a single map cell. Used both as a reusable brush in the editor palette
// and as the per-cell value in BattleMapData.Tiles — position comes from the dictionary key, so
// this type carries no Coordinates. Walkable/BlocksLineOfSight/cover/spawn are all independent.
[Tool]
[GlobalClass]
public partial class BattleMapTileData : Resource
{
  private int _coverNorth;
  private int _coverEast;
  private int _coverSouth;
  private int _coverWest;

  [Export] public bool Walkable { get; set; } = true;
  [Export] public bool BlocksLineOfSight { get; set; }
  [Export] public bool BlocksVerticalLineOfSight { get; set; }
  [Export] public float GroundSurfaceOffset { get; set; }

  [Export(PropertyHint.Range, "0,100")]
  public int CoverNorth
  {
    get => _coverNorth;
    set => _coverNorth = Math.Clamp(value, 0, 100);
  }

  [Export(PropertyHint.Range, "0,100")]
  public int CoverEast
  {
    get => _coverEast;
    set => _coverEast = Math.Clamp(value, 0, 100);
  }

  [Export(PropertyHint.Range, "0,100")]
  public int CoverSouth
  {
    get => _coverSouth;
    set => _coverSouth = Math.Clamp(value, 0, 100);
  }

  [Export(PropertyHint.Range, "0,100")]
  public int CoverWest
  {
    get => _coverWest;
    set => _coverWest = Math.Clamp(value, 0, 100);
  }

  [Export] public int SpawnFactionSlot { get; set; } = -1;
}
