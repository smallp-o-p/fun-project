using FunProject.Combatants;
using System;

namespace FunProject.Battle;

// Runtime objective instance: owns its behavior and parameters in code, and
// references authored display data (name/description). Owner is assigned when the
// objective is enqueued into a faction's Operation.
public abstract class Objective
{
  public ObjectiveData Data { get; }

  public Faction Owner { get; internal set; } = null!;

  protected Objective(ObjectiveData data)
  {
    ArgumentNullException.ThrowIfNull(data);
    Data = data;
  }

  public abstract bool IsComplete(BattleSession session);

  // Default failure rule shared by all current objectives: the owning faction has
  // no living units left. Override only if an objective needs a different rule.
  public virtual bool IsFailed(BattleSession session) => !session.HasLivingUnits(Owner);
}
