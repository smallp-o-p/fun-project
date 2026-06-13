using FunProject.Battle;

// Test-double: completion/failure are controlled by flags rather than session state.
public partial class FakeObjectiveData : ObjectiveData
{
  public bool Complete { get; set; }
  public bool Failed { get; set; }

  public override bool IsComplete(Objective objective, BattleSession session) => Complete;
  public override bool IsFailed(Objective objective, BattleSession session) => Failed;
}
