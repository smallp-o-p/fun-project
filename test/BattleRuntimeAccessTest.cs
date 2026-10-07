using System;
using System.Threading;
using FunProject.Battle;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public sealed class BattleRuntimeAccessTest
{
  [TestCase]
  public void WorkerScopeExcludesQueriesAndExecutionWithoutWaiting()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    using (var worker = BattleConcurrencyProbe.HoldQuery(battle.Runtime))
    {
      Assert.Equal(BattleAccessFailure.Busy, battle.Runtime.TryQuery(new GetBattleRevisionQuery()).RequireLeft());
      int factoryCalls = 0;
      Assert.Equal(BattleAccessFailure.Busy, battle.Runtime.TryExecuteAction(_ =>
      { factoryCalls++; return BattleAction.EndFactionTurn(battle.PlayerFaction); }).RequireLeft());
      Assert.Equal(0, factoryCalls);
    }
    Assert.True(battle.Runtime.TryQuery(new GetBattleRevisionQuery()).IsRight);
  }

  [TestCase]
  public void WrongThreadDisposeLeavesOwnershipIntact()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    using var scope = battle.Runtime.TryAcquireQueryScope().RequireRight();
    Exception? failure = null;
    var worker = new Thread(() =>
    {
      try { scope.Dispose(); }
      catch (Exception error) { failure = error; }
    });
    worker.Start();
    Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
    Assert.True(failure is InvalidOperationException);
    Assert.True(battle.Runtime.TryQuery(new GetBattleRevisionQuery()).IsRight);
    Assert.Throws<InvalidOperationException>(() => battle.AdvanceTurn());
    scope.Dispose();
    scope.Dispose();
    battle.AdvanceTurn();
  }

  [TestCase]
  public void StaleRevisionSkipsActionFactory()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    long revision = battle.Query(new GetBattleRevisionQuery());
    battle.AdvanceTurn();
    int calls = 0;
    Assert.Equal(BattleAccessFailure.StaleRevision, battle.Runtime.TryExecuteAction(_ =>
    { calls++; return BattleAction.EndFactionTurn(battle.PlayerFaction); }, Some(revision)).RequireLeft());
    Assert.Equal(0, calls);
    Assert.Equal(revision + 1, battle.Query(new GetBattleRevisionQuery()));
  }

  [TestCase]
  public void FaultAndCancellationReleaseOwnership()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    long before = battle.Query(new GetBattleRevisionQuery());
    int completed = 0;
    battle.Runtime.ActionCompleted += _ => completed++;
    Assert.Throws<OperationCanceledException>(() => battle.Runtime.TryExecuteAction(_ => throw new OperationCanceledException()));
    Assert.Equal(before + 1, battle.Query(new GetBattleRevisionQuery()));
    Assert.Equal(0, completed);
    Assert.Throws<InvalidOperationException>(() => battle.Query(new DelegateQuery(() => throw new InvalidOperationException())));
    using (BattleConcurrencyProbe.HoldQuery(battle.Runtime))
      Assert.True(battle.Runtime.TryQuery(new GetBattleRevisionQuery()).IsLeft);
    battle.AdvanceTurn();
  }

  [TestCase]
  public void CloseRejectsNewAdmissionButPreservesAdmittedReads()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    using (var worker = BattleConcurrencyProbe.HoldQuery(battle.Runtime))
    {
      battle.Runtime.RequestClose();
      Assert.True(battle.Runtime.ClosingToken.IsCancellationRequested);
      Assert.Equal(BattleAccessFailure.Closed, battle.Runtime.TryQuery(new GetBattleRevisionQuery()).RequireLeft());
      Assert.False(battle.Runtime.TryFinishDisposal());
    }
    Assert.True(battle.Runtime.TryFinishDisposal());
    Assert.Equal(BattleAccessFailure.Closed, battle.Runtime.TryAcquireQueryScope().RequireLeft());
  }

  [TestCase]
  public void OwnerCallbacksReadBeforeAndAfterCleanup()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0), actionPoints: 4);
    battle.Mutate(() => battle.Unit.TrySpendActionPoints(4));
    int during = -1;
    int after = -1;
    battle.Runtime.BattleEventCommitted += evt =>
    {
      if (evt is TurnStartedBattleEvent) during = battle.Unit.CurrentActionPoints;
    };
    battle.Runtime.ActionCompleted += _ =>
    {
      after = battle.Unit.CurrentActionPoints;
      using var scope = battle.Runtime.TryAcquireQueryScope().RequireRight();
      Assert.Equal(BattleAccessFailure.Busy, ReadFromOtherThread(battle.Runtime));
    };
    battle.AdvanceTurn();
    Assert.Equal(0, during);
    Assert.Equal(4, after);
  }

  private static BattleAccessFailure ReadFromOtherThread(BattleRuntime runtime)
  {
    BattleAccessFailure failure = default;
    var worker = new Thread(() => failure = runtime.TryQuery(new GetBattleRevisionQuery()).RequireLeft());
    worker.Start();
    Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
    return failure;
  }

  [TestCase]
  public void ReadScopeCannotMutateOrSubmit()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    Assert.Throws<InvalidOperationException>(() => battle.Query(new DelegateQuery(() =>
      battle.Runtime.ExecuteAction(BattleAction.EndFactionTurn(battle.PlayerFaction)))));
  }

  [TestCase]
  public void OwnerCallbacksCannotSubmitNestedActions()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    bool attempted = false;
    battle.Runtime.ActionStarted += _ =>
    {
      if (attempted) return;
      attempted = true;
      Assert.Throws<InvalidOperationException>(() =>
        battle.Runtime.ExecuteAction(BattleAction.EndFactionTurn(battle.PlayerFaction)));
    };
    battle.AdvanceTurn();
  }

  [TestCase]
  public void ClosePreservesAlreadyAdmittedRead()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    int result = battle.Query(new DelegateQuery(() =>
    {
      battle.Runtime.Dispose();
      Assert.True(battle.Query(new GetBattleOutcomeQuery()).IsNone);
    }));
    Assert.Equal(1, result);
    Assert.Throws<ObjectDisposedException>(() => battle.Query(new GetBattleOutcomeQuery()));
  }

  [TestCase]
  public void WorkerQueryExcludesOtherQueriesWithoutWaiting()
  {
    using var battle = BattleFixture.Solo(new Vector3I(3, 1, 3), new Vector3I(0, 0, 0));
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    Exception? workerFailure = null;
    var worker = new Thread(() =>
    {
      try { battle.Runtime.Query(new HeldQuery(entered, release)); }
      catch (Exception error) { workerFailure = error; }
    });
    worker.Start();
    try
    {
      Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "Worker must enter the query.");
      Assert.Throws<InvalidOperationException>(() => battle.Query(new GetBattleOutcomeQuery()));
    }
    finally
    {
      release.Set();
      Assert.True(worker.Join(TimeSpan.FromSeconds(5)), "Worker must release.");
    }
    Assert.True(workerFailure is null, workerFailure?.ToString() ?? "");
    Assert.True(battle.Query(new GetBattleOutcomeQuery()).IsNone);
  }

  private sealed class HeldQuery(ManualResetEventSlim entered, ManualResetEventSlim release)
    : IBattleSessionQuery<int>
  {
    public int Execute(BattleSession session)
    {
      entered.Set();
      if (!release.Wait(TimeSpan.FromSeconds(5)))
        throw new TimeoutException("Held query was not released.");
      return 1;
    }
  }

  private sealed class DelegateQuery(Action run) : IBattleSessionQuery<int>
  {
    public int Execute(BattleSession session) { run(); return 1; }
  }
}
