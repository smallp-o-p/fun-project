using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Items;

internal sealed class BattleTestUnit
{
  public BattleUnitState State { get; }

  public int UnitId => State.Id;
  public Combatant Combatant => State.Combatant;
  public int CurrentActionPoints => State.CurrentActionPoints;

  public BattleTestUnit(BattleUnitState state)
  {
    System.ArgumentNullException.ThrowIfNull(state);
    State = state;
  }

  public void AddInventoryItem(EquippableItem item)
  {
    State.AddInventoryItem(item);
  }

  public bool HasInventoryItem(EquippableItem item)
  {
    return State.HasInventoryItem(item);
  }

  public static implicit operator BattleUnitState(BattleTestUnit unit)
  {
    return unit.State;
  }
}
