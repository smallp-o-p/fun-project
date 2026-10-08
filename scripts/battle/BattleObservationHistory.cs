using System;
using System.Collections.Generic;
using FunProject.Combatants;

namespace FunProject.Battle;

// Observations are detached values: retaining a live unit would expose later unseen changes
// and require a battle read scope when guarded worker access is enabled.
public readonly record struct LastSeenContact(int UnitId, Vector3I Position, long VisibilityRevision);
public readonly record struct ObservedUnitInfo(int UnitId, bool IsOwnSide, Vector3I Position, int Health, int ActionPoints, bool IsUnconscious);
public sealed record FactionUnitKnowledge(IReadOnlyList<ObservedUnitInfo> Current, IReadOnlyList<LastSeenContact> Remembered);

internal sealed class BattleObservationHistory
{
  private readonly Dictionary<Faction, Dictionary<int, LastSeenContact>> _contacts = [];
  private readonly Dictionary<Faction, Dictionary<BattleBoardState.ValidatedPoint, BattleTileState>> _terrain = [];
  private long _revision;

  internal void RecordRefresh(BattleBoardState board, IReadOnlyList<BattleUnitState> units)
  {
    _revision = checked(_revision + 1);
    foreach (var observer in units.AsValueEnumerable().Where(observer => observer.IsAlive && !observer.IsUnconscious))
    {
      if (!_terrain.TryGetValue(observer.Side, out var terrain)) _terrain.Add(observer.Side, terrain = []);
      foreach (var point in observer.VisibleTiles) terrain[point] = board.GetTile(point);
      foreach (var target in observer.VisibleUnits.AsValueEnumerable().Where(target => target.Side != observer.Side))
      {
        observer.RecordHostileContact();
        if (!_contacts.TryGetValue(observer.Side, out var contacts)) _contacts.Add(observer.Side, contacts = []);
        var position = board.FindOccupantPosition(target.Id).Match(point => point.Raw,
          () => throw new InvalidOperationException("Visible living target must be board indexed."));
        contacts[target.Id] = new(target.Id, position, _revision);
      }
    }
  }

  internal Option<BattleTileState> GetTerrain(Faction faction, BattleBoardState.ValidatedPoint tile)
  {
    return _terrain.TryGetValue(faction, out var terrain) && terrain.TryGetValue(tile, out var value) ? Some(value) : None;
  }

  internal IReadOnlyList<LastSeenContact> GetContacts(Faction faction)
  {
    ArgumentNullException.ThrowIfNull(faction);
    return _contacts.TryGetValue(faction, out var contacts)
      ? System.Array.AsReadOnly(contacts.Values.AsValueEnumerable().OrderBy(contact => contact.UnitId).ToArray()) : [];
  }
}
