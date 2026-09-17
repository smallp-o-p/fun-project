#nullable disable warnings
using FunProject.Battle;
using FunProject.Combatants;
using GdUnit4;
using Godot;
using System;

[TestSuite]
[RequireGodotRuntime]
public partial class MapDeploymentTest
{
  [TestCase(TestName = "AssignSpawns pairs loadouts in sorted XYZ cell order, keeping each loadout reference")]
  public void AssignSpawnsPairsLoadoutsInSortedCellOrder()
  {
    Faction faction = TestData.MakeFaction("Player");
    // Authored in reverse sorted order: pairing must follow the XYZ sort, not input order.
    BattleMapData map = TestData.MakeMapData(new Vector3I(2, 2, 2),
      (new Vector3I(1, 0, 0), TestData.SpawnTile(0)),
      (new Vector3I(0, 1, 0), TestData.SpawnTile(0)),
      (new Vector3I(0, 0, 1), TestData.SpawnTile(0)),
      (new Vector3I(0, 0, 0), TestData.SpawnTile(0)));
    var loadouts = new[]
    {
      new UnitLoadout(TestData.MakeCombatant("A", faction)),
      new UnitLoadout(TestData.MakeCombatant("B", faction)),
      new UnitLoadout(TestData.MakeCombatant("C", faction)),
      new UnitLoadout(TestData.MakeCombatant("D", faction)),
    };

    var placements = MapDeployment.AssignSpawns(map, 0, loadouts).RequireRight();

    Assert.Equal(new Vector3I(0, 0, 0), placements[0].Position);
    Assert.Equal(new Vector3I(0, 0, 1), placements[1].Position);
    Assert.Equal(new Vector3I(0, 1, 0), placements[2].Position);
    Assert.Equal(new Vector3I(1, 0, 0), placements[3].Position);
    for (int i = 0; i < loadouts.Length; i++)
      Assert.True(ReferenceEquals(loadouts[i], placements[i].Loadout));
  }

  [TestCase(TestName = "AssignSpawns returns an empty list for an empty roster")]
  public void AssignSpawnsEmptyRosterReturnsEmptyList()
  {
    Faction faction = TestData.MakeFaction("Player");
    BattleMapData map = TestData.MakeMapData(new Vector3I(4, 1, 4),
      (new Vector3I(0, 0, 0), TestData.SpawnTile(0)));

    var placements = MapDeployment.AssignSpawns(map, 0, []).RequireRight();

    Assert.Equal(0, placements.Count);
  }

  [TestCase(TestName = "AssignSpawns fails when the slot has no tagged cells")]
  public void AssignSpawnsFailsWhenSlotHasNoCells()
  {
    Faction faction = TestData.MakeFaction("Player");
    BattleMapData map = TestData.MakeMapData(new Vector3I(4, 1, 4),
      (new Vector3I(0, 0, 0), TestData.SpawnTile(0)));
    var loadouts = new[] { new UnitLoadout(TestData.MakeCombatant("A", faction)) };

    var failure = MapDeployment.AssignSpawns(map, 1, loadouts).RequireLeft();

    Assert.Equal(BattleSetupFailureReason.SpawnSlotShortfall, failure.Reason);
  }

  [TestCase(TestName = "AssignSpawns fails when there are too few spawn cells")]
  public void AssignSpawnsFailsWhenTooFewSpawnCells()
  {
    Faction faction = TestData.MakeFaction("Player");
    BattleMapData map = TestData.MakeMapData(new Vector3I(4, 1, 4),
      (new Vector3I(0, 0, 0), TestData.SpawnTile(0)));
    var loadouts = new[]
    {
      new UnitLoadout(TestData.MakeCombatant("A", faction)),
      new UnitLoadout(TestData.MakeCombatant("B", faction)),
    };

    var failure = MapDeployment.AssignSpawns(map, 0, loadouts).RequireLeft();

    Assert.Equal(BattleSetupFailureReason.SpawnSlotShortfall, failure.Reason);
  }

  [TestCase(TestName = "Pairing returns out-of-bounds tagged cells; Start(type) turns them into a cell failure")]
  public void OutOfBoundsTaggedCellDelegatesToStartValidation()
  {
    Faction faction = TestData.MakeFaction("Player");
    BattleMapData map = TestData.MakeMapData(new Vector3I(4, 1, 4),
      (new Vector3I(9, 0, 0), TestData.SpawnTile(0))); // dim.X = 4
    var loadouts = new[] { new UnitLoadout(TestData.MakeCombatant("A", faction)) };

    var placements = MapDeployment.AssignSpawns(map, 0, loadouts).RequireRight();

    Assert.Equal(new Vector3I(9, 0, 0), placements[0].Position);

    var type = new BattleTypeData { Name = "Bounds probe" };
    type.MapPool.Add(map);
    var side = new FactionDeploymentData { Faction = new FactionData { Name = "Player" } };
    side.Roster.Add(new FunProject.Battle.RosterEntryData
    {
      Combatant = TestData.MakeCombatantData("A", health: 20, aim: 65),
    });
    side.Objectives.Add(new FakeObjectiveData());
    type.Factions.Add(side);

    Assert.Equal(BattleSetupFailureReason.SpawnCellUnavailable,
      BattleFactory.Start(type, seed: 1).RequireLeft().Reason);
  }

  [TestCase(TestName = "AssignSpawns guards its required inputs")]
  public void AssignSpawnsGuards()
  {
    Faction faction = TestData.MakeFaction("Player");
    BattleMapData map = TestData.MakeMapData(new Vector3I(4, 1, 4),
      (new Vector3I(0, 0, 0), TestData.SpawnTile(0)));
    var loadouts = new[] { new UnitLoadout(TestData.MakeCombatant("A", faction)) };

    Assert.Throws<ArgumentNullException>(() => MapDeployment.AssignSpawns(null!, 0, loadouts));
    Assert.Throws<ArgumentNullException>(() => MapDeployment.AssignSpawns(map, 0, null!));
    Assert.Throws<ArgumentOutOfRangeException>(() => MapDeployment.AssignSpawns(map, -1, loadouts));
  }
}
