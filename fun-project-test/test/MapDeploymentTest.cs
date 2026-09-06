using FunProject.Battle;
using FunProject.Combatants;
using GdUnit4;
using Godot;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public partial class MapDeploymentTest
{
  [TestCase(TestName = "AssignSpawns pairs each combatant with a spawn cell")]
  public void AssignSpawnsPairsEachCombatantWithASpawnCell()
  {
    Faction faction = TestData.MakeFaction("Player");
    Combatant a = TestData.MakeCombatant("A", faction);
    Combatant b = TestData.MakeCombatant("B", faction);
    BattleMapData map = TestData.MakeMapData(new Vector3I(4, 1, 4),
      (new Vector3I(0, 0, 0), TestData.SpawnTile(0)),
      (new Vector3I(1, 0, 0), TestData.SpawnTile(0)));
    var rosters = new Dictionary<int, IReadOnlyList<Combatant>> { [0] = new[] { a, b } };

    var result = MapDeployment.AssignSpawns(map, rosters);

    IReadOnlyList<(Combatant Combatant, Vector3I Position)> placements =
      result.Match(Right: r => r, Left: _ => null);
    Assert.True(placements is not null);
    Assert.Equal(2, placements.Count);
    Assert.Equal(new Vector3I(0, 0, 0), placements[0].Position);
    Assert.Equal(a, placements[0].Combatant);
    Assert.Equal(new Vector3I(1, 0, 0), placements[1].Position);
    Assert.Equal(b, placements[1].Combatant);
  }

  [TestCase(TestName = "AssignSpawns fails when a roster slot has no spawn cells")]
  public void AssignSpawnsFailsWhenRosterSlotHasNoSpawnCells()
  {
    Faction faction = TestData.MakeFaction("Player");
    Combatant a = TestData.MakeCombatant("A", faction);
    BattleMapData map = TestData.MakeMapData(new Vector3I(4, 1, 4),
      (new Vector3I(0, 0, 0), TestData.SpawnTile(0)));
    var rosters = new Dictionary<int, IReadOnlyList<Combatant>> { [1] = new[] { a } };

    var result = MapDeployment.AssignSpawns(map, rosters);

    Assert.True(result.IsLeft);
  }

  [TestCase(TestName = "AssignSpawns fails when there are too few spawn cells")]
  public void AssignSpawnsFailsWhenTooFewSpawnCells()
  {
    Faction faction = TestData.MakeFaction("Player");
    Combatant a = TestData.MakeCombatant("A", faction);
    Combatant b = TestData.MakeCombatant("B", faction);
    BattleMapData map = TestData.MakeMapData(new Vector3I(4, 1, 4),
      (new Vector3I(0, 0, 0), TestData.SpawnTile(0)));
    var rosters = new Dictionary<int, IReadOnlyList<Combatant>> { [0] = new[] { a, b } };

    var result = MapDeployment.AssignSpawns(map, rosters);

    Assert.True(result.IsLeft);
  }

  [TestCase(TestName = "AssignSpawns fails when a spawn cell is out of bounds")]
  public void AssignSpawnsFailsWhenSpawnCellOutOfBounds()
  {
    Faction faction = TestData.MakeFaction("Player");
    Combatant a = TestData.MakeCombatant("A", faction);
    BattleMapData map = TestData.MakeMapData(new Vector3I(4, 1, 4),
      (new Vector3I(9, 0, 0), TestData.SpawnTile(0))); // dim.X = 4
    var rosters = new Dictionary<int, IReadOnlyList<Combatant>> { [0] = new[] { a } };

    var result = MapDeployment.AssignSpawns(map, rosters);

    Assert.True(result.IsLeft);
  }
}
