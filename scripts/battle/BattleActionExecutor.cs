#nullable enable
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

public sealed class BattleActionExecutor
{
  private readonly BattleSession _session;
  private readonly Queue<BattleSessionMutation> _pending = [];

  public int PendingCount => _pending.Count;
  public bool IsBusy { get; private set; }
  public BattleSessionMutation? ActiveMutation { get; private set; }
  public BattleMutationResult? LastResult { get; private set; }

  public event Action<BattleSessionMutation>? MutationStarted;
  public event Action<BattleMutationResult>? MutationResolved;

  public BattleActionExecutor(BattleSession session)
  {
    _session = session ?? throw new ArgumentNullException(nameof(session));
  }

  public void Enqueue(BattleSessionMutation mutation)
  {
    ArgumentNullException.ThrowIfNull(mutation);
    _pending.Enqueue(mutation);
  }

  public void EnqueueRange(IEnumerable<BattleSessionMutation> mutations)
  {
    ArgumentNullException.ThrowIfNull(mutations);

    foreach (var mutation in mutations)
    {
      ArgumentNullException.ThrowIfNull(mutation);
      Enqueue(mutation);
    }
  }

  public BattleMutationResult? Tick()
  {
    if (IsBusy || _pending.Count == 0)
      return null;

    var activeMutation = _pending.Dequeue();
    ActiveMutation = activeMutation;
    IsBusy = true;

    BattleMutationResult result;
    try
    {
      MutationStarted?.Invoke(activeMutation);
      result = activeMutation.Execute(_session);
    }
    catch (Exception exception)
    {
      result = BattleMutationResult.Failure(
        activeMutation,
        BattleMutationFailureReason.UnexpectedError,
        exception.Message);
    }

    LastResult = result;
    ActiveMutation = null;
    IsBusy = false;
    MutationResolved?.Invoke(result);
    return result;
  }

  public IReadOnlyList<BattleMutationResult> DrainQueue(int maxActions = int.MaxValue)
  {
    if (maxActions <= 0)
      throw new ArgumentOutOfRangeException(nameof(maxActions));

    List<BattleMutationResult> results = [];
    while (_pending.Count > 0 && results.Count < maxActions)
    {
      var result = Tick();
      if (!result.HasValue)
        break;

      results.Add(result.Value);
    }

    return results;
  }
}
