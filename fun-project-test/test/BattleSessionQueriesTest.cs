using FunProject.Battle;
using GdUnit4;
using Godot;
using System.Collections.Generic;
using System.Linq;

[TestSuite]
[RequireGodotRuntime]
public class BattleSessionQueriesTest
{
  [TestCase(TestName = "FindPathForUnit returns a successful path result")]
  public void FindPathForUnitReturnsASuccessfulPathResult()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Runner", faction), new Vector3I(0, 0, 0));

    BattleBoardState.ValidatedPoint[] path = Query(session, new FindPathForUnit(unit.AliveIn(session), session.Board.At(2, 0, 0)));

    Assert.Equal(3, path.Length);
    Assert.Equal(new Vector3I(0, 0, 0), path[0].Raw);
    Assert.Equal(new Vector3I(2, 0, 0), path[^1].Raw);
  }

  [TestCase(TestName = "Minted AliveUnit carries the board position for a spawned unit")]
  public void MintedAliveUnitCarriesTheBoardPositionForASpawnedUnit()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Runner", faction), new Vector3I(0, 0, 0));

    BattleBoardState.ValidatedPoint position = unit.AliveIn(session).Position;

    Assert.Equal(new Vector3I(0, 0, 0), position.Raw);
  }

  [TestCase(TestName = "GetUnitAtTile returns the unit occupying a tile")]
  public void GetUnitAtTileReturnsTheUnitOccupyingATile()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Runner", faction), new Vector3I(1, 0, 0));

    Option<BattleUnitState> occupant = Query(session, new GetUnitAtTile(session.Board.At(1, 0, 0)));

    Assert.True(occupant.IsSome);
    Assert.Equal(unit.State, occupant.RequireSome());
  }

  [TestCase(TestName = "GetUnitAtTile returns None for an empty tile")]
  public void GetUnitAtTileReturnsNoneForAnEmptyTile()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [faction]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Runner", faction), new Vector3I(1, 0, 0));

    Option<BattleUnitState> occupant = Query(session, new GetUnitAtTile(session.Board.At(2, 0, 0)));

    Assert.True(occupant.IsNone);
  }

  [TestCase(TestName = "GetPossibleMoveTilesForUnit respects action points")]
  public void GetPossibleMoveTilesForUnitRespectsActionPoints()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Runner", faction, actionPoints: 2), new Vector3I(0, 0, 0));
    StartBattle(session);

    IReadOnlyCollection<BattleBoardState.ValidatedPoint> tiles = Query(session, new GetPossibleMoveTilesForUnit(unit.AliveIn(session)));
    Assert.True(tiles.Count == 2);
    var rawTiles = tiles.Select(tile => tile.Raw).ToHashSet();
    Assert.True(rawTiles.Contains(new Vector3I(1, 0, 0)));
    Assert.True(rawTiles.Contains(new Vector3I(2, 0, 0)));
    Assert.False(tiles.Any(tile => tile.Raw == session.GetUnitPosition(unit.State).RequireSome().Raw));
  }

  [TestCase(TestName = "GetPossibleMoveTilesForUnit does not include unreachable tiles inside movement range")]
  public void GetPossibleMoveTilesForUnitDoesNotIncludeUnreachableTilesInsideMovementRange()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Runner", faction, actionPoints: 4), new Vector3I(0, 0, 0));
    session.Board.SetTileWalkable(session.Board.At(1, 0, 0), false);
    StartBattle(session);

    IReadOnlyCollection<BattleBoardState.ValidatedPoint> tiles = Query(session, new GetPossibleMoveTilesForUnit(unit.AliveIn(session)));
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
    session.Board.GetTile(session.Board.At(3, 0, 0)).BlocksLineOfSight = true;
    StartBattle(session);

    IReadOnlyCollection<AliveUnit> enemies = Query(session, new GetVisibleEnemiesForUnit(observer.AliveIn(session)));

    Assert.True(enemies.Select(e => e.State).Contains(visibleEnemy));
    Assert.False(enemies.Select(e => e.State).Contains(hiddenEnemy));
  }

  [TestCase(TestName = "GetFactionDeadUnits filters by faction")]
  public void GetFactionDeadUnitsFiltersByFaction()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [factionA, factionB]);
    var unitA = SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", factionA, health: 10), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", factionB, health: 10), new Vector3I(1, 0, 0));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    var result = executor.Submit(BattleAction.ApplyDamage(unitA.State, 10)).RequireSingleResult();
    Assert.True(result.Succeeded);

    var factionADead = Query(session, new GetFactionDeadUnits(factionA));
    var factionBDead = Query(session, new GetFactionDeadUnits(factionB));

    Assert.True(factionADead.Select(d => d.State).Contains(unitA));
    Assert.False(factionBDead.Select(d => d.State).Contains(unitA));
    Assert.True(!factionBDead.Any());
  }

}
