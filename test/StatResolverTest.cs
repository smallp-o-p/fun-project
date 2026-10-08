using FunProject.Stats;
using FunProject.Weapons;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class StatResolverTest
{
  [TestCase(TestName = "Resolve ignores mods that target a different stat")]
  public void ResolveIgnoresOtherTargets()
  {
    StatMod[] sources = [new CriticalChanceStatMod { Modifiers = [StatModifier.Add(99)] }];
    Assert.Equal(10f, TestData.MakeWeapon("Rifle").Resolve<RangeStat>(sources));
  }

  [TestCase(TestName = "TryResolve returns None when the owner lacks the stat")]
  public void TryResolveNoneWhenMissing() =>
    Assert.True(TestData.MakeWeapon("Rifle").TryResolve<HealthStat>([]).IsNone);

  [TestCase(TestName = "StatSheet ctor rejects a transposed key/value entry")]
  public void StatSheetRejectsTransposed()
  {
    var bad = new System.Collections.Generic.Dictionary<System.Type, Stat>
    {
      [typeof(AimStat)] = new HealthStat { BaseValue = 1 },
    };
    Assert.Throws<System.ArgumentException>(() => new StatSheet(bad));
  }
}
