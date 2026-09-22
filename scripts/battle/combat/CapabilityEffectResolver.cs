using FunProject.Items.Effects;
using FunProject.Weapons;
using System;
using System.Collections.Generic;

namespace FunProject.Battle;

/// <summary>
/// Reusable, engine-agnostic core that applies a list of authored <see cref="BattleEffectData"/>
/// to every unit and every live health-bearing object within a grid radius of an origin
/// point, reusing the session's damage and status pipelines. Friendly-fire is ON (X-COM
/// style): the area effect hits ALL units in range regardless of side. Both recipient sets
/// are snapshotted before anything is applied, because applying damage can kill and remove
/// units and destroy objects mid-resolution.
/// </summary>
public static class CapabilityEffectResolver
{
  public static void Resolve(
    BattleSession session,
    BattleBoardState.ValidatedPoint origin,
    int radius,
    IReadOnlyList<BattleEffectData> effects)
  {
    ArgumentNullException.ThrowIfNull(session);
    ArgumentNullException.ThrowIfNull(effects);
    if (effects.Count == 0)
      return;

    // Snapshot every recipient within radius (via BattleSession.GetGridDistance) of the
    // origin BEFORE applying anything; radius 0 = only the origin tile's occupant.
    // Friendly-fire is intentional, so no side filtering.
    List<BattleUnitState> affected = session.AliveUnits
      .AsValueEnumerable().Where(unit => session.GetUnitPosition(unit).Match(
        Some: point => BattleSession.GetGridDistance(point.Raw, origin.Raw) <= radius,
        None: () => false))
      .ToList();
    List<BattleObjectState> affectedObjects = session.Objects.AsValueEnumerable()
      .Where(obj => obj.Status.IsNone
        && obj.FindCapability<ObjectHealthCapability>().IsSome
        && BattleSession.GetGridDistance(obj.Position, origin.Raw) <= radius)
      .ToList();

    foreach (BattleUnitState unit in affected)
    {
      foreach (BattleEffectData effect in effects)
      {
        // An earlier effect can have killed this unit (which removes it from the board);
        // ApplyDamageTo throws on a dead/off-board unit, so re-check before each application.
        if (unit.IsDead || session.GetUnitPosition(unit).IsNone)
          break;

        switch (effect)
        {
          case DamageEffectData damage:
            session.ApplyDamageTo(unit, [new Damage(damage.BaseDamage, damage.Element)], None);
            break;
          case StatusEffectSpecData status:
            session.ApplyStatusEffectTo(unit, status);
            break;
          default:
            // TODO: other BattleEffectData subclasses (e.g. SpawnHazardEffectData,
            // TerrainEffectData, VisibilityEffectData) are not yet resolved. Add a
            // dispatch case here when their behavior is implemented.
            break;
        }
      }
    }

    foreach (BattleObjectState obj in affectedObjects)
    {
      foreach (BattleEffectData effect in effects)
      {
        // An earlier effect can have destroyed this object (which clears its board
        // occupancy); ApplyDamageTo throws on a non-live object, so re-check before each
        // application. Statuses are unit-only and never land on objects.
        if (session.TryGetAliveObject(obj).IsNone)
          break;

        if (effect is DamageEffectData damage)
          session.ApplyDamageTo(obj, [new Damage(damage.BaseDamage, damage.Element)], None);
      }
    }
  }
}
