using System;

namespace FunProject.Stats;

public abstract partial class TypedStatMod<TStat> : StatMod
  where TStat : Stat
{
  public override Type TargetType => typeof(TStat);
}
