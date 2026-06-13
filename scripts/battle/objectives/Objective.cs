using FunProject.Combatants;
using System;

namespace FunProject.Battle;

public sealed class Objective
{
  public ObjectiveData Data { get; }

  // Assigned when the objective is enqueued into a faction's Operation.
  public Faction Owner { get; internal set; } = null!;

  public Objective(ObjectiveData data)
  {
    ArgumentNullException.ThrowIfNull(data);
    Data = data;
  }

  public bool IsComplete(BattleSession session) => Data.IsComplete(this, session);
  public bool IsFailed(BattleSession session) => Data.IsFailed(this, session);
}
