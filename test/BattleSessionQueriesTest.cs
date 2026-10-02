using FunProject.Battle;
using GdUnit4;
using Godot;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public class BattleSessionQueriesTest
{
  [TestCase(TestName = "FindPathForUnit returns a successful path result")]
  public void FindPathForUnitReturnsASuccessfulPathResult()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 1), [faction]);
    var unit = battle.Spawn(TestData.MakeCombatant("Runner", faction), new Vector3I(0, 0, 0));

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
    using var battle = new BattleFixture(new Vector3I(4, 1, 1), [faction]);
    var unit = battle.Spawn(TestData.MakeCombatant("Runner", faction), new Vector3I(1, 0, 0));

    Option<BattleUnitState> occupant = battle.Query(new GetUnitAtTile(battle.At(x, 0, 0)));

    Assert.Equal(x == 1, occupant.IsSome);
    if (x == 1)
      Assert.Equal(unit, occupant.RequireSome());
  }

  [TestCase(TestName = "GetPossibleMoveTilesForUnit respects action points")]
  public void GetPossibleMoveTilesForUnitRespectsActionPoints()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 1), [faction]);
    var unit = battle.Spawn(TestData.MakeCombatant("Runner", faction, actionPoints: 2), new Vector3I(0, 0, 0));
    battle.Start();

    IReadOnlyCollection<BattleBoardState.ValidatedPoint> tiles = battle.Query(new GetPossibleMoveTilesForUnit(battle.Alive(unit)));
    Assert.True(tiles.Count == 2);
    var rawTiles = tiles.AsValueEnumerable().Select(tile => tile.Raw).ToHashSet();
    Assert.True(rawTiles.Contains(new Vector3I(1, 0, 0)));
    Assert.True(rawTiles.Contains(new Vector3I(2, 0, 0)));
    Assert.False(tiles.AsValueEnumerable().Any(tile => tile.Raw == battle.PositionOf(unit).RequireSome().Raw));
  }

  [TestCase(TestName = "GetPossibleMoveTilesForUnit does not include unreachable tiles inside movement range")]
  public void GetPossibleMoveTilesForUnitDoesNotIncludeUnreachableTilesInsideMovementRange()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 1), [faction]);
    var unit = battle.Spawn(TestData.MakeCombatant("Runner", faction, actionPoints: 4), new Vector3I(0, 0, 0));
    battle.Board.SetTileWalkable(battle.At(1, 0, 0), false);
    battle.Start();

    IReadOnlyCollection<BattleBoardState.ValidatedPoint> tiles = battle.Query(new GetPossibleMoveTilesForUnit(battle.Alive(unit)));
    Assert.True(tiles.Count == 0);
  }

  [TestCase(TestName = "GetVisibleEnemiesForUnit returns visible enemies only")]
  public void GetVisibleEnemiesForUnitReturnsVisibleEnemiesOnly()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(5, 1, 1), [playerFaction, enemyFaction]);
    var observer = battle.Spawn(TestData.MakeCombatant("Observer", playerFaction, vision: 4), new Vector3I(0, 0, 0));
    var visibleEnemy = battle.Spawn(TestData.MakeCombatant("Visible", enemyFaction, vision: 1), new Vector3I(2, 0, 0));
    var hiddenEnemy = battle.Spawn(TestData.MakeCombatant("Hidden", enemyFaction, vision: 1), new Vector3I(4, 0, 0));
    battle.Board.GetTile(battle.At(3, 0, 0)).BlocksLineOfSight = true;
    battle.Start();

    IReadOnlyCollection<AliveUnit> enemies = battle.Query(new GetVisibleEnemiesForUnit(battle.Alive(observer)));

    Assert.True(enemies.AsValueEnumerable().Select(e => e.State).Contains(visibleEnemy));
    Assert.False(enemies.AsValueEnumerable().Select(e => e.State).Contains(hiddenEnemy));
  }

  [TestCase(TestName = "GetFactionDeadUnits filters by faction")]
  public void GetFactionDeadUnitsFiltersByFaction()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = new BattleFixture(new Vector3I(4, 1, 1), [factionA, factionB]);
    var unitA = battle.Spawn(TestData.MakeCombatant("A1", factionA, health: 10), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB, health: 10), new Vector3I(1, 0, 0));
    battle.Start();

    battle.ApplyDamage(unitA, 10);

    var factionADead = battle.Query(new GetFactionDeadUnits(factionA));
    var factionBDead = battle.Query(new GetFactionDeadUnits(factionB));

    Assert.True(factionADead.AsValueEnumerable().Select(d => d.State).Contains(unitA));
    Assert.False(factionBDead.AsValueEnumerable().Select(d => d.State).Contains(unitA));
    Assert.True(!factionBDead.AsValueEnumerable().Any());
  }

}
