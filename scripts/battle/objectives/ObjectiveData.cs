using FunProject.Core;

namespace FunProject.Battle;

// Authored objective template. Behavior is by subclass (like StatusEffectSpecData);
// the runtime Objective is passed in so predicates can read its Owner.
public abstract partial class ObjectiveData : NamedEntityData
{
  public abstract bool IsComplete(Objective objective, BattleSession session);
  public abstract bool IsFailed(Objective objective, BattleSession session);
}
