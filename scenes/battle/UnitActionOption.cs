using FunProject.Battle;
using FunProject.Weapons;
using System;

// Presentation-side verb taxonomy. Each retained adapter delegates availability to its domain
// UnitAction row and owns the factory for its targeting handler. Candidate sets remain on demand;
// an available verb can still yield an empty set. Targeted verbs implement NeedsTargeting;
// instant verbs implement CanActDirectly and mint a fresh proof when invoked.
public abstract class UnitActionOption
{
  private readonly UnitAction _action;

  public BattleUnitState Unit => _action.Unit;
  public bool IsAvailable => _action.IsAvailable;

  protected UnitActionOption(UnitAction action)
  {
    ArgumentNullException.ThrowIfNull(action);
    _action = action;
  }

  protected AliveUnit RequireAlive(BattleRuntime runtime) =>
    runtime.TryGetAlive(Unit).Match(
      alive => alive,
      () => throw new InvalidOperationException("Acting unit is no longer alive."));
}

public sealed class MoveActionOption(UnitAction action) : UnitActionOption(action), NeedsTargeting
{
  public IActionTargeting Targeting(BattleRuntime runtime)
  {
    return new MoveTargeting(runtime, Unit);
  }
}

public sealed class AttackActionOption : UnitActionOption, NeedsTargeting
{
  public Weapon Weapon { get; }

  public AttackActionOption(UnitAction action, Weapon weapon)
    : base(action)
  {
    ArgumentNullException.ThrowIfNull(weapon);
    Weapon = weapon;
  }

  public IActionTargeting Targeting(BattleRuntime runtime)
  {
    return new AttackTargeting(runtime, Unit, Weapon);
  }
}

public sealed class PassActionOption(UnitAction action)
  : UnitActionOption(action), CanActDirectly
{
  public BattleAction MakeAction(BattleRuntime runtime)
  {
    return new PassUnit(RequireAlive(runtime));
  }
}

public sealed class EndTurnActionOption(UnitAction action)
  : UnitActionOption(action), CanActDirectly
{
  public BattleAction MakeAction(BattleRuntime runtime)
  {
    RequireAlive(runtime);
    return new EndFactionTurn(Unit.Side);
  }
}

public sealed class ReloadActionOption(UnitAction action)
  : UnitActionOption(action), CanActDirectly
{
  public BattleAction MakeAction(BattleRuntime runtime)
  {
    return BattleAction.ReloadWeapon(RequireAlive(runtime), RequireReloadableWeapon());
  }

  private AmmunitionedWeapon RequireReloadableWeapon()
    => Unit.EquippedWeapon.Bind(
        weapon => weapon is AmmunitionedWeapon ammunitioned ? Some(ammunitioned) : None)
      .Match(
        weapon => weapon,
        () => throw new InvalidOperationException("Reload option requires a magazine weapon."));
}
