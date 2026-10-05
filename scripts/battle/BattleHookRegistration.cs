using System;

namespace FunProject.Battle;

// The capability declared systems receive during factory startup: strictly the generic
// hook-registration door. No runtime handle, queries, or action submission escape through
// it, so registration cannot drive gameplay before startup has settled.
public sealed class BattleHookRegistration
{
  private readonly BattleRuntime _runtime;

  internal BattleHookRegistration(BattleRuntime runtime)
  {
    ArgumentNullException.ThrowIfNull(runtime);
    _runtime = runtime;
  }

  public void RegisterHook<TEventKey>(BattleHook hook, int priority = 0)
    where TEventKey : BattleEventTag
    => _runtime.RegisterHook<TEventKey>(hook, priority);
}
