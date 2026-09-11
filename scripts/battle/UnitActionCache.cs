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
        return Evaluate();
      if (_isDirty)
      {
        bool available = Evaluate();
        _isAvailable = available;
        _isDirty = false;
      }
      return _isAvailable;
    }
  }

  private bool Evaluate() => _owner.Session.TryGetAlive(Unit).Match(
    alive => !Unit.IsIncapacitated
      && Action.Conditions.AsValueEnumerable()
        .All(condition => condition.IsMet(_owner.Session, alive)),
    () => false);

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

internal sealed class UnitActionCache(BattleSession session)
{
  private readonly Dictionary<BattleUnitState, IReadOnlyList<UnitAction>> _entries = [];

  internal BattleSession Session { get; } = session;
  internal bool IsExecuting { get; private set; }

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
