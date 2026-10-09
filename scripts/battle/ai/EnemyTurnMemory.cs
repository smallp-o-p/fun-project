using System.Collections.Generic;

namespace FunProject.Battle;

// Activation-local, detached memory. The controller replaces this when faction/turn changes.
public sealed class EnemyTurnMemory
{
  private readonly Dictionary<int, SysColGeneric.HashSet<Vector3I>> _visited = [];
  private readonly Dictionary<int, long> _dismissedContacts = [];
  private readonly SysColGeneric.HashSet<EnemyIntent> _suppressed = [];

  public void Visit(int actorId, Vector3I position)
  {
    if (!_visited.TryGetValue(actorId, out var visited)) _visited.Add(actorId, visited = []);
    visited.Add(position);
  }

  public bool HasVisited(int actorId, Vector3I position)
    => _visited.TryGetValue(actorId, out var visited) && visited.Contains(position);
  public bool IsDismissed(LastSeenContact contact)
    => _dismissedContacts.TryGetValue(contact.UnitId, out long revision) && revision == contact.VisibilityRevision;
  public bool IsSuppressed(EnemyIntent intent) => _suppressed.Contains(intent);
  public void Suppress(EnemyIntent intent) => _suppressed.Add(intent);

  public void Observe(EnemyDecisionFrame frame)
  {
    foreach (var own in frame.OwnUnits) Visit(own.UnitId, own.Position);
    foreach (var contact in frame.Contacts)
      if (frame.Tiles.AsValueEnumerable().Any(tile => tile.Position == contact.Position && tile.Visible)
        && !frame.Hostiles.AsValueEnumerable().Any(hostile => hostile.UnitId == contact.UnitId))
        _dismissedContacts[contact.UnitId] = contact.VisibilityRevision;
  }
}
