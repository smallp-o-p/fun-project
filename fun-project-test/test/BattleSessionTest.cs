using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Stats;
using FunProject.Tests;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using static BattleActionTestHelper;
using static BattleQueryTestHelper;

[TestSuite]
[RequireGodotRuntime]
public class BattleSessionTest
{
  [TestCase(TestName = "Board dimensions follow Vector3I axis order")]
  public void BoardDimensionsFollowVector3IAxisOrder()
  {
    var board = new BattleBoardState(new Vector3I(4, 2, 5));

    Assert.True(board.ValidatePoint(new Vector3I(3, 1, 4)).IsSome);
    Assert.True(board.ValidatePoint(new Vector3I(3, 2, 4)).IsNone);
    Assert.True(board.ValidatePoint(new Vector3I(3, 1, 5)).IsNone);

    BattleBoardState.ValidatedPoint point = board.ValidatePoint(new Vector3I(3, 1, 4)).RequireSome();
    Assert.Equal(new Vector3I(3, 1, 4), point.Raw);
  }

  [TestCase(TestName = "SpawnUnit occupies its tile")]
  public void SpawnUnitOccupiesItsTile()
  {
    var faction = BattleTestFactory.MakeFaction("City Guard");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 2, 4), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));

    var tile = session.Board.GetTile(session.Board.ValidatePoint(new Vector3I(1, 0, 1)).RequireSome());
    Assert.True(tile.IsOccupied);
    Assert.Equal(unit.UnitId, tile.OccupantUnitId.RequireSome());
  }

  [TestCase(TestName = "SpawnUnit rejects occupied tile")]
  public void SpawnUnitRejectsOccupiedTile()
  {
    var faction = BattleTestFactory.MakeFaction("City Guard");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);

    SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));
    var executor = new BattleActionExecutor(session);
    var result = executor.Submit(BattleAction.SpawnUnit(BattleTestFactory.MakeCombatant("Bravo", faction), new Vector3I(1, 0, 1))).RequireSingleResult();

    Assert.False(result.Succeeded);
  }

  [TestCase(TestName = "StartBattle activates first participating faction")]
  public void StartBattleActivatesFirstParticipatingFaction()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [playerFaction, enemyFaction]);

    SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", playerFaction), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Bandit", enemyFaction), new Vector3I(1, 0, 0));
    StartBattle(session);

    Assert.Equal(playerFaction, session.ActiveSide);
    Assert.Equal(1, session.TurnNumber);
  }

  [TestCase(TestName = "Global faction order keeps each faction only once")]
  public void GlobalFactionOrderKeepsEachFactionOnlyOnce()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [factionA]);

    SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("A2", factionA), new Vector3I(1, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", factionB), new Vector3I(2, 0, 0));

    Assert.Equal(2, session.GlobalFactionTurnOrder.Count);
    Assert.Equal(factionA, session.GlobalFactionTurnOrder.First());
    Assert.Equal(factionB, session.GlobalFactionTurnOrder.Last());
    Assert.Equal(2, session.TurnQueue.Count);
    Assert.Equal(factionA, session.TurnQueue.First());
    Assert.Equal(factionB, session.TurnQueue.Last());
  }

  [TestCase(TestName = "Constructor seeds global faction order and faction rosters")]
  public void ConstructorSeedsGlobalFactionOrderAndFactionRosters()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var combatantA = BattleTestFactory.MakeCombatant("A1", factionA);
    var combatantB = BattleTestFactory.MakeCombatant("B1", factionB);
    var session = new BattleSession(
      new BattleBoardState(new Vector3I(4, 1, 4)),
      [factionB, factionA, factionB],
      new Dictionary<Faction, IEnumerable<Combatant>>
      {
        [factionA] = [combatantA],
        [factionB] = [combatantB],
      });

    Assert.Equal(2, session.GlobalFactionTurnOrder.Count);
    Assert.Equal(factionB, session.GlobalFactionTurnOrder.First());
    Assert.Equal(factionA, session.GlobalFactionTurnOrder.Last());
    Assert.True(session.FactionRosters[factionA].Contains(combatantA));
    Assert.True(session.FactionRosters[factionB].Contains(combatantB));
    Assert.Equal(factionB, session.ActiveSide);
    Assert.Equal(factionB, session.TurnQueue.First());
    Assert.Equal(0, session.AliveUnits.Count);
    Assert.False(session.Board.GetTile(session.Board.ValidatePoint(new Vector3I(0, 0, 0)).RequireSome()).IsOccupied);
  }

  [TestCase(TestName = "Constructor can use a prebuilt board state")]
  public void ConstructorCanUseAPrebuiltBoardState()
  {
    var faction = BattleTestFactory.MakeFaction("A");
    var board = new BattleBoardState(new Vector3I(3, 1, 3));
    board.GetTile(board.ValidatePoint(new Vector3I(1, 0, 0)).RequireSome()).IsWalkable = false;

    var session = new BattleSession(
      board,
      [faction],
      new Dictionary<Faction, IEnumerable<Combatant>>());

    var executor = new BattleActionExecutor(session);
    var result = executor.Submit(BattleAction.SpawnUnit(BattleTestFactory.MakeCombatant("A1", faction), new Vector3I(1, 0, 0))).RequireSingleResult();

    Assert.True(object.ReferenceEquals(board, session.Board));
    Assert.False(result.Succeeded);
  }

  [TestCase(TestName = "Constructor initializes active side from turn queue")]
  public void ConstructorInitializesActiveSideFromTurnQueue()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 3), [factionA, factionB]);

    Assert.Equal(factionA, session.ActiveSide);
    Assert.Equal(factionA, session.TurnQueue.First());
  }

  [TestCase(TestName = "Constructor rejects sessions without factions")]
  public void ConstructorRejectsSessionsWithoutFactions()
  {
    var board = new BattleBoardState(new Vector3I(3, 1, 3));

    Assert.Throws<ArgumentException>(() => new BattleSession(
      board,
      [],
      new Dictionary<Faction, IEnumerable<Combatant>>()));
  }

  [TestCase(TestName = "GetUnit returns None for foreign handle")]
  public void GetUnitReturnsNoneForForeignHandle()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var foreignSession = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var foreignUnit = SpawnUnit(foreignSession, BattleTestFactory.MakeCombatant("Foreign", faction), new Vector3I(0, 0, 0));

    Assert.True(IsNone(session.GetUnit(foreignUnit.Handle)));
  }

  [TestCase(TestName = "GetLivingUnit returns None for dead unit")]
  public void GetLivingUnitReturnsNoneForDeadUnit()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, health: 10), new Vector3I(0, 0, 0));
    StartBattle(session);

    Assert.True(IsSome(session.GetLivingUnit(unit.Handle)));

    ApplyDamage(session, unit.Handle, 10);

    Assert.True(IsSome(session.GetUnit(unit.Handle)));
    Assert.True(IsNone(session.GetLivingUnit(unit.Handle)));
  }

  [TestCase(TestName = "AddUnit accepts equipped weapon option")]
  public void AddUnitAcceptsEquippedWeaponOption()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var weapon = new MeleeWeapon(new WeaponData
    {
      DamageStat = new DamageStat { BaseValue = 10 },
      CriticalChanceStat = new CriticalChanceStat { BaseValue = 5 },
      RangeStat = new RangeStat { BaseValue = 1 },
    });

    var spawnedUnit = session.AddUnit(
      BattleTestFactory.MakeCombatant("Alpha", faction),
      new Vector3I(0, 0, 0),
      Some<Weapon>(weapon));

    Assert.Equal(weapon, spawnedUnit.Unit.EquippedWeapon.RequireSome());
  }

  [TestCase(TestName = "AdvanceTurn rotates only participating factions without incrementing early")]
  public void AdvanceTurnRotatesOnlyParticipatingFactionsWithoutIncrementingEarly()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var factionC = BattleTestFactory.MakeFaction("C");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [factionA, factionB, factionC]);

    SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("C1", factionC), new Vector3I(2, 0, 0));
    StartBattle(session);

    Assert.Equal(factionA, session.ActiveSide);

    AdvanceTurn(session);
    Assert.Equal(factionB, session.ActiveSide);
    Assert.Equal(1, session.TurnNumber);

    AdvanceTurn(session);
    Assert.Equal(factionC, session.ActiveSide);
    Assert.Equal(1, session.TurnNumber);

    AdvanceTurn(session);
    Assert.Equal(factionA, session.ActiveSide);
    Assert.Equal(2, session.TurnNumber);
  }

  [TestCase(TestName = "Passing the last available unit advances the global turn counter")]
  public void PassingTheLastAvailableUnitAdvancesTheGlobalTurnCounter()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [factionA, factionB]);
    var unitA = SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    var unitB = SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    StartBattle(session);

    Assert.Equal(1, session.TurnNumber);
    Assert.Equal(factionA, session.ActiveSide);

    PassUnit(session, unitA.Handle);
    Assert.Equal(1, session.TurnNumber);
    Assert.Equal(factionB, session.ActiveSide);

    PassUnit(session, unitB.Handle);
    Assert.Equal(2, session.TurnNumber);
    Assert.Equal(factionA, session.ActiveSide);
  }

  [TestCase(TestName = "Eliminating an unacted faction does not block the global turn counter")]
  public void EliminatingAnUnactedFactionDoesNotBlockTheGlobalTurnCounter()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var factionC = BattleTestFactory.MakeFaction("C");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [factionA, factionB, factionC]);

    SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    var unitC = SpawnUnit(session, BattleTestFactory.MakeCombatant("C1", factionC, health: 10), new Vector3I(2, 0, 0));
    StartBattle(session);

    ApplyDamage(session, unitC.Handle, 10);
    Assert.False(session.TurnQueue.Contains(factionC));

    AdvanceTurn(session);
    Assert.Equal(1, session.TurnNumber);
    Assert.Equal(factionB, session.ActiveSide);

    AdvanceTurn(session);
    Assert.Equal(2, session.TurnNumber);
    Assert.Equal(factionA, session.ActiveSide);
  }

  [TestCase(TestName = "Adding a faction mid-round delays the global turn counter until it acts")]
  public void AddingAFactionMidRoundDelaysTheGlobalTurnCounterUntilItActs()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var factionC = BattleTestFactory.MakeFaction("C");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [factionA, factionB]);

    SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    StartBattle(session);

    SpawnUnit(session, BattleTestFactory.MakeCombatant("C1", factionC), new Vector3I(2, 0, 0));

    Assert.Equal(1, session.TurnNumber);
    Assert.True(session.TurnQueue.Contains(factionC));

    AdvanceTurn(session);
    Assert.Equal(1, session.TurnNumber);
    Assert.Equal(factionB, session.ActiveSide);

    AdvanceTurn(session);
    Assert.Equal(1, session.TurnNumber);
    Assert.Equal(factionC, session.ActiveSide);

    AdvanceTurn(session);
    Assert.Equal(2, session.TurnNumber);
    Assert.Equal(factionA, session.ActiveSide);
  }

  [TestCase(TestName = "Adding units to a faction that already acted waits until the next round")]
  public void AddingUnitsToAFactionThatAlreadyActedWaitsUntilTheNextRound()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [factionA, factionB]);

    SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    StartBattle(session);

    AdvanceTurn(session);
    Assert.Equal(factionB, session.ActiveSide);
    Assert.Equal(1, session.TurnNumber);

    SpawnUnit(session, BattleTestFactory.MakeCombatant("A2", factionA), new Vector3I(2, 0, 0));

    AdvanceTurn(session);
    Assert.Equal(2, session.TurnNumber);
    Assert.Equal(factionA, session.ActiveSide);
  }

  [TestCase(TestName = "Adding a unit mid-battle reconciles the faction queue")]
  public void AddingAUnitMidBattleReconcilesTheFactionQueue()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var factionC = BattleTestFactory.MakeFaction("C");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [factionA, factionB]);

    SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    StartBattle(session);

    SpawnUnit(session, BattleTestFactory.MakeCombatant("C1", factionC), new Vector3I(2, 0, 0));

    Assert.Equal(factionA, session.ActiveSide);
    Assert.True(session.TurnQueue.Contains(factionA));
    Assert.True(session.TurnQueue.Contains(factionB));
    Assert.True(session.TurnQueue.Contains(factionC));
  }

  [TestCase(TestName = "Move updates unit position occupancy and action points")]
  public void MoveUpdatesUnitPositionOccupancyAndActionPoints()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 2, 4), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction, actionPoints: 5), new Vector3I(1, 0, 1));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    var moved = executor.Submit(BattleAction.MoveUnitStep(unit.Handle, new Vector3I(1, 1, 1), 2)).RequireSingleResult();

    Assert.True(moved.Succeeded);
    Assert.Equal(new Vector3I(1, 1, 1), session.GetUnitPosition(unit.Handle).RequireSome().Raw);
    Assert.False(session.Board.GetTile(session.Board.ValidatePoint(new Vector3I(1, 0, 1)).RequireSome()).IsOccupied);
    Assert.True(session.Board.GetTile(session.Board.ValidatePoint(new Vector3I(1, 1, 1)).RequireSome()).IsOccupied);
    Assert.Equal(3, unit.CurrentActionPoints);
  }

  [TestCase(TestName = "Move rejects non-adjacent destination")]
  public void MoveRejectsNonAdjacentDestination()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    var result = executor.Submit(BattleAction.MoveUnitStep(unit.Handle, new Vector3I(2, 0, 0))).RequireSingleResult();
    Assert.False(result.Succeeded);
  }

  [TestCase(TestName = "Vertical move is allowed as a one-cell step")]
  public void VerticalMoveIsAllowedAsAOneCellStep()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 3, 3), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Climber", faction), new Vector3I(1, 0, 1));
    StartBattle(session);

    var executor = new BattleActionExecutor(session);
    var result = executor.Submit(BattleAction.MoveUnitStep(unit.Handle, new Vector3I(1, 1, 1))).RequireSingleResult();
    Assert.True(result.Succeeded);
    Assert.Equal(new Vector3I(1, 1, 1), session.GetUnitPosition(unit.Handle).RequireSome().Raw);
  }

  [TestCase(TestName = "MoveUnit follows a multi-step board path and spends AP per step")]
  public void MoveUnitFollowsAMultiStepBoardPathAndSpendsAPPerStep()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Runner", faction, actionPoints: 5), new Vector3I(0, 0, 0));
    StartBattle(session);

    BattleBoardState.ValidatedPoint[] validatedPath = GetValue(Query(session, new FindPathForUnit(unit.Handle, new Vector3I(2, 0, 0))));
    Vector3I[] path = validatedPath.Select(point => point.Raw).ToArray();
    Assert.Equal(3, path.Length);

    var executor = new BattleActionExecutor(session);
    IReadOnlyList<BattleActionResult> moveResults = executor.Submit(BattleAction.MoveUnit(unit.Handle, path));

    Assert.Equal(2, moveResults.Count);
    Assert.True(moveResults.All(result => result.Succeeded));
    Assert.True(moveResults.All(result => result.Action is MoveUnitStep));
    Assert.Equal(new Vector3I(2, 0, 0), session.GetUnitPosition(unit.Handle).RequireSome().Raw);
    Assert.False(session.Board.GetTile(session.Board.ValidatePoint(new Vector3I(0, 0, 0)).RequireSome()).IsOccupied);
    Assert.True(session.Board.GetTile(session.Board.ValidatePoint(new Vector3I(2, 0, 0)).RequireSome()).IsOccupied);
    Assert.Equal(3, unit.CurrentActionPoints);
  }

  [TestCase(TestName = "Session-owned unit position follows movement and clears on death")]
  public void SessionOwnedUnitPositionFollowsMovementAndClearsOnDeath()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Runner", faction, health: 10, actionPoints: 5), new Vector3I(0, 0, 0));
    StartBattle(session);

    Assert.Equal(new Vector3I(0, 0, 0), session.GetUnitPosition(unit.Handle).RequireSome().Raw);

    var executor = new BattleActionExecutor(session);
    var moved = executor.Submit(BattleAction.MoveUnitStep(unit.Handle, new Vector3I(1, 0, 0))).RequireSingleResult();

    Assert.True(moved.Succeeded);
    Assert.Equal(new Vector3I(1, 0, 0), session.GetUnitPosition(unit.Handle).RequireSome().Raw);

    ApplyDamage(session, unit.Handle, 10);

    Assert.True(session.GetUnitPosition(unit.Handle).IsNone);
  }

  [TestCase(TestName = "MoveUnit rejects paths that do not start at the current position")]
  public void MoveUnitRejectsPathsThatDoNotStartAtTheCurrentPosition()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Runner", faction, actionPoints: 5), new Vector3I(0, 0, 0));
    StartBattle(session);

    var action = BattleAction.MoveUnit(unit.Handle, [new Vector3I(1, 0, 0), new Vector3I(2, 0, 0)]);
    var executor = new BattleActionExecutor(session);
    var moved = executor.Submit(action);

    Assert.Equal(0, moved.Count);
    Assert.True(action.IsDone());
    Assert.Equal(new Vector3I(0, 0, 0), session.GetUnitPosition(unit.Handle).RequireSome().Raw);
    Assert.True(session.Board.GetTile(session.Board.ValidatePoint(new Vector3I(0, 0, 0)).RequireSome()).IsOccupied);
    Assert.False(session.Board.GetTile(session.Board.ValidatePoint(new Vector3I(2, 0, 0)).RequireSome()).IsOccupied);
  }

  [TestCase(TestName = "MoveUnit rejects invalid composed steps")]
  public void MoveUnitRejectsInvalidComposedSteps()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Runner", faction, actionPoints: 5), new Vector3I(0, 0, 0));
    StartBattle(session);

    var action = BattleAction.MoveUnit(
      unit.Handle,
      [session.GetUnitPosition(unit.Handle).RequireSome().Raw, new Vector3I(2, 0, 0)]);
    var executor = new BattleActionExecutor(session);
    var moved = executor.Submit(action);

    Assert.Equal(0, moved.Count);
    Assert.True(action.IsDone());
    Assert.Equal(new Vector3I(0, 0, 0), session.GetUnitPosition(unit.Handle).RequireSome().Raw);
    Assert.Equal(5, unit.CurrentActionPoints);
    Assert.True(session.Board.GetTile(session.Board.ValidatePoint(new Vector3I(0, 0, 0)).RequireSome()).IsOccupied);
  }

  [TestCase(TestName = "MoveUnit rejects paths that cost more AP than the unit has")]
  public void MoveUnitRejectsPathsThatCostMoreApThanTheUnitHas()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Runner", faction, actionPoints: 1), new Vector3I(0, 0, 0));
    StartBattle(session);

    BattleBoardState.ValidatedPoint[] validatedPath = GetValue(Query(session, new FindPathForUnit(unit.Handle, new Vector3I(2, 0, 0))));
    Vector3I[] path = validatedPath.Select(point => point.Raw).ToArray();
    Assert.Equal(3, path.Length);

    var action = BattleAction.MoveUnit(unit.Handle, path);
    var executor = new BattleActionExecutor(session);
    var moved = executor.Submit(action);

    Assert.Equal(0, moved.Count);
    Assert.True(action.IsDone());
    Assert.Equal(new Vector3I(0, 0, 0), session.GetUnitPosition(unit.Handle).RequireSome().Raw);
    Assert.Equal(1, unit.CurrentActionPoints);
    Assert.True(session.Board.GetTile(session.Board.ValidatePoint(new Vector3I(0, 0, 0)).RequireSome()).IsOccupied);
  }

  [TestCase(TestName = "Passing a unit ends its activation while keeping the next ally available")]
  public void PassingAUnitEndsItsActivationWhileKeepingTheNextAllyAvailable()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unitA = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
    var unitB = SpawnUnit(session, BattleTestFactory.MakeCombatant("Bravo", faction), new Vector3I(1, 0, 0));
    StartBattle(session);

    PassUnit(session, unitA.Handle);

    Assert.False(GetValue(Query(session, new IsUnitStillAvailableThisTurn(unitA.Handle))));
    Assert.False(GetValue(Query(session, new CanUnitActNow(unitA.Handle))));
    Assert.True(GetValue(Query(session, new IsUnitStillAvailableThisTurn(unitB.Handle))));
    Assert.Equal(faction, session.ActiveSide);
  }

  [TestCase(TestName = "Killing the only active unit on an eliminated faction does not auto-advance")]
  public void KillingTheOnlyActiveUnitOnAnEliminatedFactionDoesNotAutoAdvance()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [factionA, factionB]);
    var unitA = SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", factionA, health: 10), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", factionB, health: 10), new Vector3I(1, 0, 0));
    StartBattle(session);

    Assert.Equal(factionA, session.ActiveSide);

    ApplyDamage(session, unitA.Handle, 10);

    Assert.False(session.Board.GetTile(session.Board.ValidatePoint(new Vector3I(0, 0, 0)).RequireSome()).IsOccupied);
    Assert.Equal(factionA, session.ActiveSide);
    Assert.False(session.AliveUnits.Contains(unitA));
    Assert.True(session.DeadUnits.Contains(unitA));
    Assert.False(GetValue(Query(session, new GetFactionAliveUnits(factionA))).Contains(unitA));
    Assert.True(GetValue(Query(session, new GetFactionDeadUnits(factionA))).Contains(unitA));
    Assert.True(session.TurnQueue.Contains(factionA));
    Assert.True(session.TurnQueue.Contains(factionB));

    AdvanceTurn(session);
    Assert.Equal(factionB, session.ActiveSide);
  }

  [TestCase(TestName = "Killing an active unit leaves another surviving ally actable")]
  public void KillingAnActiveUnitLeavesAnotherSurvivingAllyActable()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [factionA, factionB]);
    var unitA1 = SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", factionA, health: 10), new Vector3I(0, 0, 0));
    var unitA2 = SpawnUnit(session, BattleTestFactory.MakeCombatant("A2", factionA, health: 10), new Vector3I(1, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", factionB, health: 10), new Vector3I(2, 0, 0));
    StartBattle(session);

    ApplyDamage(session, unitA1.Handle, 10);

    Assert.Equal(factionA, session.ActiveSide);
    Assert.True(GetValue(Query(session, new CanUnitActNow(unitA2.Handle))));
    Assert.False(session.Board.GetTile(session.Board.ValidatePoint(new Vector3I(0, 0, 0)).RequireSome()).IsOccupied);
    Assert.True(session.DeadUnits.Contains(unitA1));
  }

  [TestCase(TestName = "Killing the last actable unit does not auto-advance even if the faction survives")]
  public void KillingTheLastActableUnitDoesNotAutoAdvanceEvenIfTheFactionSurvives()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [factionA, factionB]);
    var unitA1 = SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", factionA, health: 10, actionPoints: 4), new Vector3I(0, 0, 0));
    var unitA2 = SpawnUnit(session, BattleTestFactory.MakeCombatant("A2", factionA, health: 10, actionPoints: 0), new Vector3I(1, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", factionB, health: 10), new Vector3I(2, 0, 0));
    StartBattle(session);

    Assert.False(GetValue(Query(session, new CanUnitActNow(unitA2.Handle))));

    ApplyDamage(session, unitA1.Handle, 10);

    Assert.Equal(factionA, session.ActiveSide);
    Assert.Equal(1, session.TurnNumber);
    Assert.False(GetValue(Query(session, new CanUnitActNow(unitA2.Handle))));

    AdvanceTurn(session);
    Assert.Equal(factionB, session.ActiveSide);
  }

  [TestCase(TestName = "Unit can throw a grenade in battle session")]
  public void UnitCanThrowAGrenadeInBattleSession()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Thrower", faction, actionPoints: 4), new Vector3I(1, 0, 1));
    var grenade = BattleTestFactory.MakeGrenade("Practice Grenade", throwRange: 4);
    unit.AddInventoryItem(grenade);

    Option<ItemThrownBattleEvent> thrownEvent = None;
    session.BattleEventCommitted += battleEvent =>
    {
      if (battleEvent is ItemThrownBattleEvent itemThrownEvent)
        thrownEvent = Some(itemThrownEvent);
    };

    StartBattle(session);
    var executor = new BattleActionExecutor(session);
    var threw = executor.Submit(BattleAction.ThrowItem(unit.Handle, grenade, new Vector3I(3, 0, 1))).RequireSingleResult();

    Assert.True(threw.Succeeded);
    Assert.False(unit.HasInventoryItem(grenade));
    Assert.Equal(3, unit.CurrentActionPoints);
    Assert.True(thrownEvent.IsSome);
    ItemThrownBattleEvent itemThrownEvent = thrownEvent.RequireSome();
    Assert.Equal(BattleEventType.ItemThrown, itemThrownEvent.Type);
    Assert.Equal(unit.UnitId, itemThrownEvent.UnitId);
    Assert.Equal(new Vector3I(3, 0, 1), itemThrownEvent.Position.Raw);
  }

  [TestCase(TestName = "AddUnit throws when a validated placement can no longer be applied")]
  public void AddUnitThrowsWhenValidatedPlacementCanNoLongerBeApplied()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var occupiedUnit = session.AddUnit(BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1)).Unit;

    Assert.Throws<InvalidOperationException>(() => session.AddUnit(BattleTestFactory.MakeCombatant("Bravo", faction), new Vector3I(1, 0, 1)));
    Assert.Equal(1, session.AliveUnits.Count);
    Assert.True(session.AliveUnits.Contains(occupiedUnit));
    Assert.False(session.AliveUnits.Any(unit => unit.Combatant.Name == "Bravo"));
  }

  [TestCase(TestName = "HandleUnitDeath throws when the unit is not tracked as alive")]
  public void HandleUnitDeathThrowsWhenUnitIsNotTrackedAsAlive()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var foreignSession = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unit = SpawnUnit(foreignSession, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));

    Assert.Throws<InvalidOperationException>(() => session.HandleUnitDeath(unit));
    Assert.False(session.DeadUnits.Contains(unit));
  }

  [TestCase(TestName = "RemoveAvailableUnit throws when the unit is already unavailable")]
  public void RemoveAvailableUnitThrowsWhenTheUnitIsAlreadyUnavailable()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
    StartBattle(session);

    session.RemoveAvailableUnit(unit.Handle);

    Assert.False(GetValue(Query(session, new IsUnitStillAvailableThisTurn(unit.Handle))));
    Assert.Throws<InvalidOperationException>(() => session.RemoveAvailableUnit(unit.Handle));
  }

  [TestCase(TestName = "EndUnitActivation advances the turn when no active units remain")]
  public void EndUnitActivationAdvancesTheTurnWhenNoActiveUnitsRemain()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [factionA, factionB]);
    var unitA = SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    StartBattle(session);

    session.EndUnitActivation(unitA);

    Assert.False(GetValue(Query(session, new IsUnitStillAvailableThisTurn(unitA.Handle))));
    Assert.Equal(factionB, session.ActiveSide);
    Assert.Equal(1, session.TurnNumber);
  }

  [TestCase(TestName = "EndFactionTurn throws when the expected side is not active")]
  public void EndFactionTurnThrowsWhenTheExpectedSideIsNotActive()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [factionA, factionB]);

    SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    StartBattle(session);

    Assert.Throws<InvalidOperationException>(() => session.EndFactionTurn(factionB));
    Assert.Equal(factionA, session.ActiveSide);
  }

  private static bool IsSome<T>(Option<T> option)
  {
    return option.Match(
      _ => true,
      () => false);
  }

  private static void AdvanceTurn(BattleSession session)
  {
    var activeSide = session.ActiveSide;
    var executor = new BattleActionExecutor(session);
    var result = executor.Submit(BattleAction.EndFactionTurn(activeSide)).RequireSingleResult();
    Assert.True(result.Succeeded);
  }

  private static void PassUnit(BattleSession session, BattleSession.BattleUnitHandle unitHandle)
  {
    var executor = new BattleActionExecutor(session);
    var result = executor.Submit(BattleAction.PassUnit(unitHandle)).RequireSingleResult();
    Assert.True(result.Succeeded);
  }

  private static void ApplyDamage(BattleSession session, BattleSession.BattleUnitHandle unitHandle, int amount)
  {
    var executor = new BattleActionExecutor(session);
    var result = executor.Submit(BattleAction.ApplyDamage(unitHandle, amount)).RequireSingleResult();
    Assert.True(result.Succeeded);
  }

  private static bool IsNone<T>(Option<T> option)
  {
    return option.Match(
      _ => false,
      () => true);
  }
}
