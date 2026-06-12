using System;
using FunProject.Items.Effects;

namespace FunProject.Battle;

/// <summary>
/// Mutable per-unit instance of an authored status effect spec. Identity is the spec
/// resource itself: re-applying the same spec refreshes this instance instead of
/// stacking a second one.
/// </summary>
public sealed class ActiveStatusEffect
{
  public StatusEffectSpecData Spec { get; }
  public int RemainingTurns { get; private set; }
  public bool IsExpired => RemainingTurns <= 0;

  internal ActiveStatusEffect(StatusEffectSpecData spec)
  {
    ArgumentNullException.ThrowIfNull(spec);
    Spec = spec;
    RemainingTurns = Math.Max(1, spec.DurationTurns);
  }

  internal void Refresh()
  {
    RemainingTurns = Math.Max(RemainingTurns, Math.Max(1, Spec.DurationTurns));
  }

  internal void TickDown()
  {
    if (RemainingTurns <= 0)
      throw new InvalidOperationException("Cannot tick down an expired status effect.");
    RemainingTurns--;
  }
}
