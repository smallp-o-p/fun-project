using System;

namespace FunProject.Battle;

/// <summary>Hold through every use of borrowed results, synchronously on the acquiring
/// thread. Never transfer this scope or retain it across an await.</summary>
public sealed class BattleQueryScope : IDisposable
{
  private readonly BattleAccessGuard _access;
  private readonly int _thread = Environment.CurrentManagedThreadId;
  private bool _disposed;
  public long Revision { get; }
  internal BattleQueryScope(BattleAccessGuard access, long revision) { _access = access; Revision = revision; }
  public void Dispose()
  {
    if (Environment.CurrentManagedThreadId != _thread)
      throw new InvalidOperationException("A battle query scope must be released on its acquiring thread.");
    if (_disposed) return;
    _access.ExitRead();
    _disposed = true;
  }
}
