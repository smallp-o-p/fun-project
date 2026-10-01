using System;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed class UnitAction
{
  private readonly UnitActionCache _owner;
  private bool _isAvailable;
  private bool _isDirty = true;

  internal UnitAction(UnitActionCache owner, BattleUnitState unit, UnitActionDefinition action)
  {
    _owner = owner;
    Unit = unit;
    Action = action;
  }

  public BattleUnitState Unit { get; }
  public UnitActionDefinition Action { get; }
  internal bool IsDirty => _isDirty;

  public bool IsAvailable
  {
    get
    {
      if (_owner.IsExecuting)
        return Evaluate(_owner.ReadContext());
      if (_isDirty)
      {
        _isAvailable = Evaluate(_owner.ReadContext());
        _isDirty = false;
      }
      return _isAvailable;
    }
  }

  // Every evaluation reads a fresh runtime context: completion disables the option, and
  // running evaluations see the current turn facts rather than a retained receiver.
  private bool Evaluate(BattleReadContext context)
  {
    if (context.Completed.IsSome)
      return false;
    return context.State.TryGetAlive(Unit).Match(
      alive => !Unit.IsIncapacitated
        && Action.Conditions.AsValueEnumerable()
          .All(condition => condition.IsMet(context, alive)),
      () => false);
  }

  internal void Invalidate(BattleEvent battleEvent)
  {
    if (_isDirty)
      return;
    if (UnitActionCondition.IncapacityMayChange(battleEvent, Unit)
        || Action.Conditions.AsValueEnumerable()
          .Any(condition => condition.MayChange(battleEvent, Unit)))
      _isDirty = true;
  }

  internal void MarkDirty() => _isDirty = true;
}

// Per-unit action-option storage living on the shared tactical state; the runtime installs
// the fresh-context provider at construction, so cached options always evaluate against the
// runtime's current running-or-completed representation.
internal sealed class UnitActionCache
{
  private readonly Dictionary<BattleUnitState, IReadOnlyList<UnitAction>> _entries = [];
  private Func<BattleReadContext> _contextProvider = static () => throw new InvalidOperationException(
    "No runtime read context is installed for action-option evaluation.");

  internal bool IsExecuting { get; private set; }

  internal BattleReadContext ReadContext() => _contextProvider();

  // Engine-owned wiring: the runtime hands the cache its fresh-context provider once.
  internal void InstallContextProvider(Func<BattleReadContext> provider)
  {
    ArgumentNullException.ThrowIfNull(provider);
    _contextProvider = provider;
  }

  internal IReadOnlyList<UnitAction> GetFor(AliveUnit unit)
  {
    if (_entries.TryGetValue(unit.State, out IReadOnlyList<UnitAction>? actions))
      return actions;

    var materialized = UnitActionCatalog.All
      .AsValueEnumerable().Where(definition => definition.ExistsFor(unit.State))
      .Select(definition => new UnitAction(this, unit.State, definition))
      .ToList();
    actions = materialized.AsReadOnly();
    _entries.Add(unit.State, actions);
    return actions;
  }

  internal void Invalidate(BattleEvent battleEvent)
  {
    foreach (IReadOnlyList<UnitAction> actions in _entries.Values)
      foreach (UnitAction action in actions)
        action.Invalidate(battleEvent);
  }

  internal void InvalidateAll()
  {
    foreach (IReadOnlyList<UnitAction> actions in _entries.Values)
      foreach (UnitAction action in actions)
        action.MarkDirty();
  }

  internal void BeginExecution() => IsExecuting = true;

  internal void EndExecution() => IsExecuting = false;
}
