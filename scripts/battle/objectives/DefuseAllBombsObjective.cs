using FunProject.Combatants;

namespace FunProject.Battle;

/// <summary>
/// The bomb-defusal win condition: a <see cref="TimedObjectObjective"/> that passes
/// only when every timed-effect object placed in the battle was defused before its deadline.
/// </summary>
public sealed class DefuseAllBombsObjective : TimedObjectObjective
{
  public DefuseAllBombsObjective(DefuseAllBombsObjectiveData data) : base(data)
  {
  }

  protected override bool HasPassed(int placed, int defused) => defused == placed;
}
