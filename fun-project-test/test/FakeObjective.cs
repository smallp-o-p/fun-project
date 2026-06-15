using FunProject.Battle;

// Test double: completion/failure are controlled by flags rather than session state.
// Flags are read live, so a test may flip them after construction.
public sealed class FakeObjective : Objective
{
  public bool Complete { get; set; }
  public bool Failed { get; set; }

  public FakeObjective() : base(new ObjectiveData())
  {
  }

  public override bool IsComplete(BattleSession session) => Complete;
  public override bool IsFailed(BattleSession session) => Failed;
}
