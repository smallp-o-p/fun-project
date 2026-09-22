using System;

namespace FunProject.Battle;

/// <summary>Typed identity for anything that can be an attack target. A pure identity
/// wrapper: it retains the exact state reference and carries no registry, damage dispatch,
/// or capability plumbing.</summary>
public abstract class BattleEntity
{
  private BattleEntity() { }

  /// <summary>Identity of one <see cref="BattleUnitState"/>.</summary>
  public sealed class Unit : BattleEntity
  {
    public BattleUnitState State { get; }
    public Unit(BattleUnitState state)
    {
      ArgumentNullException.ThrowIfNull(state);
      State = state;
    }
  }

  /// <summary>Identity of one <see cref="BattleObjectState"/>.</summary>
  public sealed class Object : BattleEntity
  {
    public BattleObjectState State { get; }
    public Object(BattleObjectState state)
    {
      ArgumentNullException.ThrowIfNull(state);
      State = state;
    }
  }
}
