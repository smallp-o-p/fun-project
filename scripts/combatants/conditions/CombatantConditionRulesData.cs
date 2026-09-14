using FunProject.Stats;
using Godot;
using Godot.Collections;

namespace FunProject.Combatants.Conditions;

/// <summary>
/// Injury and Fatigue ladder
/// </summary>
[GlobalClass]
public partial class CombatantConditionRulesData : Resource
{
  /// <summary>Shared default ladder for campaigns that author no rules of their own.</summary>
  public static CombatantConditionRulesData Shared { get; } = new();

  [Export] public Array<InjuryTierData> InjuryTiers { get; set; } = DefaultInjuryTiers();

  [Export] public Array<ConditionTierData> FatigueTiers { get; set; } = DefaultFatigueTiers();

  public void ValidateAndSort()
  {
    for (int i = 0; i < InjuryTiers.Count; i++)
    {
      InjuryTierData tier = InjuryTiers[i];
      ValidateTier(tier, "injury", i);
      double threshold = tier.MinimumDamagePercent;
      if (!double.IsFinite(threshold) || threshold < 0.0 || threshold > 100.0)
        throw new System.InvalidOperationException(
          $"Injury tier {i + 1} ('{tier.Name}') has MinimumDamagePercent {threshold}; it must be finite within [0, 100].");
    }
    for (int i = 0; i < FatigueTiers.Count; i++)
      ValidateTier(FatigueTiers[i], "fatigue", i);

    // ZLinq's OrderBy breaks equal-key ties by original index, so sorted ladders retain
    // authored order among equal keys; references are written back by index, keeping the
    // arrays and their resource identities.
    InjuryTierData[] sortedInjury = InjuryTiers.AsValueEnumerable()
      .OrderBy(t => t.MinimumDamagePercent).ToArray();
    for (int i = 0; i < sortedInjury.Length; i++)
      InjuryTiers[i] = sortedInjury[i];

    ConditionTierData[] sortedFatigue = FatigueTiers.AsValueEnumerable()
      .OrderBy(t => t.RecoveryDays).ToArray();
    for (int i = 0; i < sortedFatigue.Length; i++)
      FatigueTiers[i] = sortedFatigue[i];
  }

  private static void ValidateTier(ConditionTierData tier, string kind, int index)
  {
    System.ArgumentNullException.ThrowIfNull(tier);
    if (string.IsNullOrWhiteSpace(tier.Name))
      throw new System.InvalidOperationException($"{kind} tier {index + 1} must have a nonempty Name.");
    if (tier.RecoveryDays == 0)
      throw new System.InvalidOperationException(
        $"{kind} tier {index + 1} ('{tier.Name}') must have positive RecoveryDays.");
    if (tier.StatMods is null)
      throw new System.InvalidOperationException(
        $"{kind} tier {index + 1} ('{tier.Name}') must have a StatMods array.");
    for (int i = 0; i < tier.StatMods.Count; i++)
      if (tier.StatMods[i] is null)
        throw new System.InvalidOperationException(
          $"{kind} tier {index + 1} ('{tier.Name}') has a null StatMod at index {i}.");
  }

  private static Array<InjuryTierData> DefaultInjuryTiers() =>
  [
    new() { Name = "Lightly Injured", RecoveryDays = 4, MinimumDamagePercent = 0f,
      StatMods = InjuryMods(health: 0.90f, aim: 0.95f, movement: 0.95f) },
    new() { Name = "Injured", RecoveryDays = 6, MinimumDamagePercent = 25f,
      StatMods = InjuryMods(health: 0.75f, aim: 0.90f, movement: 0.90f) },
    new() { Name = "Gravely Injured", RecoveryDays = 16, MinimumDamagePercent = 50f,
      StatMods = InjuryMods(health: 0.60f, aim: 0.80f, movement: 0.80f) },
    new() { Name = "Critically Injured", RecoveryDays = 30, MinimumDamagePercent = 75f,
      StatMods = InjuryMods(health: 0.40f, aim: 0.70f, movement: 0.70f) },
  ];

  private static Array<ConditionTierData> DefaultFatigueTiers() =>
  [
    new() { Name = "Tired", RecoveryDays = 1, StatMods = FatigueMods(aim: 0.90f, will: 0.90f) },
    new() { Name = "Weary", RecoveryDays = 4, StatMods = FatigueMods(aim: 0.80f, will: 0.80f) },
    new() { Name = "Exhausted", RecoveryDays = 8, StatMods = FatigueMods(aim: 0.65f, will: 0.65f) },
  ];

  private static Array<StatMod> InjuryMods(float health, float aim, float movement)
  {
    // The one-hit-point CapMin floor is contributed by the condition system itself whenever
    // an injury is active, so it applies to authored ladders too, not just these defaults.
    var healthMod = new HealthStatMod();
    healthMod.AddModifier(StatModifier.Multiply(health));
    var aimMod = new AimStatMod();
    aimMod.AddModifier(StatModifier.Multiply(aim));
    var movementMod = new MovementStatMod();
    movementMod.AddModifier(StatModifier.Multiply(movement));
    return [healthMod, aimMod, movementMod];
  }

  private static Array<StatMod> FatigueMods(float aim, float will)
  {
    return [
      new AimStatMod { Modifiers = [StatModifier.Multiply(aim)] },
      new WillStatMod { Modifiers = [StatModifier.Multiply(will)] }
    ];
  }
}
