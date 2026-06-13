using FunProject.Combatants;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

// One faction's mission: a FIFO queue of objectives. The active objective is the
// front of the queue. Sequential semantics with a halt-on-failure: completing the
// last objective completes the operation; failing the current one halts it (Failed)
// until a new objective is added, which reactivates the operation.
public sealed class Operation
{
  private readonly Queue<Objective> _objectives = new();

  public Faction Faction { get; }
  public OperationStatus Status { get; private set; } = OperationStatus.Active;

  public Operation(Faction faction)
  {
    ArgumentNullException.ThrowIfNull(faction);
    Faction = faction;
  }

  public Option<Objective> Current => _objectives.Count > 0 ? Some(_objectives.Peek()) : None;
  public IReadOnlyCollection<Objective> PendingObjectives => _objectives;

  public void AddObjective(Objective objective)
  {
    ArgumentNullException.ThrowIfNull(objective);
    objective.Owner = Faction;
    _objectives.Enqueue(objective);
    if (Status != OperationStatus.Active)
      Status = OperationStatus.Active;
  }

  public void CompleteCurrent()
  {
    if (_objectives.Count == 0)
      throw new InvalidOperationException("No current objective to complete.");
    _objectives.Dequeue();
    if (_objectives.Count == 0)
      Status = OperationStatus.Completed;
  }

  public void FailCurrent()
  {
    if (_objectives.Count == 0)
      throw new InvalidOperationException("No current objective to fail.");
    _objectives.Dequeue();
    Status = OperationStatus.Failed;
  }
}
