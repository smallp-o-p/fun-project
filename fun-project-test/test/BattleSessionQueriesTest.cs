using FunProject.Battle;
using FunProject.Tests;
using GdUnit4;
using Godot;
using System.Collections.Generic;
using System.Linq;
using static BattleActionTestHelper;
using static BattleQueryTestHelper;

[TestSuite]
[RequireGodotRuntime]
public class BattleSessionQueriesTest
{
  [TestCase(TestName = "Query runner returns failure object for foreign unit")]
  public void QueryRunnerReturnsFailureObjectForForeignUnit()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var foreignSession = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);
    var foreignUnit = SpawnUnit(foreignSession, BattleTestFactory.MakeCombatant("Foreign", faction), new Vector3I(0, 0, 0));

    Either<BattleQueryFailure, BattleBoardState.ValidatedPoint> result = Query(session, new GetUnitPosition(foreignUnit.State));

    Assert.True(result.IsLeft);
    BattleQueryFailure failure = GetFailure(result);
    Assert.Equal(BattleQueryFailureReason.UnknownUnit, failure.Reason);
  }

  [TestCase(TestName = "FindPathForUnit returns a successful path result")]
  public void FindPathForUnitReturnsASuccessfulPathResult()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Runner", faction), new Vector3I(0, 0, 0));

    BattleBoardState.ValidatedPoint[] path = GetValue(Query(session, new FindPathForUnit(unit.State, new Vector3I(2, 0, 0))));

    Assert.Equal(3, path.Length);
    Assert.Equal(new Vector3I(0, 0, 0), path[0].Raw);
    Assert.Equal(new Vector3I(2, 0, 0), path[^1].Raw);
  }

  [TestCase(TestName = "GetUnitPosition returns the board position for a spawned unit")]
  public void GetUnitPositionReturnsTheBoardPositionForASpawnedUnit()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Runner", faction), new Vector3I(0, 0, 0));

    BattleBoardState.ValidatedPoint position = GetValue(Query(session, new GetUnitPosition(unit.State)));

    Assert.Equal(new Vector3I(0, 0, 0), position.Raw);
  }

  [TestCase(TestName = "GetUnitAtTile returns the unit occupying a tile")]
  public void GetUnitAtTileReturnsTheUnitOccupyingATile()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Runner", faction), new Vector3I(1, 0, 0));

    Option<BattleUnitState> occupant = GetValue(Query(session, new GetUnitAtTile(new Vector3I(1, 0, 0))));

    Assert.True(occupant.IsSome);
    Assert.Equal(unit.State, occupant.RequireSome());
  }

  [TestCase(TestName = "GetUnitAtTile returns None for an empty tile")]
  public void GetUnitAtTileReturnsNoneForAnEmptyTile()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [faction]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Runner", faction), new Vector3I(1, 0, 0));

    Option<BattleUnitState> occupant = GetValue(Query(session, new GetUnitAtTile(new Vector3I(2, 0, 0))));

    Assert.True(occupant.IsNone);
  }

  [TestCase(TestName = "GetUnitAtTile returns failure for an invalid tile")]
  public void GetUnitAtTileReturnsFailureForAnInvalidTile()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [faction]);

    Either<BattleQueryFailure, Option<BattleUnitState>> result = Query(session, new GetUnitAtTile(new Vector3I(4, 0, 0)));

    Assert.True(result.IsLeft);
    BattleQueryFailure failure = GetFailure(result);
    Assert.Equal(BattleQueryFailureReason.InvalidTile, failure.Reason);
  }

  [TestCase(TestName = "GetPossibleMoveTilesForUnit respects action points")]
  public void GetPossibleMoveTilesForUnitRespectsActionPoints()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Runner", faction, actionPoints: 2), new Vector3I(0, 0, 0));
    StartBattle(session);

    IReadOnlyCollection<BattleBoardState.ValidatedPoint> tiles = GetValue(Query(session, new GetPossibleMoveTilesForUnit(unit.State)));
    Assert.True(tiles.Count == 2);
    Assert.True(tiles.Select(tile => tile.Raw).SequenceEqual(new List<Vector3I>([new Vector3I(1, 0, 0), new Vector3I(2, 0, 0)])));
    Assert.False(tiles.Any(tile => tile.Raw == session.GetUnitPosition(unit.State).RequireSome().Raw));
  }

  [TestCase(TestName = "GetPossibleMoveTilesForUnit does not include unreachable tiles inside movement range")]
  public void GetPossibleMoveTilesForUnitDoesNotIncludeUnreachableTilesInsideMovementRange()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Runner", faction, actionPoints: 4), new Vector3I(0, 0, 0));
    session.Board.GetTile(session.Board.ValidatePoint(new Vector3I(1, 0, 0)).RequireSome()).IsWalkable = false;
    StartBattle(session);

    IReadOnlyCollection<BattleBoardState.ValidatedPoint> tiles = GetValue(Query(session, new GetPossibleMoveTilesForUnit(unit.State)));
    Assert.True(tiles.Count == 0);
  }

  [TestCase(TestName = "GetVisibleEnemiesForUnit returns visible enemies only")]
  public void GetVisibleEnemiesForUnitReturnsVisibleEnemiesOnly()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 1), [playerFaction, enemyFaction]);
    var observer = SpawnUnit(session, BattleTestFactory.MakeCombatant("Observer", playerFaction, vision: 4), new Vector3I(0, 0, 0));
    var visibleEnemy = SpawnUnit(session, BattleTestFactory.MakeCombatant("Visible", enemyFaction, vision: 1), new Vector3I(2, 0, 0));
    var hiddenEnemy = SpawnUnit(session, BattleTestFactory.MakeCombatant("Hidden", enemyFaction, vision: 1), new Vector3I(4, 0, 0));
    session.Board.GetTile(session.Board.ValidatePoint(new Vector3I(3, 0, 0)).RequireSome()).BlocksLineOfSight = true;
    StartBattle(session);

    IReadOnlyCollection<BattleUnitState> enemies = GetValue(Query(session, new GetVisibleEnemiesForUnit(observer.State)));

    Assert.True(enemies.Contains(visibleEnemy));
    Assert.False(enemies.Contains(hiddenEnemy));
  }

}
