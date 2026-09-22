using FunProject.Combatants;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

/// <summary>
/// Base objective for missions built around <see cref="TimedEffectCapability"/> objects: fail
/// the moment any of them expires or is destroyed, pass per the subclass' predicate over how
/// many were placed and how many were resolved without expiring (defused). Tracking happens
/// entirely from the delivered event stream — the router guarantees delivery of the observed
/// events, each fires exactly once per object (status flips are one-way), and the factory
/// attaches objectives before placing objects, so no placement is ever missed. Counted
/// objects are those carrying a <see cref="TimedEffectCapability"/>; that is this family's
/// definition.
/// </summary>
public abstract class TimedObjectObjective(ObjectiveData data) : Objective(data)
{
  private int _placed;
  private int _defused;

  public sealed override IReadOnlyCollection<Type> ObservedEventKeys { get; } =
  [
    typeof(ObjectPlacedBattleEvent),
    typeof(ObjectInteractedBattleEvent),
    typeof(ObjectExpiredBattleEvent),
    typeof(ObjectDestroyedBattleEvent),
  ];

  public sealed override ObjectiveResult Check(Faction owner, BattleEvent battleEvent, BattleSession session)
  {
    switch (battleEvent)
    {
      case ObjectExpiredBattleEvent expired when expired.Object.FindCapability<TimedEffectCapability>().IsSome:
        return ObjectiveResult.Failed;
      case ObjectDestroyedBattleEvent destroyed
        when destroyed.Object.FindCapability<TimedEffectCapability>().IsSome:
        return ObjectiveResult.Failed;
      case ObjectPlacedBattleEvent placed when placed.Object.FindCapability<TimedEffectCapability>().IsSome:
        _placed++;
        break;
      case ObjectInteractedBattleEvent interacted when interacted.Object.FindCapability<TimedEffectCapability>().IsSome:
        _defused++;
        break;
    }

    return _placed > 0 && HasPassed(_placed, _defused) ? ObjectiveResult.Passed : ObjectiveResult.Ongoing;
  }

  /// <summary>The pass predicate over how many tracked objects were placed and defused.</summary>
  protected abstract bool HasPassed(int placed, int defused);
}
