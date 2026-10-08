using System;
using System.Threading;
using System.Threading.Tasks;
using FunProject.Battle;
using GdUnit4;
using Godot;
using static FunProject.Tests.GeoscapeTestScenes;

[TestSuite]
[RequireGodotRuntime]
public sealed partial class BattleRuntimeDrainTest
{
  private static async Task Frames(int count = 3)
  {
    var tree = (SceneTree)Engine.GetMainLoop();
    for (int i = 0; i < count; i++) await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
  }

  [TestCase]
  public async Task HostRemovalRetainsAdmittedWorkerDependencies()
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 1), Vector3I.Zero);
    var scene = new BattleScene();
    scene.Present(battle.Runtime, new BattleSetup(TestData.MakeOpenBattleMap(new Vector3I(3, 1, 1)), [], Seed: 7));
    ((SceneTree)Engine.GetMainLoop()).Root.AddChild(scene);
    int drained = 0;
    using (BattleConcurrencyProbe.HoldQuery(battle.Runtime))
    {
      scene.Free();
      Assert.True(battle.Runtime.IsClosing);
      Assert.True(battle.PlayerUnit.Combatant.IsDeployedInBattle);
      Assert.False(battle.Runtime.TryFinishDisposal());
      BattleRuntimeDrainHost.Retain(battle.Runtime, () => drained++);
      await Frames();
      Assert.Equal(0, drained);
    }
    await Frames();
    Assert.Equal(1, drained);
    Assert.False(battle.PlayerUnit.Combatant.IsDeployedInBattle);
  }

  [TestCase]
  public async Task ReturnDrainsBeforeCampaignMutation()
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 1), Vector3I.Zero);
    int edits = 0;
    using (BattleConcurrencyProbe.HoldQuery(battle.Runtime))
    {
      BattleRuntimeDrainHost.Retain(battle.Runtime, () =>
      {
        Assert.False(battle.PlayerUnit.Combatant.IsDeployedInBattle);
        battle.PlayerUnit.Combatant.Progression.AwardPoints(1); edits++;
      });
      await Frames();
      Assert.Equal(0, edits);
      Assert.Equal(BattleAccessFailure.Closed, battle.Runtime.TryExecuteAction(_ => throw new Exception()).RequireLeft());
    }
    await Frames();
    Assert.Equal(1, edits);
  }

  [TestCase]
  public async Task CallbackCloseCannotTeardownReentrantly()
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 1), Vector3I.Zero);
    int drained = 0;
    battle.Runtime.ActionCompleted += _ =>
    {
      BattleRuntimeDrainHost.Retain(battle.Runtime, () => drained++);
      Assert.False(battle.Runtime.TryFinishDisposal());
      Assert.Equal(0, drained);
    };
    battle.AdvanceTurn();
    await Frames();
    Assert.Equal(1, drained);
  }

  [TestCase]
  public async Task CancelledCallbackDoesNotBlockFrame()
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 1), Vector3I.Zero);
    using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
    using var registration = battle.Runtime.ClosingToken.Register(() => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); });
    int drained = 0;
    try
    {
      BattleRuntimeDrainHost.Retain(battle.Runtime, () => drained++);
      Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
      await Frames();
      Assert.False(battle.Runtime.TryFinishDisposal());
      Assert.Equal(0, drained);
    }
    finally { release.Set(); }
    await Frames(5);
    Assert.Equal(1, drained);
  }

  [TestCase]
  public async Task CancellationFaultStillRunsRetainedCleanup()
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 1), Vector3I.Zero);
    using var registration = battle.Runtime.ClosingToken.Register(() => throw new InvalidOperationException("cancel fault"));
    using (BattleConcurrencyProbe.HoldQuery(battle.Runtime))
    {
      battle.Runtime.RequestClose();
      await Frames(5);
    }
    int drained = 0;
    Assert.Throws<AggregateException>(() => BattleRuntimeDrainHost.Retain(battle.Runtime, () => drained++));
    Assert.False(battle.PlayerUnit.Combatant.IsDeployedInBattle);
    Assert.Equal(1, drained);
  }

  [TestCase]
  public async Task FaultStillAllowsFinalDrain()
  {
    await using var cleanup = new DeferredNodeCleanup();
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 1), Vector3I.Zero);
    Assert.Throws<InvalidOperationException>(() => battle.Runtime.TryExecuteAction(_ => throw new InvalidOperationException()));
    int drained = 0;
    BattleRuntimeDrainHost.Retain(battle.Runtime, () => drained++);
    await Frames();
    Assert.Equal(1, drained);
  }
}
