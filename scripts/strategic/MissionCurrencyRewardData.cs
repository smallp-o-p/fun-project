using System;
using System.Collections.Generic;
using FunProject.Currencies;
using Godot;

namespace FunProject.Strategic;

[GlobalClass]
public partial class MissionCurrencyRewardData : Resource
{
  [Export] public required Currency Currency { get; set; }
  [Export] public ulong Minimum { get; set; }
  [Export] public ulong Maximum { get; set; }
  internal static IReadOnlyDictionary<Currency, ulong> Generate(IReadOnlyList<MissionCurrencyRewardData> entries, int seed)
  {
    MissionCurrencyRewardData[] pool = [.. entries];
    var rewards = new Dictionary<Currency, ulong>(ReferenceEqualityComparer.Instance);
    if (pool.Length > 0)
    {
      var random = new Random(unchecked(seed ^ (int)0xBB67AE85));
      int count = random.Next(1, pool.Length + 1);
      for (int i = 0; i < count; i++)
      {
        int chosen = random.Next(i, pool.Length);
        (pool[i], pool[chosen]) = (pool[chosen], pool[i]);
        var reward = pool[i];
        ulong amount = rewards.GetValueOrDefault(reward.Currency);
        rewards[reward.Currency] = amount + Math.Min(reward.Sample(random), ulong.MaxValue - amount);
      }
    }
    return new System.Collections.ObjectModel.ReadOnlyDictionary<Currency, ulong>(rewards);
  }

  private ulong Sample(Random random)
  {
    ulong range = Maximum - Minimum;
    if (range == 0) return Minimum;
    Span<byte> bytes = stackalloc byte[sizeof(ulong)];
    ulong width = unchecked(range + 1);
    ulong threshold = width == 0 ? 0 : unchecked(0UL - width) % width;
    ulong value;
    do
    {
      random.NextBytes(bytes);
      value = BitConverter.ToUInt64(bytes);
    } while (value < threshold);
    return width == 0 ? value : Minimum + value % width;
  }

}
