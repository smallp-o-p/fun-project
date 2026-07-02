using FunProject.Stats;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class StatResolverTest
{
  [TestCase(TestName = "Resolve with no sources returns the base value")]
  public void ResolveNoSources() =>
    Assert.Equal(10f, ((HasStats)MakeWeapon("Rifle")).Resolve<RangeStat>([]));

  [TestCase(TestName = "Resolve folds matching-target mods over the base")]
  public void ResolveFoldsMatching()
  {
    StatMod[] sources = [new RangeStatMod { Modifiers = [StatModifier.Add(5)] }];
    Assert.Equal(15f, ((HasStats)MakeWeapon("Rifle")).Resolve<RangeStat>(sources));
  }

  [TestCase(TestName = "Resolve combines same-stat mods from multiple sources")]
  public void ResolveStacksAcrossSources()
  {
    StatMod[] sources =
    [
      new RangeStatMod { Modifiers = [StatModifier.Add(5)] },
      new RangeStatMod { Modifiers = [StatModifier.PercentAdd(0.5f)] },
    ];
    // (10 + 5) * 1.5 = 22.5
    Assert.Equal(22.5f, ((HasStats)MakeWeapon("Rifle")).Resolve<RangeStat>(sources));
  }

  [TestCase(TestName = "Resolve ignores mods that target a different stat")]
  public void ResolveIgnoresOtherTargets()
  {
    StatMod[] sources = [new CriticalChanceStatMod { Modifiers = [StatModifier.Add(99)] }];
    Assert.Equal(10f, ((HasStats)MakeWeapon("Rifle")).Resolve<RangeStat>(sources));
  }

  [TestCase(TestName = "TryResolve returns None when the owner lacks the stat")]
  public void TryResolveNoneWhenMissing() =>
    Assert.True(((HasStats)MakeWeapon("Rifle")).TryResolve<HealthStat>([]).IsNone);

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
