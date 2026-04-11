using System;

namespace FunProject.Stats;

public abstract partial class TypedEquippableStatMod<TStat> : EquippableStatMod
  where TStat : Stat
{
  protected override Type TargetStatType => typeof(TStat);
}
