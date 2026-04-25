using FunProject.Battle;
using FunProject.Combatants;
using GdUnit4;
using Godot;
using System.Collections.Generic;
using System.Linq;
using static BattleQueryTestHelper;

[TestSuite]
[RequireGodotRuntime]
public class BattleSessionQueriesTest
{
  [TestCase(TestName = "Query runner returns failure object for unknown living unit")]
  public void QueryRunnerReturnsFailureObjectForUnknownLivingUnit()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [faction]);

    BattleQueryResult<BattleUnitState> result = session.Queries.Execute(new GetLivingUnit(404));

    Assert.False(result.Succeeded);
    BattleQueryFailure failure = GetFailure(result);
    Assert.Equal(BattleQueryFailureReason.UnknownUnit, failure.Reason);
  }

  [TestCase(TestName = "FindPathForUnit returns a successful path result")]
  public void FindPathForUnitReturnsASuccessfulPathResult()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Runner", faction), new Vector3I(0, 0, 0));

    Vector3I[] path = GetValue(session.Queries.Execute(new FindPathForUnit(unit.UnitId, new Vector3I(2, 0, 0))));

    Assert.Equal(3, path.Length);
    Assert.Equal(new Vector3I(0, 0, 0), path[0]);
    Assert.Equal(new Vector3I(2, 0, 0), path[^1]);
  }

  [TestCase(TestName = "GetPossibleMoveTilesForUnit respects action points")]
  public void GetPossibleMoveTilesForUnitRespectsActionPoints()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Runner", faction, actionPoints: 2), new Vector3I(0, 0, 0));
    StartBattle(session);

    IReadOnlyCollection<Vector3I> tiles = GetValue(session.Queries.Execute(new GetPossibleMoveTilesForUnit(unit.UnitId)));

    Assert.True(tiles.Contains(new Vector3I(1, 0, 0)));
    Assert.True(tiles.Contains(new Vector3I(2, 0, 0)));
    Assert.False(tiles.Contains(new Vector3I(3, 0, 0)));
    Assert.False(tiles.Contains(unit.Position));
  }

  [TestCase(TestName = "GetPossibleMoveTilesForUnit does not include unreachable tiles inside movement range")]
  public void GetPossibleMoveTilesForUnitDoesNotIncludeUnreachableTilesInsideMovementRange()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Runner", faction, actionPoints: 4), new Vector3I(0, 0, 0));
    session.Board.GetTile(new Vector3I(1, 0, 0)).IsWalkable = false;
    StartBattle(session);

    IReadOnlyCollection<Vector3I> tiles = GetValue(session.Queries.Execute(new GetPossibleMoveTilesForUnit(unit.UnitId)));

    Assert.False(tiles.Contains(new Vector3I(1, 0, 0)));
    Assert.False(tiles.Contains(new Vector3I(2, 0, 0)));
    Assert.False(tiles.Contains(new Vector3I(3, 0, 0)));
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
    session.Board.GetTile(new Vector3I(3, 0, 0)).BlocksLineOfSight = true;
    StartBattle(session);

    IReadOnlyCollection<BattleUnitState> enemies = GetValue(session.Queries.Execute(new GetVisibleEnemiesForUnit(observer.UnitId)));

    Assert.True(enemies.Contains(visibleEnemy));
    Assert.False(enemies.Contains(hiddenEnemy));
  }

  [TestCase(TestName = "Tile visibility query returns failure for invalid tile")]
  public void TileVisibilityQueryReturnsFailureForInvalidTile()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(2, 1, 2), [faction]);

    BattleQueryResult<bool> result = session.Queries.Execute(new IsTileVisibleToFaction(faction, new Vector3I(4, 0, 0)));

    Assert.False(result.Succeeded);
    BattleQueryFailure failure = GetFailure(result);
    Assert.Equal(BattleQueryFailureReason.InvalidTile, failure.Reason);
  }

  private static BattleUnitState SpawnUnit(BattleSession session, Combatant combatant, Vector3I position)
  {
    var result = BattleSessionMutation.SpawnUnit(combatant, position).Execute(session);
    Assert.True(result.Succeeded);
    if (result.AffectedUnit == null)
      throw new System.InvalidOperationException("Spawn unit mutation succeeded without an affected unit.");

    return result.AffectedUnit;
  }

  private static void StartBattle(BattleSession session)
  {
    var result = BattleSessionMutation.StartBattle().Execute(session);
    Assert.True(result.Succeeded);
  }
}
