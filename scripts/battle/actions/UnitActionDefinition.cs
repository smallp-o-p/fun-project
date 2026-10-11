using System.Collections.Generic;

namespace FunProject.Battle;

// Domain verb taxonomy for a unit's action options. A definition declares when the verb EXISTS
// for a unit (capability: what it is and carries) and which conditions gate its AVAILABILITY
// (state: scheduler, AP, ammo). Availability is independent of target discovery: targeting
// queries may return an empty candidate set for an available verb. Identity is the concrete
// subclass; presentation maps subclasses onto its own option/targeting types. Definitions are
// stateless singletons held by the catalog.
public abstract class UnitActionDefinition
{
  internal abstract bool ExistsFor(BattleUnitState unit);
  internal abstract IReadOnlyList<UnitActionCondition> Conditions { get; }
}

public sealed class MoveActionDefinition : UnitActionDefinition
{
  private static readonly IReadOnlyList<UnitActionCondition> MoveConditions =
  [
    new UnitCanActNowCondition(),
    new HasActionPointsCondition(BattleSession.DefaultMovementStepActionPointCost),
  ];

  internal override bool ExistsFor(BattleUnitState unit) => true;
  internal override IReadOnlyList<UnitActionCondition> Conditions => MoveConditions;
}

public sealed class AttackActionDefinition : UnitActionDefinition
{
  private static readonly IReadOnlyList<UnitActionCondition> AttackConditions =
  [
    new UnitCanActNowCondition(),
    new HasActionPointsCondition(BattleSession.DefaultAttackActionPointCost),
    new WeaponIsLoadedCondition(),
  ];

  internal override bool ExistsFor(BattleUnitState unit) => unit.EquippedWeapon.IsSome;
  internal override IReadOnlyList<UnitActionCondition> Conditions => AttackConditions;
}

public sealed class ReloadActionDefinition : UnitActionDefinition
{
  private static readonly IReadOnlyList<UnitActionCondition> ReloadConditions =
  [
    new UnitCanActNowCondition(),
    new HasActionPointsCondition(BattleSession.DefaultReloadActionPointCost),
    new CanReloadCondition(),
  ];

  internal override bool ExistsFor(BattleUnitState unit)
    => unit.EquippedWeapon.Match(
      Some: weapon => weapon.HasMagazine,
      None: () => false);

  internal override IReadOnlyList<UnitActionCondition> Conditions => ReloadConditions;
}

public sealed class PassActionDefinition : UnitActionDefinition
{
  private static readonly IReadOnlyList<UnitActionCondition> PassConditions =
  [
    new UnitCanActNowCondition(),
  ];

  internal override bool ExistsFor(BattleUnitState unit) => true;
  internal override IReadOnlyList<UnitActionCondition> Conditions => PassConditions;
}

public sealed class EndTurnActionDefinition : UnitActionDefinition
{
  private static readonly IReadOnlyList<UnitActionCondition> EndTurnConditions =
  [
    new IsActiveSideCondition(),
  ];

  internal override bool ExistsFor(BattleUnitState unit) => true;
  internal override IReadOnlyList<UnitActionCondition> Conditions => EndTurnConditions;
}

// Every verb the game knows, in display order.
public static class UnitActionCatalog
{
  public static readonly IReadOnlyList<UnitActionDefinition> All =
  [
    new MoveActionDefinition(),
    new AttackActionDefinition(),
    new ReloadActionDefinition(),
    new PassActionDefinition(),
    new EndTurnActionDefinition(),
  ];
}
