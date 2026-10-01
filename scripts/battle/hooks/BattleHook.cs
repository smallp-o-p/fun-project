using System.Collections.Generic;

namespace FunProject.Battle;

/// <summary>
/// Everything a firing hook needs to do its job: the read context for its scope (runtime
/// contexts derive from the one running-or-completed representation; trusted default systems
/// obtain the running receiver through it) and the in-flight executor action when the event
/// was committed inside one (None for dispatches no action produced, completion included).
/// Minted by the executor's BattleEventCommitted handler per firing.
/// </summary>
public readonly struct HookContext
{
  public BattleReadContext Read { get; }
  public Option<BattleAction> SourceAction { get; }

  internal HookContext(BattleReadContext read, Option<BattleAction> sourceAction)
  {
    Read = read;
    SourceAction = sourceAction;
  }
}

/// <summary>
/// The one battle observer/reactor concept. A hook matches a BattleEvent (registered by
/// event-tag type: concrete record or marker interface) and fires once per committed event,
/// receiving the context required to complete its work. It may mutate the session directly,
/// raise follow-up events (context.Session.RaiseEvent — queued, the committed stream stays
/// linear), and/or RETURN interrupt actions: the executor pushes returned actions to the
/// FRONT of the pending queue after the current primitive commits, so they run before the
/// interrupted composite's next step, through full action validation. Most hooks return [].
/// Returning interrupts while no executor action is in flight is a trusted-core violation
/// and throws (gameplay flows only through the executor). One-shot hooks (mines) flip
/// <see cref="NeedsToUnregister"/> — the registry owns the removal and retires them after
/// the firing. Hooks must never call executor.Submit from inside
/// OnEvent — a nested submission would corrupt the executor's pending action stack (there is
/// deliberately no guard; the rule is convention). Subclass freely — concrete hooks take whatever they need
/// (positions, factions, damage) through their own constructors; the root class and uniform
/// registration are the only fixed points.
/// </summary>
public abstract class BattleHook
{
  public abstract IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent);

  /// <summary>
  /// One-shot hooks (mines) flip this once their firing work is done; everything else keeps
  /// the default. The registry polls it right after <c>OnEvent</c> returns and retires the
  /// hook from every key it is registered under, so retirement takes effect on the next
  /// dispatch without the hook holding any registry reference.
  /// </summary>
  public virtual bool NeedsToUnregister => false;
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
