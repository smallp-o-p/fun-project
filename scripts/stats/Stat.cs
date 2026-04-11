using System;
using Godot;

namespace FunProject.Stats;

public abstract partial class Stat : Resource
{
  [Export] public int BaseValue { get; set; } = 0;
}

public interface HasStats
{
  bool TryGetStat(Type statType, out Stat stat);
  bool TryGetStat<TStat>(out TStat stat) where TStat : Stat;
  TStat GetStat<TStat>() where TStat : Stat;
}
