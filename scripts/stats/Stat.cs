using System;
using Godot;

namespace FunProject.Stats;

public abstract partial class Stat : Resource
{
  [Export] public int BaseValue { get; set; } = 0;
}

public interface HasStats
{
  Option<Stat> TryGetStat(Type statType);
  Option<TStat> TryGetStat<TStat>() where TStat : Stat;
  TStat GetStat<TStat>() where TStat : Stat;
}
