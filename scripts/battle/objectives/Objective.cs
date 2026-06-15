using FunProject.Combatants;

namespace FunProject.Battle;

// Runtime objective instance: owns behavior and (later) per-instance mutable
// progress state, reading its immutable authored ObjectiveData. Owner is assigned
// when the objective is enqueued into a faction's Operation.
public abstract class Objective
{
  public abstract ObjectiveData Data { get; }

  public Faction Owner { get; internal set; } = null!;

  public abstract bool IsComplete(BattleSession session);
  public abstract bool IsFailed(BattleSession session);
}
