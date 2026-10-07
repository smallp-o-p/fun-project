using System;
using FunProject.Items.Effects;

namespace FunProject.Battle;

/// <summary>
/// Mutable per-unit instance of an authored status effect spec. Identity is the spec
/// resource itself: re-applying the same spec refreshes this instance instead of
/// stacking a second one. Behavior is dispatched polymorphically by concrete subclass
/// (built via <see cref="Create"/>) rather than by type-switching on the spec at each
/// consumer site.
/// </summary>
public abstract class ActiveStatusEffect
{
  private readonly BattleAccessBinding _battleAccess = new();
  internal void BindBattleAccess(BattleAccessGuard access) { _battleAccess.Bind(access);  }
  internal void UnbindBattleAccess(BattleAccessGuard access) => _battleAccess.Unbind(access);

  public StatusEffectSpecData Spec { get; }
  private int _remainingTurns;
  public int RemainingTurns
  {
    get { _battleAccess.RequireRead(); return _remainingTurns; }
    private set { _battleAccess.RequireMutation(); _remainingTurns = value; }
  }
  public bool IsExpired => RemainingTurns <= 0;

  protected ActiveStatusEffect(StatusEffectSpecData spec)
  {
    ArgumentNullException.ThrowIfNull(spec);
    Spec = spec;
    RemainingTurns = Math.Max(1, spec.DurationTurns);
  }

  /// <summary>
  /// Maps an authored spec to its runtime status-effect subclass. This is the single,
  /// localized Data-&gt;Runtime switch; consumers dispatch through the polymorphic hooks
  /// below instead of inspecting the spec type.
  /// </summary>
  internal static ActiveStatusEffect Create(StatusEffectSpecData spec)
  {
    ArgumentNullException.ThrowIfNull(spec);
    return spec switch
    {
      DamageOverTimeStatusSpecData dot => new DamageOverTimeStatusEffect(dot),
      ImmobilizeStatusSpecData immobilize => new ImmobilizeStatusEffect(immobilize),
      _ => throw new ArgumentException(
        $"No runtime status effect mapped for spec type {spec.GetType().Name}.",
        nameof(spec)),
    };
  }

  /// <summary>Hook invoked at the end of the owning faction's turn. Default no-op.</summary>
  internal virtual void OnFactionTurnEnd(BattleSession session, BattleUnitState unit) { }

  /// <summary>Whether this effect prevents the owning unit from acting while active.</summary>
  internal virtual bool BlocksAction => false;

  internal void Refresh()
  {
    _battleAccess.RequireMutation();
    RemainingTurns = Math.Max(RemainingTurns, Math.Max(1, Spec.DurationTurns));
  }

  internal void TickDown()
  {
    _battleAccess.RequireMutation();
    if (RemainingTurns <= 0)
      throw new InvalidOperationException("Cannot tick down an expired status effect.");
    RemainingTurns--;
  }
}
