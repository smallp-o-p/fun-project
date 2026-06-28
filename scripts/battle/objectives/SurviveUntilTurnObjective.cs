namespace FunProject.Battle;

public sealed class SurviveUntilTurnObjective : Objective
{
  private readonly int _targetTurn;

  public SurviveUntilTurnObjective(ObjectiveData data, int targetTurn) : base(data)
  {
    _targetTurn = targetTurn;
  }

  public override bool IsComplete(BattleSession session) => session.TurnNumber >= _targetTurn;
}
