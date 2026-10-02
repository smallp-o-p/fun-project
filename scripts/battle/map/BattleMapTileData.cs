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
  [Export] public bool Walkable { get; set; } = true;
  [Export] public bool BlocksLineOfSight { get; set; }
  [Export] public bool BlocksVerticalLineOfSight { get; set; }
  [Export] public float GroundSurfaceOffset { get; set; }

  [Export(PropertyHint.Range, "0,100")]
  public int CoverNorth { get; set => field = Math.Clamp(value, 0, 100); }

  [Export(PropertyHint.Range, "0,100")]
  public int CoverEast { get; set => field = Math.Clamp(value, 0, 100); }

  [Export(PropertyHint.Range, "0,100")]
  public int CoverSouth { get; set => field = Math.Clamp(value, 0, 100); }

  [Export(PropertyHint.Range, "0,100")]
  public int CoverWest { get; set => field = Math.Clamp(value, 0, 100); }

  [Export] public int SpawnFactionSlot { get; set; } = -1;
}
