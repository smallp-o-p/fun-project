using System;
using FunProject.Progression;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class UnitRankGainTest
{
  [TestCase(TestName = "Rank gain: scaling, per-rank factor, floor at one, overflow recut, chaining, max remainder")]
  public void GainTable()
  {
    (string Name, (string Level, int Factor)[] Levels, int Gain, bool MaxBefore, Action<UnitRank, string> Verify)[] rows =
    [
      ("a gain at full factor accrues unchanged",
        [("Rookie", 100), ("Squaddie", 85)], 40, false, (rank, name) =>
        {
          Assert.Equal(1, rank.Level, name);
          Assert.Equal(40, rank.Xp, name);
          Assert.Equal("Rookie", rank.RankName, name);
        }),
      ("a gain scales by the current rank's factor",
        [("Rookie", 50), ("Squaddie", 50)], 40, false, (rank, name) => Assert.Equal(20, rank.Xp, name)),
      // 0.5 floored would be 0 — must accrue 1.
      ("a scaled gain below one accrues one",
        [("Rookie", 10), ("Squaddie", 10)], 5, false, (rank, name) => Assert.Equal(1, rank.Xp, name)),
      // 120 - 100 = 20 carried, cut by Squaddie's 50% -> 10.
      ("a level-up carries overflow cut by the new rank's factor",
        [("Rookie", 100), ("Squaddie", 50), ("Corporal", 50)], 120, false, (rank, name) =>
        {
          Assert.Equal(2, rank.Level, name);
          Assert.Equal(10, rank.Xp, name);
          Assert.Equal("Squaddie", rank.RankName, name);
        }),
      // -> level 3 with 50 carried.
      ("a large gain chains level-ups",
        [("Rookie", 100), ("Squaddie", 100), ("Corporal", 100), ("Sergeant", 100)], 250, false, (rank, name) =>
        {
          Assert.Equal(3, rank.Level, name);
          Assert.Equal(50, rank.Xp, name);
        }),
      ("a single-entry table is max at level 1 and gains are ignored",
        [("Rookie", 100)], 500, true, (rank, name) =>
        {
          Assert.Equal(1, rank.Level, name);
          Assert.Equal(0, rank.Xp, name);
        }),
      // Level 2 (max) — the 50 remainder is discarded, not carried.
      ("landing on the max rank discards the remainder",
        [("Rookie", 100), ("Colonel", 25)], 150, false, (rank, name) =>
        {
          Assert.Equal(2, rank.Level, name);
          Assert.True(rank.IsMaxLevel, name);
          Assert.Equal(0, rank.Xp, name);
        }),
    ];

    foreach (var row in rows)
    {
      var levels = new RankLevelData[row.Levels.Length];
      for (int i = 0; i < row.Levels.Length; i++)
        levels[i] = new RankLevelData { Name = row.Levels[i].Level, GainFactorPercent = row.Levels[i].Factor };
      var rank = new UnitRank(TestData.MakeRankTable(levels));

      if (row.MaxBefore)
        Assert.True(rank.IsMaxLevel, row.Name);
      rank.Gain(row.Gain);
      row.Verify(rank, row.Name);
    }
  }

  [TestCase(TestName = "Non-positive awards and malformed tables throw")]
  public void GuardsThrow()
  {
    var rank = new UnitRank(TestData.MakeRankTable(new RankLevelData { Name = "Rookie", GainFactorPercent = 100 }));
    Assert.Throws<ArgumentOutOfRangeException>(() => rank.Gain(0));
    Assert.Throws<InvalidOperationException>(() => new UnitRank(new RankTableData()));
    Assert.Throws<InvalidOperationException>(() => new UnitRank(TestData.MakeRankTable(
      new RankLevelData { Name = "Rookie", GainFactorPercent = 100 },
      new RankLevelData { Name = "Broken", GainFactorPercent = 0 })));
  }
}
