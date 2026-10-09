using FunProject.Stats;
using FunProject.Weapons;
using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class StatResolverTest
{
  [TestCase(TestName = "Resolve ignores mods that target a different stat")]
  public static void ResolveIgnoresOtherTargets()
  {
    StatMod[] sources = [new CriticalChanceStatMod { Modifiers = [StatModifier.Add(99)] }];
    Assert.Equal(10f, TestData.MakeWeapon("Rifle").Resolve<RangeStat>(sources));
  }

  [TestCase(TestName = "TryResolve returns None when the owner lacks the stat")]
  public static void TryResolveNoneWhenMissing() =>
    Assert.True(TestData.MakeWeapon("Rifle").TryResolve<HealthStat>([]).IsNone);

  [TestCase(TestName = "StatSheet ctor rejects a transposed key/value entry")]
  public static void StatSheetRejectsTransposed()
  {
    var bad = new System.Collections.Generic.Dictionary<System.Type, Stat>
    {
      [typeof(AimStat)] = new HealthStat { BaseValue = 1 },
    };
    Assert.Throws<System.ArgumentException>(() => _ = new StatSheet(bad));
  }
}
