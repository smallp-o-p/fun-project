using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Items;

namespace FunProject.Tests;

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

  // Mints an aliveness proof for this unit from the given session (throws if the unit is not
  // alive in the session). The test-side door to the new AliveUnit-typed read queries.
  public AliveUnit AliveIn(BattleSession session)
  {
    return session.TryGetAlive(State).RequireSome($"Unit {State.Id} is not alive in the session.");
  }

  public static implicit operator BattleUnitState(BattleTestUnit unit)
  {
    return unit.State;
  }
}
