using System.Collections.Generic;
using FunProject.Combatants;

namespace FunProject.Battle;

public readonly record struct LastSeenContact(int UnitId, Vector3I Position, long VisibilityRevision);
public readonly record struct ObservedUnitInfo(int UnitId, bool IsOwnSide, Vector3I Position, int Health, int ActionPoints, bool IsUnconscious);
public sealed record FactionUnitKnowledge(IReadOnlyList<ObservedUnitInfo> Current, IReadOnlyList<LastSeenContact> Remembered);

public sealed class GetFactionUnitKnowledge(Faction faction) : IBattleSessionQuery<FactionUnitKnowledge>
{
  public FactionUnitKnowledge Execute(BattleSession session)
  {
    var visible = new GetVisibleUnitsForFaction(faction).Execute(session);
    var current = visible.AsValueEnumerable().OrderBy(unit => unit.State.Id).Select(unit =>
      new ObservedUnitInfo(unit.State.Id, unit.State.Side == faction, unit.Position.Raw,
        unit.State.CurrentHealth, unit.State.CurrentActionPoints, unit.State.IsUnconscious)).ToArray();
    var currentIds = current.AsValueEnumerable().Select(unit => unit.UnitId).ToHashSet();
    var remembered = session.ObservationHistory.GetContacts(faction).AsValueEnumerable()
      .Where(contact => !currentIds.Contains(contact.UnitId)).ToArray();
    return new(System.Array.AsReadOnly(current), System.Array.AsReadOnly(remembered));
  }
}

public sealed class GetFactionLastSeenContacts(Faction faction) : IBattleSessionQuery<IReadOnlyList<LastSeenContact>>
{
  public IReadOnlyList<LastSeenContact> Execute(BattleSession session) => session.ObservationHistory.GetContacts(faction);
}

public sealed class GetHasSeenHostile(AliveUnit observer) : IBattleSessionQuery<bool>
{
  public bool Execute(BattleSession session) => observer.State.HasSeenHostile;
}
