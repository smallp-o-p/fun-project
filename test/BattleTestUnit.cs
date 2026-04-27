using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Items;

internal sealed class BattleTestUnit
{
  public BattleUnitState State { get; }
  public BattleSession.BattleUnitHandle Handle { get; }

  public int UnitId => State.UnitId;
  public Combatant Combatant => State.Combatant;
  public int CurrentActionPoints => State.CurrentActionPoints;

  public BattleTestUnit(BattleUnitState state, BattleSession.BattleUnitHandle handle)
  {
    System.ArgumentNullException.ThrowIfNull(state);
    System.ArgumentNullException.ThrowIfNull(handle);
    State = state;
    Handle = handle;
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
