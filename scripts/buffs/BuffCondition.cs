using FunProject.Battle;
using Godot;

namespace FunProject.Buffs;

/// <summary>A shared, read-only predicate evaluated against a battle unit.</summary>
[GlobalClass]
public abstract partial class BuffCondition : Resource
{
  internal abstract bool IsMet(BattleSession session, BattleUnitState unit);
}
