using FunProject.Buffs;
using FunProject.Progression;
using FunProject.Stats;

namespace FunProject.Tests;

internal static class ProgressionTestFactory
{
  // Hermetic default for test-built combatants: DefaultRankTable loads res://resources/ranks.tres,
  // which the test project's separate res:// root does not have — so test data carries its own
  // ladder. Three full-factor rungs so gains accrue without immediately hitting max.
  private static RankTableData? _ladder;

  public static RankTableData Ladder => _ladder ??= BuildLadder();

  private static RankTableData BuildLadder()
  {
    var table = new RankTableData();
    table.Levels.Add(new RankLevelData { Name = "Rookie", GainFactorPercent = 100 });
    table.Levels.Add(new RankLevelData { Name = "Squaddie", GainFactorPercent = 100 });
    table.Levels.Add(new RankLevelData { Name = "Corporal", GainFactorPercent = 100 });
    return table;
  }

  public static SkillPathData MakePath(string name, params SkillUpgradeStepData[] steps)
  {
    var path = new SkillPathData { Name = name, Description = $"{name} path" };
    foreach (SkillUpgradeStepData step in steps)
      path.Steps.Add(step);
    return path;
  }

  public static SkillUpgradeStepData MakeStep(int cost, params UpgradeEffectData[] effects)
  {
    var step = new SkillUpgradeStepData { Cost = cost };
    foreach (UpgradeEffectData effect in effects)
      step.Effects.Add(effect);
    return step;
  }

  public static StatModUpgradeEffectData MakeStatModEffect(params StatMod[] mods)
  {
    var effect = new StatModUpgradeEffectData();
    foreach (StatMod mod in mods)
      effect.StatMods.Add(mod);
    return effect;
  }

  public static BuffGrantUpgradeEffectData MakeBuffGrantEffect(params BuffData[] buffs)
  {
    var effect = new BuffGrantUpgradeEffectData();
    foreach (BuffData buff in buffs)
      effect.Buffs.Add(buff);
    return effect;
  }

  public static AbilityGrantUpgradeEffectData MakeAbilityGrantEffect(string abilityName)
    => new() { Ability = new AbilityData { Name = abilityName } };
}
