using System;
using FunProject.Battle;

namespace FunProject.Tests;

// Factory/lifecycle suites own a bare runtime rather than a scenario fixture.
internal static class BattleRuntimeTestAccess
{
  internal static void Read(this BattleRuntime runtime, Action read)
  {
    using var scope = runtime.TryAcquireQueryScope().RequireRight();
    read();
  }
  internal static T Read<T>(this BattleRuntime runtime, Func<T> read)
  {
    using var scope = runtime.TryAcquireQueryScope().RequireRight();
    return read();
  }
  internal static void Mutate(this BattleRuntime runtime, Action mutation)
    => runtime.ExecuteAction(new BattleFixture.FixtureMutation(mutation));
}
