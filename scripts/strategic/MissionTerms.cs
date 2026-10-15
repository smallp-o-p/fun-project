using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using FunProject.Battle;
using FunProject.Currencies;

namespace FunProject.Strategic;

public sealed record MissionTally(int TurnsTaken, int SoldiersWounded, int SoldiersKilled, int EnemiesKilled)
{
  public static MissionTally From(FactionBattleSummary summary, BattleResult result)
    => new(summary.TurnCount, summary.CombatantsWounded.Count, summary.CombatantsDead.Count,
      result.Factions.AsValueEnumerable().Where(pair => pair.Key != summary.Faction).Sum(pair => pair.Value.Killed));
}

public sealed record MissionReceipt(string MissionType, Option<BattleOutcome> Outcome,
  IReadOnlyDictionary<Currency, ulong> Rewards)
{
  public Option<MissionTally> Tally { get; init; }
}

/// <summary>One retained reward roll per event, independent of combat RNG.</summary>
public sealed record MissionTerms(IReadOnlyDictionary<Currency, ulong> VictoryRewards)
{
  internal static MissionTerms Resolve(TacticalMissionData mission, int seed)
  {
    var rewards = new Dictionary<Currency, ulong>(ReferenceEqualityComparer.Instance);
    var rewardRandom = new Random(unchecked(seed ^ (int)0xBB67AE85));
    foreach (var reward in mission.VictoryRewards)
    {
      if (reward.Currency is null || reward.Minimum > reward.Maximum)
        throw new InvalidOperationException("Mission rewards require a currency and an ordered range.");
      AddAmount(rewards, reward.Currency, Sample(rewardRandom, reward.Minimum, reward.Maximum));
    }

    return new MissionTerms(new ReadOnlyDictionary<Currency, ulong>(rewards));
  }

  internal static void AddAmount(Dictionary<Currency, ulong> amounts, Currency currency, ulong amount)
    => amounts[currency] = checked(amounts.GetValueOrDefault(currency) + amount);

  // Inclusive unsigned sampling, including ulong.MaxValue, without modulo bias.
  private static ulong Sample(Random random, ulong minimum, ulong maximum)
  {
    ulong range = maximum - minimum;
    if (range == 0) return minimum;
    Span<byte> bytes = stackalloc byte[sizeof(ulong)];
    ulong width = unchecked(range + 1);
    ulong threshold = width == 0 ? 0 : unchecked(0UL - width) % width;
    ulong value;
    do
    {
      random.NextBytes(bytes);
      value = BitConverter.ToUInt64(bytes);
    } while (value < threshold);
    return width == 0 ? value : minimum + value % width;
  }
}
