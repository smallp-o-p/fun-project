using FunProject.Battle;
using FunProject.Combatants;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class BattleRuntimeTest
{
  [TestCase(TestName = "Disposed runtime no longer republishes session events")]
  public static void DisposedRuntimeNoLongerRepublishesSessionEvents()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = BattleFixture.Started(new Vector3I(3, 1, 3),
      new UnitLoadout(TestData.MakeCombatant("Alpha", faction)), Vector3I.Zero);
    var runtime = battle.Runtime;
    var observed = new List<BattleEvent>();
    runtime.BattleEventCommitted += observed.Add;

    runtime.Dispose();
    // A retained replacement executor is safe here: the runtime's own executor was
    // disposed with it, so this is still the session's only live executor, and it commits
    // an event the disposed runtime must not re-raise.
    using var replacement = new BattleActionExecutor(battle.Session);
    replacement.Submit(BattleAction.SpawnUnit(
      TestData.MakeCombatant("Alpha", faction), battle.Board.At(1, 0, 1)));

    Assert.Equal(0, observed.Count);
    runtime.BattleEventCommitted -= observed.Add;
  }

  [TestCase(TestName = "Public methods throw after runtime is disposed")]
  public static void PublicMethodsThrowAfterRuntimeIsDisposed()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = BattleFixture.Started(new Vector3I(3, 1, 3),
      new UnitLoadout(TestData.MakeCombatant("Alpha", faction)), Vector3I.Zero);
    var runtime = battle.Runtime;
    var hook = new RecordingHook();

    runtime.Dispose();

    Assert.Throws<ObjectDisposedException>(() => runtime.Query(new GetFactionAliveUnits(faction)));
    Assert.Throws<ObjectDisposedException>(() => runtime.ExecuteAction(BattleAction.SpawnUnit(
      TestData.MakeCombatant("Alpha", faction),
      battle.Board.At(1, 0, 1))));
    Assert.Throws<ObjectDisposedException>(() => runtime.RegisterHook<UnitMovedBattleEvent>(hook));
    Assert.Throws<ObjectDisposedException>(() => runtime.RegisterHook<IPositionedBattleEvent>(
      new RecordingHook()));
    Assert.Throws<ObjectDisposedException>(() => runtime.UnregisterHook<UnitMovedBattleEvent>(hook));

    // Disposal is idempotent; the second call must not mask a broken first cleanup above.
    runtime.Dispose();
  }

  // The runtime's re-raise fires before its executor's hook reactions, so scene subscribers
  // on Runtime.BattleEventCommitted see the cause before any hook-born follow-up — the
  // order this test pins.
  [TestCase(TestName = "Scene subscribers observe a cause event before hook-born follow-up events")]
  public static void SceneSubscribersObserveCauseBeforeHookFollowUps()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0), health: 10);
    var runtime = battle.Runtime;
    var unit = battle.Unit;

    var observed = new List<BattleEvent>();
    runtime.BattleEventCommitted += observed.Add;
    runtime.RegisterHook<UnitMovedBattleEvent>(new DamageMoverOnMovedHook());

    runtime.ExecuteAction(BattleAction.MoveUnit(
      battle.Alive(unit), [battle.Board.At(1, 0, 0)]));

    int causeIndex = observed.EventIndex<UnitMovedBattleEvent>();
    int followUpIndex = observed.EventIndex<UnitDamagedBattleEvent>();
    Assert.True(causeIndex >= 0);
    Assert.True(followUpIndex > causeIndex);
  }

  private sealed class DamageMoverOnMovedHook : BattleHook<UnitMovedBattleEvent>
  {
    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, UnitMovedBattleEvent evt)
      => [BattleAction.ApplyDamage(context.Session.TryGetAlive(evt.Unit).RequireSome(), 3)];
  }
}
