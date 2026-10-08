using FunProject.Battle;
using System;
using System.Collections.Generic;
using FunProject.Buffs;
using FunProject.Stats;

namespace FunProject.Progression;

/// <summary>
/// Persistent per-unit progression: a currency balance, up to two committed skill paths,
/// and how many steps of each chain are unlocked. No refunds, no respec, no un-commit.
/// Currency is earned per unit (missions, kills, objectives) via AwardPoints.
/// </summary>
public sealed class UnitProgression
{
  private readonly BattleAccessBinding _battleAccess = new(shared: true);
  internal void BindBattleAccess(BattleAccessGuard access) { _battleAccess.Bind(access); }
  internal void UnbindBattleAccess(BattleAccessGuard access) => _battleAccess.Unbind(access);

  public const int MaxCommittedPaths = 2;

  private readonly List<SkillPathData> _committedPaths = [];
  private readonly Dictionary<SkillPathData, int> _unlockedStepsByPath = [];

  private int _currencyPoints;
  public int CurrencyPoints
  {
    get { _battleAccess.RequireRead(); return _currencyPoints; }
    private set { _battleAccess.RequireMutation(); _currencyPoints = value; }
  }
  public IReadOnlyList<SkillPathData> CommittedPaths { get { _battleAccess.RequireRead(); return _committedPaths.AsReadOnly(); } }

  public void AwardPoints(int amount)
  {
    _battleAccess.RequireMutation();
    if (amount < 1)
      throw new ArgumentOutOfRangeException(nameof(amount), amount, "Currency awards must be positive.");
    CurrencyPoints += amount;
  }

  /// <summary>Free commitment of an available path; false when both slots are full or it is already held.</summary>
  public bool TryCommit(SkillPathData path)
  {
    _battleAccess.RequireMutation();
    ArgumentNullException.ThrowIfNull(path);
    if (path.Steps.Count == 0)
      throw new InvalidOperationException(
        $"SkillPath '{path.Name}' has no steps; an authored path must define at least one.");
    if (_committedPaths.Count >= MaxCommittedPaths || _unlockedStepsByPath.ContainsKey(path))
      return false;

    _unlockedStepsByPath[path] = 0;
    _committedPaths.Add(path);
    return true;
  }

  /// <summary>How many steps of the chain are unlocked (0 = committed, nothing bought).</summary>
  public int StepsUnlockedFor(SkillPathData path)
  {
    _battleAccess.RequireRead();
    ArgumentNullException.ThrowIfNull(path);
    return _unlockedStepsByPath.TryGetValue(path, out int unlocked) ? unlocked : 0;
  }

  /// <summary>The next purchasable step of a committed path, or None when not committed or the chain is complete.</summary>
  public Option<SkillUpgradeStepData> NextStep(SkillPathData path)
  {
    _battleAccess.RequireRead();
    ArgumentNullException.ThrowIfNull(path);
    if (!_unlockedStepsByPath.TryGetValue(path, out int unlocked))
      return Option<SkillUpgradeStepData>.None;

    return path.Steps.Count > unlocked ? path.Steps[unlocked] : Option<SkillUpgradeStepData>.None;
  }

  /// <summary>Purchases the next step of a committed path; None when the path is not
  /// committed, the chain is complete, or the step is unaffordable.</summary>
  public Option<UnlockedStep> TryUnlockNext(SkillPathData path)
  {
    _battleAccess.RequireMutation();
    ArgumentNullException.ThrowIfNull(path);
    if (!_unlockedStepsByPath.ContainsKey(path))
      return Option<UnlockedStep>.None;

    return NextStep(path).Match(
      Some: next =>
      {
        if (next.Cost < 1)
          throw new InvalidOperationException(
            $"Step {StepsUnlockedFor(path) + 1} of '{path.Name}' has Cost {next.Cost}; costs must be at least 1.");
        if (CurrencyPoints < next.Cost)
          return Option<UnlockedStep>.None;

        CurrencyPoints -= next.Cost;
        _unlockedStepsByPath[path] += 1;
        _battleAccess.Guard?.NotifyStatSourcesChanged();
        return Some(new UnlockedStep(path, next));
      },
      None: () => Option<UnlockedStep>.None);
  }

  /// <summary>Permanent stat mods from unlocked steps, folding into stat contributions.</summary>
  public IReadOnlyList<StatMod> StatMods()
  {
    _battleAccess.RequireRead();
    List<StatMod> result = [];
    foreach (var (_, _, step) in UnlockedSteps())
      foreach (UpgradeEffectData effect in step.Effects)
        if (effect is StatModUpgradeEffectData statModEffect)
          foreach (StatMod mod in statModEffect.StatMods)
            result.Add(mod);
    return result;
  }

  /// <summary>Buffs granted by unlocked steps, joining spawn-time buff grants.</summary>
  public IReadOnlyList<Buff> GrantedBuffs()
  {
    _battleAccess.RequireRead();
    List<Buff> result = [];
    foreach (var (_, _, step) in UnlockedSteps())
      foreach (UpgradeEffectData effect in step.Effects)
        if (effect is BuffGrantUpgradeEffectData buffEffect)
          foreach (Buff buff in buffEffect.Buffs)
            result.Add(buff);
    return result;
  }

  /// <summary>Abilities granted by unlocked steps; inert until the ability system consumes them.</summary>
  public IReadOnlyList<AbilityData> GrantedAbilities()
  {
    _battleAccess.RequireRead();
    List<AbilityData> result = [];
    foreach (var (path, stepIndex, step) in UnlockedSteps())
      foreach (UpgradeEffectData effect in step.Effects)
        if (effect is AbilityGrantUpgradeEffectData abilityEffect)
        {
          if (abilityEffect.Ability is null)
            throw new InvalidOperationException(
              $"An AbilityGrantUpgradeEffect on step {stepIndex + 1} of path '{path.Name}' has no Ability assigned.");
          result.Add(abilityEffect.Ability);
        }
    return result;
  }

  // Unlocked steps in commit order, then chain order. The single interpretation loop
  // shared by all three aggregation accessors; carries path and step index so guards
  // can name the exact authoring mistake.
  private IEnumerable<(SkillPathData Path, int StepIndex, SkillUpgradeStepData Step)> UnlockedSteps()
  {
    foreach (SkillPathData path in _committedPaths)
    {
      int unlocked = StepsUnlockedFor(path);
      for (int i = 0; i < unlocked; i++)
        yield return (path, i, path.Steps[i]);
    }
  }
}
