#nullable enable
using System;
using System.Collections.Generic;
using Godot;

namespace FunProject.Battle;

public sealed class BattleFactionVisibilityState
{
  private static readonly IReadOnlySet<Vector3I> EmptyTileSet = new HashSet<Vector3I>();
  private static readonly IReadOnlySet<int> EmptyUnitIdSet = new HashSet<int>();

  public static BattleFactionVisibilityState Empty { get; } = new();

  public IReadOnlySet<Vector3I> VisibleTiles { get; }
  public IReadOnlySet<Vector3I> ExploredTiles { get; }
  public IReadOnlySet<int> VisibleForeignUnitIds { get; }

  public BattleFactionVisibilityState(
    IEnumerable<Vector3I>? visibleTiles = null,
    IEnumerable<Vector3I>? exploredTiles = null,
    IEnumerable<int>? visibleForeignUnitIds = null)
  {
    VisibleTiles = visibleTiles == null ? EmptyTileSet : new HashSet<Vector3I>(visibleTiles);
    ExploredTiles = exploredTiles == null ? EmptyTileSet : new HashSet<Vector3I>(exploredTiles);
    VisibleForeignUnitIds = visibleForeignUnitIds == null ? EmptyUnitIdSet : new HashSet<int>(visibleForeignUnitIds);
  }

  public BattleFactionVisibilityState WithExploredTiles(IEnumerable<Vector3I> exploredTiles)
  {
    ArgumentNullException.ThrowIfNull(exploredTiles);
    return new BattleFactionVisibilityState(VisibleTiles, exploredTiles, VisibleForeignUnitIds);
  }
}
