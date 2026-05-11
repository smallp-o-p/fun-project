using Godot;
using System;

namespace FunProject.Battle;

[Flags]
public enum BattleMapTileFlags
{
  None = 0,
  Present = 1 << 0,
  Walkable = 1 << 1,
  BlocksLineOfSight = 1 << 2
}

public enum CoverDirection
{
  NorthSouth,
  EastWest,
  NorthWest,
  NorthEast,
  SouthWest,
  SouthEast,
  None
};

[Tool]
[GlobalClass]
public partial class BattleMapTileData : Resource
{
  private CoverDirection _coverDescription = CoverDirection.None;
  private int _coverAmount;

  [Export] public Vector3I Coordinates { get; set; } = Vector3I.Zero;

  [Export(PropertyHint.Flags, "Present,Walkable,Blocks Line Of Sight")]
  public BattleMapTileFlags Flags { get; set; } = BattleMapTileFlags.Present | BattleMapTileFlags.Walkable;

  [Export]
  public CoverDirection CoverDescription
  {
    get => _coverDescription;
    set
    {
      _coverDescription = value;
      if (_coverDescription == CoverDirection.None)
        _coverAmount = 0;

      NotifyPropertyListChanged();
    }
  }

  [Export(PropertyHint.Range, "0,100,10")]
  public int CoverAmount
  {
    get => _coverAmount;
    set
    {
      _coverAmount = _coverDescription != CoverDirection.None ? Math.Clamp(value, 0, 100) : 0;
    }
  }

  public bool IsPresent
  {
    get => HasFlag(BattleMapTileFlags.Present);
    set => SetFlag(BattleMapTileFlags.Present, value);
  }

  public bool IsWalkable
  {
    get => HasFlag(BattleMapTileFlags.Walkable);
    set => SetFlag(BattleMapTileFlags.Walkable, value);
  }

  public bool BlocksLineOfSight
  {
    get => HasFlag(BattleMapTileFlags.BlocksLineOfSight);
    set => SetFlag(BattleMapTileFlags.BlocksLineOfSight, value);
  }

  public override void _ValidateProperty(Godot.Collections.Dictionary property)
  {
    if (property["name"].AsStringName() != PropertyName.CoverAmount)
      return;
    if (_coverDescription != CoverDirection.None)
      return;

    var usage = property["usage"].As<PropertyUsageFlags>() | PropertyUsageFlags.ReadOnly;
    property["usage"] = (int)usage;
  }

  private bool HasFlag(BattleMapTileFlags flag)
  {
    return (Flags & flag) == flag;
  }

  private void SetFlag(BattleMapTileFlags flag, bool isEnabled)
  {
    if (isEnabled)
    {
      Flags |= flag;
      return;
    }

    Flags &= ~flag;
  }
}
