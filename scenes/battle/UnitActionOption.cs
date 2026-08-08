using FunProject.Battle;
using FunProject.Weapons;
using System;

// Presentation-side verb taxonomy. Each verb carries its availability (sourced from the domain
// UnitAction row) AND owns the factory for its targeting handler, which produces that
// verb's candidate set. Availability and candidates live on the same class so they cannot drift.
// Targeted verbs implement NeedsTargeting; instant verbs implement CanActDirectly.
public abstract class UnitActionOption
{
  public AliveUnit Unit { get; }
  public bool IsAvailable { get; }

  protected UnitActionOption(AliveUnit unit, bool isAvailable)
  {
    Unit = unit;
    IsAvailable = isAvailable;
  }
}

public sealed class MoveActionOption(AliveUnit unit, bool isAvailable) : UnitActionOption(unit, isAvailable), NeedsTargeting
{
  public IActionTargeting Targeting(BattleRuntime runtime)
  {
    return new MoveTargeting(runtime, Unit.State);
  }
}

public sealed class AttackActionOption : UnitActionOption, NeedsTargeting
{
  public Weapon Weapon { get; }

  public AttackActionOption(AliveUnit unit, Weapon weapon, bool isAvailable)
    : base(unit, isAvailable)
  {
    ArgumentNullException.ThrowIfNull(weapon);
    Weapon = weapon;
  }

  public IActionTargeting Targeting(BattleRuntime runtime)
  {
    return new AttackTargeting(runtime, Unit.State, Weapon);
  }
}

public sealed class PassActionOption(AliveUnit unit, bool isAvailable)
  : UnitActionOption(unit, isAvailable), CanActDirectly
{
  public BattleAction MakeAction()
  {
    return new PassUnit(Unit);
  }
}

public sealed class EndTurnActionOption(AliveUnit unit, bool isAvailable)
  : UnitActionOption(unit, isAvailable), CanActDirectly
{
  public BattleAction MakeAction()
  {
    return new EndFactionTurn(Unit.State.Combatant.OwningFaction);
  }
}

public sealed class ReloadActionOption(AliveUnit unit, bool isAvailable)
  : UnitActionOption(unit, isAvailable), CanActDirectly
{
  public BattleAction MakeAction()
  {
    return BattleAction.ReloadWeapon(Unit, RequireReloadableWeapon());
  }

  private AmmunitionedWeapon RequireReloadableWeapon()
    => Unit.State.EquippedWeapon.Bind(
        weapon => weapon is AmmunitionedWeapon ammunitioned ? Some(ammunitioned) : None)
      .Match(
        weapon => weapon,
        () => throw new InvalidOperationException("Reload option requires a magazine weapon."));
}
