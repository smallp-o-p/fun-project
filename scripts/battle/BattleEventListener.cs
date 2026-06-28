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

/// <summary>
/// Listener base for a single event tag type. Routing already guarantees the committed event
/// is a <typeparamref name="TEvent"/>, so the cast is safe and subclasses skip the manual
/// is/cast guard, implementing <see cref="OnEvent"/> instead.
/// </summary>
public abstract class BattleEventListener<TEvent> : BattleEventListener
  where TEvent : BattleEvent
{
  protected abstract void OnEvent(BattleSession session, TEvent evt);

  public sealed override void OnEventCommitted(BattleSession session, BattleEvent battleEvent)
    => OnEvent(session, (TEvent)battleEvent);
}
