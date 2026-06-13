namespace FunProject.Battle;

// Bookkeeping system: at the end of a faction's turn, evaluates that faction's
// operation (advance on completion, halt on failure) and raises objective/operation
// events. Registered after StatusEffectSystem/ArmorRegenSystem so end-of-turn DoT
// kills are reflected before objectives evaluate. State lives on each Operation.
public sealed class ObjectiveSystem : BattleEventListener
{
  public override void OnEventCommitted(BattleSession session, BattleEvent battleEvent)
  {
    if (battleEvent is not TurnEndedBattleEvent turnEnded)
      return;

    session.EvaluateOperationAtTurnEnd(turnEnded.Faction);
  }
}
