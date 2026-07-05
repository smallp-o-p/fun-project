using FunProject.Battle;
using FunProject.Weapons;
using System;

// Presentation-side verb taxonomy. Each verb carries its availability (sourced from the domain
// UnitAction row) AND owns the factory for its targeting handler, which produces that
// verb's candidate set. Availability and candidates live on the same class so they cannot drift.
// Targeted verbs implement NeedsTargeting; instant verbs implement CanActDirectly.
public abstract class UnitActionOption
{
  public BattleUnitState Unit { get; }
  public bool IsAvailable { get; }

  protected UnitActionOption(BattleUnitState unit, bool isAvailable)
  {
    ArgumentNullException.ThrowIfNull(unit);
    Unit = unit;
    IsAvailable = isAvailable;
  }
}

public sealed class MoveActionOption(BattleUnitState unit, bool isAvailable) : UnitActionOption(unit, isAvailable), NeedsTargeting
{
  public IActionTargeting Targeting(BattleRuntime runtime)
  {
    return new MoveTargeting(runtime, Unit);
  }
}

public sealed class AttackActionOption : UnitActionOption, NeedsTargeting
{
  public Weapon Weapon { get; }

  public AttackActionOption(BattleUnitState unit, Weapon weapon, bool isAvailable)
    : base(unit, isAvailable)
  {
    ArgumentNullException.ThrowIfNull(weapon);
    Weapon = weapon;
  }

  public IActionTargeting Targeting(BattleRuntime runtime)
  {
    return new AttackTargeting(runtime, Unit, Weapon);
  }
}

public sealed class PassActionOption(BattleUnitState unit, bool isAvailable)
  : UnitActionOption(unit, isAvailable), CanActDirectly
{
  public BattleAction MakeAction()
  {
    return new PassUnit(Unit);
  }
}

public sealed class EndTurnActionOption(BattleUnitState unit, bool isAvailable)
  : UnitActionOption(unit, isAvailable), CanActDirectly
{
  public BattleAction MakeAction()
  {
    return new EndFactionTurn(Unit.Combatant.OwningFaction);
  }
}

public sealed class ReloadActionOption(BattleUnitState unit, bool isAvailable)
  : UnitActionOption(unit, isAvailable), CanActDirectly
{
  public BattleAction MakeAction()
  {
    return BattleAction.ReloadWeapon(Unit);
  }
}
