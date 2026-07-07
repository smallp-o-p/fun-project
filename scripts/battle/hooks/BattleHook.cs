using System.Collections.Generic;

namespace FunProject.Battle;

public enum HookPhase
{
  Before, // Before the event starts
  After, // After the event has completed
}

/// <summary>
/// Everything a firing hook needs to do its job: the session (mutate it or don't), the phase
/// that is firing, and the in-flight executor action when the event was committed inside one
/// (what the old trigger signature carried as sourceAction; None for setup/turn-transition
/// dispatches that no action produced). Minted by the session's dispatch loop per firing.
/// </summary>
public readonly struct HookContext
{
  public BattleSession Session { get; }
  public HookPhase Phase { get; }
  public Option<BattleAction> SourceAction { get; }

  internal HookContext(BattleSession session, HookPhase phase, Option<BattleAction> sourceAction)
  {
    Session = session;
    Phase = phase;
    SourceAction = sourceAction;
  }
}

/// <summary>
/// The one battle observer/reactor concept. A hook matches a BattleEvent (registered by
/// event-tag type: concrete record or marker interface) and fires in its registered phase,
/// receiving the context required to complete its work. It may mutate the session directly,
/// raise follow-up events (context.Session.RaiseEvent — queued, the committed stream stays
/// linear), and/or RETURN interrupt actions: the executor pushes returned actions to the
/// FRONT of the pending queue after the current primitive commits, so they run before the
/// interrupted composite's next step, through full action validation. Most hooks return [].
/// Returning interrupts while no executor action is in flight is a trusted-core violation
/// and throws (gameplay flows only through the executor). One-shot hooks (mines) unregister
/// themselves: session.UnregisterHook. Hooks must never call executor.Submit (the executor
/// throws while events dispatch). Subclass freely — concrete hooks take whatever they need
/// (positions, factions, damage) through their own constructors; the root class and uniform
/// registration are the only fixed points.
/// </summary>
public abstract class BattleHook
{
  public abstract IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent);
}

/// <summary>
/// Generic base for a single event tag type: routing guarantees the committed event is a
/// TEvent, so subclasses implement the typed overload and skip the cast.
/// </summary>
public abstract class BattleHook<TEvent> : BattleHook
  where TEvent : BattleEvent
{
  protected abstract IReadOnlyList<BattleAction> OnEvent(HookContext context, TEvent evt);

  public sealed override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    => OnEvent(context, (TEvent)battleEvent);
}
