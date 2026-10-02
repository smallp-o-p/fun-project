using System;
using System.Collections.Generic;
using FunProject.Stats;
using Godot;

namespace FunProject.Battle;

// Flat-subtraction model: base chance is the attacker's aim; applicable
// cover subtracts its amount. Future contributions (defense stats, unit
// attributes) become additional modifier entries or a replacement calculator.
public sealed class StandardHitChanceCalculator : IHitChanceCalculator
{
  public const string CoverModifierLabel = "Cover";

  public HitChanceBreakdown Calculate(AttackContext context)
  {
    ArgumentNullException.ThrowIfNull(context);

    int baseChance = context.Attacker.TryEffectiveStat<AimStat>().Match(
      value => Mathf.RoundToInt(value),
      () => 0);

    List<HitChanceModifier> modifiers = [];
    TileCover cover = context.Board.GetTile(context.DefenderPosition).Cover;
    CoverDirections approach = CoverRules.GetApproach(context.AttackerPosition.Raw, context.DefenderPosition.Raw);
    int coverAmount = CoverRules.GetAmount(cover, approach);
    if (coverAmount > 0)
      modifiers.Add(new HitChanceModifier(CoverModifierLabel, -coverAmount));

    return new HitChanceBreakdown(baseChance, modifiers);
  }
}
