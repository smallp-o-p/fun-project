using System;
using System.Collections.Generic;
using System.Linq;

namespace FunProject.Battle;

public sealed record HitChanceModifier(string Label, int Amount);

public sealed record HitChanceBreakdown
{
  public int BaseChance { get; }
  public IReadOnlyList<HitChanceModifier> Modifiers { get; }
  public int FinalChance { get; }

  public HitChanceBreakdown(int baseChance, IReadOnlyList<HitChanceModifier> modifiers)
  {
    ArgumentNullException.ThrowIfNull(modifiers);

    BaseChance = baseChance;
    Modifiers = [.. modifiers];
    FinalChance = Math.Clamp(baseChance + Modifiers.Sum(modifier => modifier.Amount), 0, 100);
  }
}
