using FunProject.Core;
using Godot;
using System;

namespace FunProject.Battle;

/// <summary>Authored capacity envelope for a tactical mission: how many player units may
/// deploy, and the inclusive ordinary-enemy draw range. Special enemies are additive and
/// never raise the player limit.</summary>
[GlobalClass]
public partial class BattleSizeData : NamedEntityData
{
  [Export] public int MaxPlayerUnits { get; set; } = 1;

  [Export] public int MinEnemyUnits { get; set; } = 0;

  [Export] public int MaxEnemyUnits { get; set; } = 0;

  /// <summary>Authoring guard: positive player capacity and 0 &lt;= Min &lt;= Max.</summary>
  public void Validate()
  {
    if (MaxPlayerUnits <= 0)
      throw new InvalidOperationException(
        $"{nameof(BattleSizeData)} requires a positive {nameof(MaxPlayerUnits)}.");
    if (MinEnemyUnits < 0 || MinEnemyUnits > MaxEnemyUnits)
      throw new InvalidOperationException(
        $"{nameof(BattleSizeData)} requires 0 <= {nameof(MinEnemyUnits)} <= {nameof(MaxEnemyUnits)}; found [{MinEnemyUnits}, {MaxEnemyUnits}].");
  }
}
