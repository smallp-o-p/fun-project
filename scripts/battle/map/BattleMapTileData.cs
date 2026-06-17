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
  private CoverDirections _coverDirections = CoverDirections.None;
  private int _coverAmount;

  [Export] public bool Walkable { get; set; } = true;
  [Export] public bool BlocksLineOfSight { get; set; }

  [Export(PropertyHint.Flags, "North,South,East,West")]
  public CoverDirections CoverDirections
  {
    get => _coverDirections;
    set
    {
      _coverDirections = value;
      if (_coverDirections == CoverDirections.None)
        _coverAmount = 0;

      NotifyPropertyListChanged();
    }
  }

  [Export(PropertyHint.Range, "0,100,10")]
  public int CoverAmount
  {
    get => _coverAmount;
    set => _coverAmount = _coverDirections != CoverDirections.None ? Math.Clamp(value, 0, 100) : 0;
  }

  [Export] public int SpawnFactionSlot { get; set; } = -1;

  public override void _ValidateProperty(Godot.Collections.Dictionary property)
  {
    if (property["name"].AsStringName() != PropertyName.CoverAmount)
      return;
    if (_coverDirections != CoverDirections.None)
      return;

    var usage = property["usage"].As<PropertyUsageFlags>() | PropertyUsageFlags.ReadOnly;
    property["usage"] = (int)usage;
  }
}
