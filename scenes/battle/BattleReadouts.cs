using FunProject.Battle;

public readonly record struct UnitReadout(string Name, int Health, int MaxHealth, int Stun, bool IsUnconscious)
{
  public static UnitReadout Capture(BattleUnitState unit) => new(unit.Combatant.Name,
    unit.CurrentHealth, unit.MaxHealth, unit.CurrentStun, unit.IsUnconscious);
}

public readonly record struct ActionOptionReadout(UnitActionOption Option, bool IsAvailable);
