using FunProject.Combatants;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

public sealed class BattleVisibilitySnapshot
{
  private static readonly IReadOnlySet<int> EmptyVisibleUnitIds = new SysColGeneric.HashSet<int>();

  public static BattleVisibilitySnapshot Empty { get; } =
    new BattleVisibilitySnapshot(
      new Dictionary<Faction, BattleFactionVisibilityState>(),
      new Dictionary<int, IReadOnlySet<int>>());

  public IReadOnlyDictionary<Faction, BattleFactionVisibilityState> FactionStates { get; }
  public IReadOnlyDictionary<int, IReadOnlySet<int>> VisibleUnitsByObserverUnitId { get; }

  public BattleVisibilitySnapshot(
    IDictionary<Faction, BattleFactionVisibilityState> factionStates,
    IDictionary<int, IReadOnlySet<int>> visibleUnitsByObserverUnitId)
  {
    ArgumentNullException.ThrowIfNull(factionStates);
    ArgumentNullException.ThrowIfNull(visibleUnitsByObserverUnitId);

    FactionStates = factionStates.ToDictionary(
      entry => entry.Key,
      entry => entry.Value);

    VisibleUnitsByObserverUnitId = visibleUnitsByObserverUnitId.ToDictionary(
      entry => entry.Key,
      entry => (IReadOnlySet<int>)new SysColGeneric.HashSet<int>(entry.Value));
  }

  public BattleFactionVisibilityState GetFactionStateOrEmpty(Faction faction)
  {
    ArgumentNullException.ThrowIfNull(faction);
    if (FactionStates.TryGetValue(faction, out var state))
      return state;

    return BattleFactionVisibilityState.Empty;
  }

  public IReadOnlySet<int> GetVisibleUnitsForObserverOrEmpty(int observerUnitId)
  {
    if (VisibleUnitsByObserverUnitId.TryGetValue(observerUnitId, out var visibleUnitIds))
      return visibleUnitIds;

    return EmptyVisibleUnitIds;
  }

  public BattleVisibilitySnapshot WithMergedExplored(BattleVisibilitySnapshot previousSnapshot)
  {
    ArgumentNullException.ThrowIfNull(previousSnapshot);

    Dictionary<Faction, BattleFactionVisibilityState> mergedFactionStates = [];
    SysColGeneric.HashSet<Faction> allFactions = [.. FactionStates.Keys, .. previousSnapshot.FactionStates.Keys];

    foreach (var faction in allFactions)
    {
      var currentState = GetFactionStateOrEmpty(faction);
      var previousState = previousSnapshot.GetFactionStateOrEmpty(faction);
      SysColGeneric.HashSet<Godot.Vector3I> exploredTiles = [.. previousState.ExploredTiles];
      exploredTiles.UnionWith(currentState.ExploredTiles);
      mergedFactionStates[faction] = currentState.WithExploredTiles(exploredTiles);
    }

    return new BattleVisibilitySnapshot(
      mergedFactionStates,
      VisibleUnitsByObserverUnitId.ToDictionary(
        entry => entry.Key,
        entry => entry.Value));
  }
}
