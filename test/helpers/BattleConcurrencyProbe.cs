using System;
using System.Threading;
using FunProject.Battle;

namespace FunProject.Tests;

public sealed class BattleConcurrencyProbe : IDisposable
{
  private readonly ManualResetEventSlim _entered = new();
  private readonly ManualResetEventSlim _release = new();
  private readonly Thread _worker;
  private Exception? _failure;
  private bool _disposed;

  private BattleConcurrencyProbe(BattleRuntime runtime)
  {
    _worker = new Thread(() =>
    {
      try
      {
        using var scope = runtime.TryAcquireQueryScope().RequireRight();
        _entered.Set();
        if (!_release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Worker was not released.");
        // An admitted owner remains readable even after close was requested.
        runtime.Query(new GetBattleRevisionQuery());
      }
      catch (Exception failure) { _failure = failure; _entered.Set(); }
    });
    _worker.Start();
    if (!_entered.Wait(TimeSpan.FromSeconds(5)) || _failure is not null)
    {
      Release();
      throw new InvalidOperationException("Worker scope admission failed.", _failure);
    }
  }

  public static BattleConcurrencyProbe HoldQuery(BattleRuntime runtime) => new(runtime);
  public void Release()
  {
    if (_disposed) return;
    _release.Set();
    if (!_worker.Join(TimeSpan.FromSeconds(5))) throw new TimeoutException("Worker did not drain.");
    _disposed = true;
    _entered.Dispose();
    _release.Dispose();
    if (_failure is not null) throw new InvalidOperationException("Worker failed.", _failure);
  }
  public void Dispose() => Release();
}
