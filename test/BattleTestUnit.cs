#nullable enable
using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Items;
using Godot;

internal sealed class BattleTestUnit
{
  public BattleUnitState State { get; }
  public BattleSession.BattleUnitHandle Handle { get; }

  public int UnitId => State.UnitId;
  public Combatant Combatant => State.Combatant;
  public Vector3I Position => State.Position;
  public int CurrentActionPoints => State.CurrentActionPoints;

  public BattleTestUnit(BattleUnitState state, BattleSession.BattleUnitHandle handle)
  {
    State = state ?? throw new System.ArgumentNullException(nameof(state));
    Handle = handle ?? throw new System.ArgumentNullException(nameof(handle));
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
