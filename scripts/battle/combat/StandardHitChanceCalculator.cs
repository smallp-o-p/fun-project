using System;
using System.Collections.Generic;
using FunProject.Stats;

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

    int baseChance = context.Attacker.Combatant.TryGetStat<AimStat>().Match(
      stat => stat.BaseValue,
      () => 0);

    List<HitChanceModifier> modifiers = [];
    TileCover cover = context.Board.GetTile(context.DefenderPosition).Cover;
    CoverDirections approach = CoverRules.GetApproach(context.AttackerPosition.Raw, context.DefenderPosition.Raw);
    if (CoverRules.Applies(cover, approach))
      modifiers.Add(new HitChanceModifier(CoverModifierLabel, -cover.Amount));

    return new HitChanceBreakdown(baseChance, modifiers);
  }
}
