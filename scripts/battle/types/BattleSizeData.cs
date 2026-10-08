using FunProject.Core;
using Godot;
using System;

namespace FunProject.Battle;

/// <summary>Player capacity and the inclusive ordinary-enemy range. Special enemies are additive.</summary>
[Tool, GlobalClass]
public partial class BattleSizeData : NamedEntityData
{
  private uint _maxPlayerUnits = 1;
  private uint _minEnemyUnits;
  private uint _maxEnemyUnits;

  [Export(PropertyHint.Range, "1,4294967295,1")]
  public uint MaxPlayerUnits
  {
    get => _maxPlayerUnits;
    set => _maxPlayerUnits = Math.Max(1u, value);
  }

  // Preserve the authored minimum until both serialized properties have been assigned.
  [Export(PropertyHint.Range, "0,4294967295,1")]
  public uint MinEnemyUnits
  {
    get => Math.Min(_minEnemyUnits, MaxEnemyUnits);
    set => _minEnemyUnits = value;
  }

  [Export(PropertyHint.Range, "0,4294967295,1")]
  public uint MaxEnemyUnits
  {
    get => _maxEnemyUnits;
    set
    {
      _maxEnemyUnits = value;
      NotifyPropertyListChanged();
    }
  }

  public override void _ValidateProperty(Godot.Collections.Dictionary property)
  {
    if (property["name"].AsString() == PropertyName.MinEnemyUnits)
      property["hint_string"] = $"0,{MaxEnemyUnits},1";
  }

  public uint SampleEnemyCount(Random random)
    => (uint)random.NextInt64(MinEnemyUnits, (long)MaxEnemyUnits + 1);
}
