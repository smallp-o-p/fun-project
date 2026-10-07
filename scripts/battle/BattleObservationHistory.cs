using System;
using System.Collections.Generic;
using FunProject.Combatants;

namespace FunProject.Battle;

internal sealed class BattleObservationHistory
{
  private readonly BattleAccessBinding _access = new();
  private readonly Dictionary<Faction, Dictionary<int, LastSeenContact>> _contacts = [];
  private long _revision;
  internal void BindBattleAccess(BattleAccessGuard access) => _access.Bind(access);

  internal void RecordRefresh(BattleBoardState board, IReadOnlyList<BattleUnitState> units)
  {
    _access.RequireMutation();
    _revision = checked(_revision + 1);
    foreach (var observer in units)
    {
      if (!observer.IsAlive || observer.IsUnconscious) continue;
      foreach (var target in observer.VisibleUnits)
      {
        if (target.Side == observer.Side) continue;
        observer.RecordHostileContact();
        if (!_contacts.TryGetValue(observer.Side, out var contacts)) _contacts.Add(observer.Side, contacts = []);
        var position = board.FindOccupantPosition(target.Id).Match(point => point.Raw,
          () => throw new InvalidOperationException("Visible living target must be board indexed."));
        contacts[target.Id] = new(target.Id, position, _revision);
      }
    }
  }

  internal IReadOnlyList<LastSeenContact> GetContacts(Faction faction)
  {
    _access.RequireRead();
    ArgumentNullException.ThrowIfNull(faction);
    return _contacts.TryGetValue(faction, out var contacts)
      ? System.Array.AsReadOnly(contacts.Values.AsValueEnumerable().OrderBy(contact => contact.UnitId).ToArray()) : [];
  }
}
