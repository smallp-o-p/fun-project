using System;
using FunProject.Progression;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class UnitRankGainTest
{
  private static RankTableData MakeTable(params (string Name, int Factor)[] levels)
  {
    var table = new RankTableData();
    foreach ((string name, int factor) in levels)
      table.Levels.Add(new RankLevelData { Name = name, GainFactorPercent = factor });
    return table;
  }

  [TestCase(TestName = "A gain at full factor accrues unchanged")]
  public void GainAtFullFactorAccrues()
  {
    var rank = new UnitRank(MakeTable(("Rookie", 100), ("Squaddie", 85)));
    rank.Gain(40);
    Assert.Equal(1, rank.Level);
    Assert.Equal(40, rank.Xp);
    Assert.Equal("Rookie", rank.RankName);
  }

  [TestCase(TestName = "A gain scales by the current rank's factor")]
  public void GainScalesByFactor()
  {
    var rank = new UnitRank(MakeTable(("Rookie", 50), ("Squaddie", 50)));
    rank.Gain(40);
    Assert.Equal(20, rank.Xp);
  }

  [TestCase(TestName = "A scaled gain below one accrues one (slow, never zero)")]
  public void ScaledGainFloorsAtOne()
  {
    var rank = new UnitRank(MakeTable(("Rookie", 10), ("Squaddie", 10)));
    rank.Gain(5); // 0.5 floored would be 0 — must accrue 1
    Assert.Equal(1, rank.Xp);
  }

  [TestCase(TestName = "A level-up carries overflow cut by the new rank's factor")]
  public void LevelUpCarriesOverflowCutByNewFactor()
  {
    var rank = new UnitRank(MakeTable(("Rookie", 100), ("Squaddie", 50), ("Corporal", 50)));
    rank.Gain(120); // 120 - 100 = 20 carried, cut by Squaddie's 50% -> 10
    Assert.Equal(2, rank.Level);
    Assert.Equal(10, rank.Xp);
    Assert.Equal("Squaddie", rank.RankName);
  }

  [TestCase(TestName = "A large gain chains level-ups")]
  public void LargeGainChainsLevelUps()
  {
    var rank = new UnitRank(MakeTable(("Rookie", 100), ("Squaddie", 100), ("Corporal", 100), ("Sergeant", 100)));
    rank.Gain(250); // -> level 3 with 50 carried
    Assert.Equal(3, rank.Level);
    Assert.Equal(50, rank.Xp);
  }

  [TestCase(TestName = "A single-entry table is max at level 1 and gains are ignored")]
  public void SingleEntryTableIgnoresGains()
  {
    var rank = new UnitRank(MakeTable(("Rookie", 100)));
    Assert.True(rank.IsMaxLevel);
    rank.Gain(500);
    Assert.Equal(1, rank.Level);
    Assert.Equal(0, rank.Xp);
  }

  [TestCase(TestName = "Landing on the max rank discards the remainder")]
  public void LandingOnMaxDiscardsRemainder()
  {
    var rank = new UnitRank(MakeTable(("Rookie", 100), ("Colonel", 25)));
    rank.Gain(150); // level 2 (max) — the 50 remainder is discarded, not carried
    Assert.Equal(2, rank.Level);
    Assert.True(rank.IsMaxLevel);
    Assert.Equal(0, rank.Xp);
  }

  [TestCase(TestName = "Non-positive awards and malformed tables throw")]
  public void GuardsThrow()
  {
    var rank = new UnitRank(MakeTable(("Rookie", 100)));
    Assert.Throws<ArgumentOutOfRangeException>(() => rank.Gain(0));
    Assert.Throws<InvalidOperationException>(() => new UnitRank(new RankTableData()));
    Assert.Throws<InvalidOperationException>(() => new UnitRank(MakeTable(("Rookie", 100), ("Broken", 0))));
  }
}
