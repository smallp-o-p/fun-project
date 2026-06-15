using FunProject.Battle;
using System;

// Runtime double whose predicates read the authored flags directly. The flags are
// read live, so a test may flip FakeObjectiveData.Complete after construction.
public sealed class FakeObjective : Objective
{
  private readonly FakeObjectiveData _data;

  public FakeObjective(FakeObjectiveData data)
  {
    ArgumentNullException.ThrowIfNull(data);
    _data = data;
  }

  public override ObjectiveData Data => _data;

  public override bool IsComplete(BattleSession session) => _data.Complete;
  public override bool IsFailed(BattleSession session) => _data.Failed;
}
