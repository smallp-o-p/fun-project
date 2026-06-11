namespace FunProject.Battle;

/// <summary>
/// Session-internal bookkeeping observer. Unlike BattleTrigger (gameplay reactions
/// that queue interrupt actions through the executor), a listener may mutate session
/// state directly and raise follow-up events. Registered by event tag type, exactly
/// like triggers. Listeners raise follow-up events via session.RaiseEvent and must not submit executor actions.
/// </summary>
public abstract class BattleEventListener
{
  public abstract void OnEventCommitted(BattleSession session, BattleEvent battleEvent);
}
