using System;
using System.Collections.Generic;
using FunProject.Buffs;
using FunProject.Stats;

namespace FunProject.Progression;

/// <summary>
/// Per-unit rank state: a 1-based level into an authored rank ladder and a 0–99 XP bucket.
/// XP is awarded after battles (see <see cref="AwardBattleExperience"/>) — gains shrink by
/// the current rank's factor, overflow carries into the new rank cut by that rank's factor,
/// and the last entry freezes progression. Effects of all held rungs fold into the
/// combatant's live surfaces, exactly like trained skill-path effects.
/// </summary>
public sealed class UnitRank
{
  public const int XpPerLevel = 100;

  private readonly RankTableData _table;

  public UnitRank(RankTableData table)
  {
    ArgumentNullException.ThrowIfNull(table);
    if (table.Levels.Count == 0)
      throw new InvalidOperationException("A rank table must define at least one level.");
    for (int i = 0; i < table.Levels.Count; i++)
      if (table.Levels[i].GainFactorPercent < 1)
        throw new InvalidOperationException(
          $"Rank entry {i + 1} ('{table.Levels[i].Name}') has GainFactorPercent {table.Levels[i].GainFactorPercent}; factors must be at least 1.");

    _table = table;
  }

  public int Level { get; private set; } = 1;
  public int Xp { get; private set; }

  public string RankName => _table.Levels[Level - 1].Name;
  public bool IsMaxLevel => Level == _table.Levels.Count;

  /// <summary>Award raw XP: scaled by the current rank's factor (floored to 1, so high
  /// ranks are slow but never zero), overflow carries into the new rank cut by its factor
  /// (floored to 0 — the min-1 rule guards awards, not remainders). A large award can chain
  /// level-ups. No-op at max level; the bucket stays at 0.</summary>
  public void Gain(int rawAward)
  {
    if (rawAward < 1)
      throw new ArgumentOutOfRangeException(nameof(rawAward), rawAward, "XP awards must be positive.");
    if (IsMaxLevel)
      return;

    Xp += Math.Max(1, rawAward * _table.Levels[Level - 1].GainFactorPercent / 100);
    while (Xp >= XpPerLevel && !IsMaxLevel)
    {
      Xp -= XpPerLevel;
      Level++;
      Xp = Xp * _table.Levels[Level - 1].GainFactorPercent / 100;
    }

    if (IsMaxLevel)
      Xp = 0;
  }

  /// <summary>Permanent stat mods from held rungs, folding into stat contributions.</summary>
  public IReadOnlyList<StatMod> StatMods()
  {
    List<StatMod> result = [];
    foreach (UpgradeEffectData effect in HeldEffects())
      if (effect is StatModUpgradeEffectData statModEffect)
        foreach (StatMod mod in statModEffect.StatMods)
          result.Add(mod);
    return result;
  }

  /// <summary>Buffs granted by held rungs, joining spawn-time buff grants.</summary>
  public IReadOnlyList<Buff> GrantedBuffs()
  {
    List<Buff> result = [];
    foreach (UpgradeEffectData effect in HeldEffects())
      if (effect is BuffGrantUpgradeEffectData buffEffect)
        foreach (Buff buff in buffEffect.Buffs)
          result.Add(buff);
    return result;
  }

  /// <summary>Abilities granted by held rungs; inert until the ability system consumes them.</summary>
  public IReadOnlyList<AbilityData> GrantedAbilities()
  {
    List<AbilityData> result = [];
    foreach ((int index, UpgradeEffectData effect) in HeldEffectsIndexed())
      if (effect is AbilityGrantUpgradeEffectData abilityEffect)
      {
        if (abilityEffect.Ability is null)
          throw new InvalidOperationException(
            $"An AbilityGrantUpgradeEffect on rank '{_table.Levels[index].Name}' has no Ability assigned.");
        result.Add(abilityEffect.Ability);
      }
    return result;
  }

  // Effects of rungs 1..Level in ladder order. Carries the rung index so guards can name
  // the exact authoring mistake.
  private IEnumerable<(int Index, UpgradeEffectData Effect)> HeldEffectsIndexed()
  {
    for (int i = 0; i < Level; i++)
      foreach (UpgradeEffectData effect in _table.Levels[i].Effects)
        yield return (i, effect);
  }

  private IEnumerable<UpgradeEffectData> HeldEffects()
  {
    foreach ((_, UpgradeEffectData effect) in HeldEffectsIndexed())
      yield return effect;
  }
}
