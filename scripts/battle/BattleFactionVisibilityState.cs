using System;
using System.Collections.Generic;
using Godot;

namespace FunProject.Battle;

public sealed class BattleFactionVisibilityState
{
  private static readonly IReadOnlySet<Vector3I> EmptyTileSet = new SysColGeneric.HashSet<Vector3I>();
  private static readonly IReadOnlySet<int> EmptyUnitIdSet = new SysColGeneric.HashSet<int>();

  public static BattleFactionVisibilityState Empty { get; } = new();

  public IReadOnlySet<Vector3I> VisibleTiles { get; }
  public IReadOnlySet<Vector3I> ExploredTiles { get; }
  public IReadOnlySet<int> VisibleForeignUnitIds { get; }

  public BattleFactionVisibilityState()
  {
    VisibleTiles = EmptyTileSet;
    ExploredTiles = EmptyTileSet;
    VisibleForeignUnitIds = EmptyUnitIdSet;
  }

  public BattleFactionVisibilityState(
    IEnumerable<Vector3I> visibleTiles,
    IEnumerable<Vector3I> exploredTiles,
    IEnumerable<int> visibleForeignUnitIds)
  {
    VisibleTiles = new SysColGeneric.HashSet<Vector3I>(visibleTiles);
    ExploredTiles = new SysColGeneric.HashSet<Vector3I>(exploredTiles);
    VisibleForeignUnitIds = new SysColGeneric.HashSet<int>(visibleForeignUnitIds);
  }

  public BattleFactionVisibilityState WithExploredTiles(IEnumerable<Vector3I> exploredTiles)
  {
    ArgumentNullException.ThrowIfNull(exploredTiles);
    return new BattleFactionVisibilityState(VisibleTiles, exploredTiles, VisibleForeignUnitIds);
  }
}
