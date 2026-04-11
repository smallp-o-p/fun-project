using System;

namespace FunProject.Stats;

public abstract partial class TypedStatMod<TStat> : StatMod
  where TStat : Stat
{
  protected override Type TargetStatType => typeof(TStat);
}
