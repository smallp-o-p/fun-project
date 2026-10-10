using System;
using FunProject.Combatants;
using FunProject.Weapons;

namespace FunProject.Battle;

// Owns synchronous automation only. Presentation and committed-event callbacks have no
// execution role; completion observers finish before every fresh policy decision.
internal sealed class EnemyTurnController(BattleRuntime runtime)
{
  private EnemyPolicy? _policy;
  private Faction? _player;
  private bool _stopped;

  internal void Enable(Faction player, EnemyPolicySettings? settings)
  {
    if (_policy is not null) throw new InvalidOperationException("Enemy turns are already enabled.");
    if (_stopped) throw new InvalidOperationException("Cannot enable enemy turns after an execution fault.");
    _player = player;
    _policy = new(settings);
  }

  internal void Stop() => _stopped = true;

  internal void ExecuteEnemyTurns()
  {
    if (_policy is null || _stopped) return;
    try
    {
      Faction? side = null;
      int turn = 0;
      var memory = new EnemyTurnMemory();
      while (!_stopped && runtime.Query(new GetBattleOutcomeQuery()).IsNone)
      {
        var active = runtime.Query(new GetActiveSideQuery());
        if (ReferenceEquals(active, _player)) return;
        int currentTurn = runtime.Query(new GetTurnNumberQuery());
        if (side is not null && (!ReferenceEquals(side, active) || turn != currentTurn)) memory = new();
        side = active;
        turn = currentTurn;
        var intent = _policy.Decide(runtime, side, memory);
        bool trackProgress = intent.Kind is EnemyIntentKind.Attack or EnemyIntentKind.Reload or EnemyIntentKind.Move;
        var beforeOwn = trackProgress ? OwnProgress(side) : [];
        var beforeHostiles = trackProgress ? HostileProgress(side) : [];
        runtime.ExecuteSingleAction(MintAction(side, intent));
        // Completion observers can dispose the runtime; do not query after shutdown.
        if (!_stopped && trackProgress && beforeOwn.AsValueEnumerable().SequenceEqual(OwnProgress(side))
          && beforeHostiles.AsValueEnumerable().SequenceEqual(HostileProgress(side))) memory.MarkFailed(intent);
      }
    }
    catch { Stop(); throw; }
  }

  private BattleAction MintAction(Faction side, EnemyIntent intent)
  {
    if (intent.Kind == EnemyIntentKind.EndFactionTurn) return BattleAction.EndFactionTurn(side);
    // Mint submission proofs after the decision; policy previews retain no live handles.
    var actor = runtime.Query(new GetFactionAliveUnits(side)).AsValueEnumerable()
      .Single(unit => unit.Id == intent.ActorId);
    switch (intent.Kind)
    {
      case EnemyIntentKind.Attack:
        var target = runtime.Query(new GetVisibleHostileUnitsForFaction(side)).AsValueEnumerable()
          .Single(unit => unit.Id == intent.TargetId);
        return BattleAction.AttackEntity(actor, runtime.TryGetAttackTarget(new BattleEntity.Unit(target.State))
          .Match(value => value, () => throw new InvalidOperationException("A fresh visible target must be alive.")));
      case EnemyIntentKind.Reload:
        return BattleAction.ReloadWeapon(actor, (AmmunitionedWeapon)actor.State.RequireEquippedWeapon());
      case EnemyIntentKind.Move:
        return BattleAction.MoveUnit(actor, [runtime.TryGetTile(intent.Destination).Match(value => value,
          () => throw new InvalidOperationException("A permitted neighboring tile must be in bounds."))]);
      case EnemyIntentKind.Pass:
        return BattleAction.PassUnit(actor);
      default:
        throw new InvalidOperationException("Unknown enemy intent.");
    }
  }

  // Only unit values cross a submission. A callback can change another known unit,
  // allowing an interrupted intent to be reconsidered. Terrain is never copied here.
  private (int Id, Vector3I Position, int Health, int Stun, int Ap, bool CanAct, bool CanAttack,
    bool CanReload, int Range, int HealthDamage, int StunDamage, int Ammo)[] OwnProgress(Faction side)
    => runtime.Query(new GetFactionAliveUnits(side)).AsValueEnumerable().OrderBy(unit => unit.Id).Select(unit =>
    {
      var weapon = unit.State.EquippedWeapon;
      long health = 0, stun = 0;
      foreach (var damage in weapon.Match(value => value.EmitDamage(unit.State.ActiveBuffDamageMods), () => []))
        if (damage.Kind == DamageKind.Health) health += damage.Amount;
        else if (damage.Kind == DamageKind.Stun) stun += damage.Amount;
      bool canAct = runtime.Query(new CanUnitActNow(unit.State));
      bool canAttack = canAct && unit.State.CurrentActionPoints >= BattleSession.DefaultAttackActionPointCost
        && weapon.Match(value => value.IsLoaded, () => false);
      bool canReload = canAct && unit.State.CurrentActionPoints >= BattleSession.DefaultReloadActionPointCost
        && weapon.Match(value => value is AmmunitionedWeapon magazine && magazine.CurrentAmmo == 0 && magazine.CanReload(), () => false);
      return (unit.Id, unit.Position.Raw, unit.State.CurrentHealth, unit.State.CurrentStun, unit.State.CurrentActionPoints,
        canAct, canAttack, canReload, weapon.Match(value => value.EffectiveRange, () => 0),
        (int)Math.Min(health, int.MaxValue), (int)Math.Min(stun, int.MaxValue),
        weapon.Match(value => value is AmmunitionedWeapon magazine ? magazine.CurrentAmmo : -1, () => -1));
    }).ToArray();

  private (int Id, Vector3I Position, int Health, int Stun)[] HostileProgress(Faction side)
    => runtime.Query(new GetVisibleHostileUnitsForFaction(side)).AsValueEnumerable().Where(unit => !unit.State.IsUnconscious)
      .OrderBy(unit => unit.Id).Select(unit => (unit.Id, unit.Position.Raw, unit.State.CurrentHealth, unit.State.CurrentStun)).ToArray();
}
