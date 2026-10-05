using FunProject.Battle;
using FunProject.Combatants;
using GdUnit4;
using Godot;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public class BattleSessionQueriesTest
{
  private static BattleSetup SoloSetup(Faction faction, string unitName, Vector3I position,
    int actionPoints = 4, BattleMapData map = null)
    => new(
      map ?? TestData.MakeOpenBattleMap(new Vector3I(4, 1, 1)),
      [new BattleSideSetup(faction, [new FakeObjectiveData()],
        [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant(unitName, faction, actionPoints: actionPoints)), position)])],
      Seed: 7);

  private static BattleSetup DuelSetup(Faction playerFaction, string playerName, Vector3I playerPosition,
    Faction enemyFaction, string enemyName, Vector3I enemyPosition,
    int playerHealth = 20, int enemyHealth = 20, int playerVision = 20, int enemyVision = 20,
    BattleMapData map = null)
    => new(
      map ?? TestData.MakeOpenBattleMap(new Vector3I(5, 1, 1)),
      [
        new BattleSideSetup(playerFaction, [new FakeObjectiveData()],
          [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant(playerName, playerFaction,
            health: playerHealth, vision: playerVision)), playerPosition)]),
        new BattleSideSetup(enemyFaction, [new FakeObjectiveData()],
          [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant(enemyName, enemyFaction,
            health: enemyHealth, vision: enemyVision)), enemyPosition)]),
      ],
      Seed: 7);

  private static BattleMapData RowMapWithWall(int width, int wallX)
  {
    List<(Vector3I Cell, BattleMapTileData Tile)> tiles = [];
    for (int x = 0; x < width; x++)
      tiles.Add((new Vector3I(x, 0, 0), x == wallX ? TestData.WallTile() : TestData.FloorTile()));
    return TestData.MakeMapData(new Vector3I(width, 1, 1), [.. tiles]);
  }

  [TestCase(TestName = "FindPathForUnit returns a successful path result")]
  public void FindPathForUnitReturnsASuccessfulPathResult()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(SoloSetup(faction, "Runner", new Vector3I(0, 0, 0)));
    var unit = battle.Unit;

    BattleBoardState.ValidatedPoint[] path = battle.Query(new FindPathForUnit(battle.Alive(unit), battle.At(2, 0, 0)));

    Assert.Equal(new Vector3I(0, 0, 0), battle.Alive(unit).Position.Raw);
    Assert.Equal(3, path.Length);
    Assert.Equal(new Vector3I(0, 0, 0), path[0].Raw);
    Assert.Equal(new Vector3I(2, 0, 0), path[^1].Raw);
  }

  [TestCase(1, TestName = "GetUnitAtTile returns the unit occupying a tile")]
  [TestCase(2, TestName = "GetUnitAtTile returns None for an empty tile")]
  public void GetUnitAtTileReturnsOccupantOrNone(int x)
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(SoloSetup(faction, "Runner", new Vector3I(1, 0, 0)));
    var unit = battle.Unit;

    Option<BattleUnitState> occupant = battle.Query(new GetUnitAtTile(battle.At(x, 0, 0)));

    Assert.Equal(x == 1, occupant.IsSome);
    if (x == 1)
      Assert.Equal(unit, occupant.RequireSome());
  }

  [TestCase(TestName = "GetPossibleMoveTilesForUnit respects action points")]
  public void GetPossibleMoveTilesForUnitRespectsActionPoints()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(SoloSetup(faction, "Runner", new Vector3I(0, 0, 0), actionPoints: 2));
    var unit = battle.Unit;

    IReadOnlyCollection<BattleBoardState.ValidatedPoint> tiles = battle.Query(new GetPossibleMoveTilesForUnit(battle.Alive(unit)));
    Assert.True(tiles.Count == 2);
    var rawTiles = tiles.AsValueEnumerable().Select(tile => tile.Raw).ToHashSet();
    Assert.True(rawTiles.Contains(new Vector3I(1, 0, 0)));
    Assert.True(rawTiles.Contains(new Vector3I(2, 0, 0)));
    Assert.False(tiles.AsValueEnumerable().Any(tile => tile.Raw == battle.Session.GetUnitPosition(unit).RequireSome().Raw));
  }

  [TestCase(TestName = "GetPossibleMoveTilesForUnit does not include unreachable tiles inside movement range")]
  public void GetPossibleMoveTilesForUnitDoesNotIncludeUnreachableTilesInsideMovementRange()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(SoloSetup(faction, "Runner", new Vector3I(0, 0, 0),
      actionPoints: 4, map: RowMapWithWall(4, 1)));
    var unit = battle.Unit;

    IReadOnlyCollection<BattleBoardState.ValidatedPoint> tiles = battle.Query(new GetPossibleMoveTilesForUnit(battle.Alive(unit)));
    Assert.True(tiles.Count == 0);
  }

  [TestCase(TestName = "GetVisibleEnemiesForUnit returns visible enemies only")]
  public void GetVisibleEnemiesForUnitReturnsVisibleEnemiesOnly()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(DuelSetup(playerFaction, "Observer", new Vector3I(0, 0, 0),
      enemyFaction, "Visible", new Vector3I(2, 0, 0), playerVision: 4, enemyVision: 1,
      map: RowMapWithWall(5, 3)));
    var observer = battle.Unit;
    var visibleEnemy = battle.UnitAt(new Vector3I(2, 0, 0));
    var hiddenEnemy = battle.Spawn(TestData.MakeCombatant("Hidden", enemyFaction, vision: 1), new Vector3I(4, 0, 0));

    IReadOnlyCollection<AliveUnit> enemies = battle.Query(new GetVisibleEnemiesForUnit(battle.Alive(observer)));

    Assert.True(enemies.AsValueEnumerable().Select(e => e.State).Contains(visibleEnemy));
    Assert.False(enemies.AsValueEnumerable().Select(e => e.State).Contains(hiddenEnemy));
  }

  [TestCase(TestName = "GetFactionDeadUnits filters by faction")]
  public void GetFactionDeadUnitsFiltersByFaction()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = new BattleFixture(DuelSetup(factionA, "A1", new Vector3I(0, 0, 0),
      factionB, "B1", new Vector3I(1, 0, 0), playerHealth: 10, enemyHealth: 10));
    var unitA = battle.Unit;

    battle.ApplyDamage(unitA, 10);

    var factionADead = battle.Query(new GetFactionDeadUnits(factionA));
    var factionBDead = battle.Query(new GetFactionDeadUnits(factionB));

    Assert.True(factionADead.AsValueEnumerable().Select(d => d.State).Contains(unitA));
    Assert.False(factionBDead.AsValueEnumerable().Select(d => d.State).Contains(unitA));
    Assert.True(!factionBDead.AsValueEnumerable().Any());
  }

}
