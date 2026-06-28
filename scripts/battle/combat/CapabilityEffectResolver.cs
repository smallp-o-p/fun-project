using FunProject.Items.Effects;
using FunProject.Weapons;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

/// <summary>
/// Reusable, engine-agnostic core that applies a list of authored <see cref="BattleEffectData"/>
/// to every unit within a grid radius of an origin point, reusing the session's damage and
/// status pipelines. Friendly-fire is ON (X-COM style): the area effect hits ALL units in
/// range regardless of side. The affected-unit set is snapshotted before anything is applied,
/// because applying damage can kill and remove units mid-resolution.
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

    // Snapshot every alive unit within radius (Manhattan grid distance) of the origin
    // BEFORE applying anything; radius 0 = only the origin tile's occupant. Friendly-fire
    // is intentional, so no side filtering.
    List<BattleUnitState> affected = session.AliveUnits
      .Where(unit => session.GetUnitPosition(unit).Match(
        Some: point => BattleSession.GetGridDistance(point.Raw, origin.Raw) <= radius,
        None: () => false))
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
            session.ApplyDamageTo(unit, [new Damage(damage.BaseDamage, damage.Element)]);
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
  }
}
