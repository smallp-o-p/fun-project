using System;
using System.Threading;

namespace FunProject.Battle;

// One exclusive gate for runtime state, including permitted lazy cache publication.
internal sealed class BattleAccessGuard
{
  private readonly object _gate = new();
  private readonly int _hostThread = Environment.CurrentManagedThreadId;
  private int _closing;
  private int _readDepth;
  private int _mutationDepth;
  private bool _executing;
  private bool _disposed;
  private readonly SysColGeneric.HashSet<BattleAccessBinding> _sharedBindings = [];
  internal long Revision { get; private set; }
  internal bool IsClosing => Volatile.Read(ref _closing) != 0;
  internal bool IsOwned => Monitor.IsEntered(_gate);
  internal bool IsExecuting => IsOwned && _executing;
  internal bool RequestClose() => Interlocked.Exchange(ref _closing, 1) == 0;

  private Option<BattleAccessFailure> TryEnter()
  {
    bool owned = IsOwned;
    if (!owned && IsClosing) return Some(BattleAccessFailure.Closed);
    if (!Monitor.TryEnter(_gate)) return Some(BattleAccessFailure.Busy);
    if (_disposed || (!owned && IsClosing))
    {
      Monitor.Exit(_gate);
      return Some(BattleAccessFailure.Closed);
    }
    return None;
  }

  internal Either<BattleAccessFailure, BattleQueryScope> TryRead()
  {
    Option<BattleAccessFailure> failure = TryEnter();
    if (failure.IsSome) return Left(failure.IfNone(BattleAccessFailure.Closed));
    _readDepth++;
    return Right(new BattleQueryScope(this, Revision));
  }

  internal void ExitRead()
  {
    RequireReadAccess();
    _readDepth--;
    Monitor.Exit(_gate);
  }

  internal Option<BattleAccessFailure> TryMutation(bool execution)
  {
    RequireHostThread();
    Option<BattleAccessFailure> failure = TryEnter();
    if (failure.IsSome) return failure;
    if (_readDepth != 0 || (execution && _mutationDepth != 0))
    {
      Monitor.Exit(_gate);
      throw new InvalidOperationException("Read scopes cannot mutate; submissions cannot nest.");
    }
    _mutationDepth++;
    if (execution) _executing = true;
    return None;
  }

  internal void ExitMutation(bool execution)
  {
    RequireMutationAccess();
    if (execution) _executing = false;
    _mutationDepth--;
    Monitor.Exit(_gate);
  }

  internal void AdvanceRevision()
  {
    RequireMutationAccess();
    Revision = checked(Revision + 1);
  }

  internal void RequireReadAccess()
  {
    ObjectDisposedException.ThrowIf(_disposed, this);
    if (!IsOwned) throw new InvalidOperationException("Borrowed battle state requires an owned access scope.");
  }

  internal void RequireMutationAccess()
  {
    RequireReadAccess();
    if (_readDepth != 0 || _mutationDepth == 0)
      throw new InvalidOperationException("Battle mutation requires execution ownership outside a query.");
  }

  internal void RequireHostThread()
  {
    if (Environment.CurrentManagedThreadId != _hostThread)
      throw new InvalidOperationException("Battle execution and final teardown belong to the host thread.");
  }

  internal bool TryEnterDisposal()
  {
    RequireHostThread();
    return !IsOwned && Monitor.TryEnter(_gate);
  }
  internal void FinishDisposal() { _disposed = true; Monitor.Exit(_gate); }
  internal void ExitDisposal() => Monitor.Exit(_gate);

  internal void RequireDrainAccess()
  {
    RequireHostThread();
    RequireReadAccess();
    if (!IsClosing || _readDepth != 0 || _mutationDepth != 0)
      throw new InvalidOperationException("Unbinding requires final drain ownership.");
  }

  internal void RetainSharedBinding(BattleAccessBinding binding)
  {
    RequireMutationAccess();
    _sharedBindings.Add(binding);
  }

  internal void UnbindSharedState()
  {
    RequireDrainAccess();
    foreach (var binding in _sharedBindings) binding.Unbind(this);
    _sharedBindings.Clear();
  }
}

// Binding shares the runtime's gate; it owns no synchronization mechanism.
internal sealed class BattleAccessBinding(bool shared = false)
{
  internal BattleAccessGuard? Guard { get; private set; }
  internal void Bind(BattleAccessGuard access)
  {
    access.RequireMutationAccess();
    if (Guard is not null && !ReferenceEquals(Guard, access))
      throw new InvalidOperationException("Mutable state is already deployed in another battle.");
    Guard = access;
    if (shared) access.RetainSharedBinding(this);
  }
  internal void Unbind(BattleAccessGuard access)
  {
    if (!ReferenceEquals(Guard, access)) return;
    access.RequireDrainAccess();
    Guard = null;
  }
  internal void RequireRead() => Guard?.RequireReadAccess();
  internal void RequireMutation() => Guard?.RequireMutationAccess();
}
