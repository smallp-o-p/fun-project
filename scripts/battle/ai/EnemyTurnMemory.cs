using System.Collections.Generic;

namespace FunProject.Battle;

// Activation-local, detached memory. The controller replaces this when faction/turn changes.
internal sealed class EnemyTurnMemory
{
  private readonly Dictionary<int, SysColGeneric.HashSet<Vector3I>> _visited = [];
  private readonly Dictionary<int, long> _dismissedContacts = [];
  private readonly SysColGeneric.HashSet<EnemyIntent> _failedIntents = [];

  public void Visit(int actorId, Vector3I position)
  {
    if (!_visited.TryGetValue(actorId, out var visited)) _visited.Add(actorId, visited = []);
    visited.Add(position);
  }

  public bool HasVisited(int actorId, Vector3I position)
    => _visited.TryGetValue(actorId, out var visited) && visited.Contains(position);
  public bool IsDismissed(LastSeenContact contact)
    => _dismissedContacts.TryGetValue(contact.UnitId, out long revision) && revision == contact.VisibilityRevision;
  public bool HasFailed(EnemyIntent intent) => _failedIntents.Contains(intent);
  public void MarkFailed(EnemyIntent intent) => _failedIntents.Add(intent);

  public void Dismiss(LastSeenContact contact) => _dismissedContacts[contact.UnitId] = contact.VisibilityRevision;
}
