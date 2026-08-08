using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Stats;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

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

    var point = session.Board.At(1, 0, 1);
    Assert.True(session.Board.IsOccupied(point));
    Assert.Equal(unit.UnitId, session.Board.GetOccupant(point).RequireSome());
  }

  [TestCase(TestName = "SpawnUnit rejects occupied tile")]
  public void SpawnUnitRejectsOccupiedTile()
  {
    var faction = BattleTestFactory.MakeFaction("City Guard");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);

    SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));
    var executor = ExecutorFor(session);
    Assert.Throws<System.InvalidOperationException>(
      () => executor.Submit(BattleAction.SpawnUnit(BattleTestFactory.MakeCombatant("Bravo", faction), session.Board.At(1, 0, 1))));
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

  [TestCase(TestName = "Constructor seeds global faction order")]
  public void ConstructorSeedsGlobalFactionOrder()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var session = new BattleSession(
      new BattleBoardState(new Vector3I(4, 1, 4)),
      [factionB, factionA, factionB]);

    Assert.Equal(2, session.GlobalFactionTurnOrder.Count);
    Assert.Equal(factionB, session.GlobalFactionTurnOrder.First());
    Assert.Equal(factionA, session.GlobalFactionTurnOrder.Last());
    Assert.Equal(factionB, session.ActiveSide);
    Assert.Equal(factionB, session.TurnQueue.First());
    Assert.Equal(0, session.AliveUnits.Count());
    Assert.False(session.Board.IsOccupied(session.Board.ValidatePoint(new Vector3I(0, 0, 0)).RequireSome()));
  }

  [TestCase(TestName = "Constructor can use a prebuilt board state")]
  public void ConstructorCanUseAPrebuiltBoardState()
  {
    var faction = BattleTestFactory.MakeFaction("A");
    var board = new BattleBoardState(new Vector3I(3, 1, 3));
    board.SetTileWalkable(board.ValidatePoint(new Vector3I(1, 0, 0)).RequireSome(), false);

    var session = new BattleSession(
      board,
      [faction]);

    var executor = ExecutorFor(session);
    Assert.Throws<System.InvalidOperationException>(
      () => executor.Submit(BattleAction.SpawnUnit(BattleTestFactory.MakeCombatant("A1", faction), board.At(1, 0, 0))));

    Assert.True(ReferenceEquals(board, session.Board));
  }

  [TestCase(TestName = "Constructor rejects sessions without factions")]
  public void ConstructorRejectsSessionsWithoutFactions()
  {
    var board = new BattleBoardState(new Vector3I(3, 1, 3));

    Assert.Throws<ArgumentException>(() => new BattleSession(
      board,
      []));
  }

  [TestCase(TestName = "SpawnUnit accepts equipped weapon option")]
  public void SpawnUnitAcceptsEquippedWeaponOption()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var weapon = new MeleeWeapon(new WeaponData
    {
      Frame = BattleTestFactory.MakeFrame(),
      DamageStat = new DamageStat { BaseValue = 10 },
      CriticalChanceStat = new CriticalChanceStat { BaseValue = 5 },
      RangeStat = new RangeStat { BaseValue = 1 },
    });

    var executor = ExecutorFor(session);
    executor
      .Submit(BattleAction.SpawnUnit(
        BattleTestFactory.MakeCombatant("Alpha", faction),
        session.Board.At(0, 0, 0),
        weapon))
      ;

    Assert.Equal(weapon, session.GetUnitAt(session.Board.At(0, 0, 0)).RequireSome().EquippedWeapon.RequireSome());
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

    PassUnit(session, unitA.State);
    Assert.Equal(1, session.TurnNumber);
    Assert.Equal(factionB, session.ActiveSide);

    PassUnit(session, unitB.State);
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

    ApplyDamage(session, unitC.State, 10);
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

    var executor = ExecutorFor(session);
    var moved = executor.Submit(BattleAction.MoveUnit(unit.AliveIn(session), [session.Board.At(1, 1, 1)], 2));

    Assert.Equal(new Vector3I(1, 1, 1), session.GetUnitPosition(unit.State).RequireSome().Raw);
    Assert.False(session.Board.IsOccupied(session.Board.At(1, 0, 1)));
    Assert.True(session.Board.IsOccupied(session.Board.At(1, 1, 1)));
    Assert.Equal(3, unit.CurrentActionPoints);
  }

  [TestCase(TestName = "MoveUnit follows a multi-step route and spends AP per step")]
  public void MoveUnitFollowsAMultiStepRouteAndSpendsAPPerStep()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Runner", faction, actionPoints: 5), new Vector3I(0, 0, 0));
    StartBattle(session);

    BattleBoardState.ValidatedPoint[] validatedPath = Query(session, new FindPathForUnit(unit.AliveIn(session), session.Board.At(2, 0, 0)));
    BattleBoardState.ValidatedPoint[] path = validatedPath.Skip(1).ToArray();
    Assert.Equal(3, validatedPath.Length);
    Assert.Equal(2, path.Length);

    var executor = ExecutorFor(session);
    BattleActionExecResult moveResults = executor.Submit(BattleAction.MoveUnit(unit.AliveIn(session), path));

    BattleActionExecResult moveResult = moveResults;
    Assert.True(moveResult.Action is MoveUnit);
    Assert.Equal(new Vector3I(2, 0, 0), session.GetUnitPosition(unit.State).RequireSome().Raw);
    Assert.False(session.Board.IsOccupied(session.Board.At(0, 0, 0)));
    Assert.True(session.Board.IsOccupied(session.Board.At(2, 0, 0)));
    Assert.Equal(3, unit.CurrentActionPoints);
  }

  [TestCase(TestName = "Session-owned unit position follows movement")]
  public void SessionOwnedUnitPositionFollowsMovement()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Runner", faction, health: 10, actionPoints: 5), new Vector3I(0, 0, 0));
    StartBattle(session);

    Assert.Equal(new Vector3I(0, 0, 0), session.GetUnitPosition(unit.State).RequireSome().Raw);

    var executor = ExecutorFor(session);
    var moved = executor.Submit(BattleAction.MoveUnit(unit.AliveIn(session), [session.Board.At(1, 0, 0)]));

    Assert.Equal(new Vector3I(1, 0, 0), session.GetUnitPosition(unit.State).RequireSome().Raw);

    BattleBoardState.ValidatedPoint destination = session.Board.At(1, 0, 0);
    Assert.Equal(unit.State, session.GetUnitAt(destination).RequireSome());

    ApplyDamage(session, unit.State, 10);
    Assert.True(session.GetUnitPosition(unit.State).IsNone);
    Assert.True(session.GetUnitAt(destination).IsNone);

    var replacement = SpawnUnit(session, BattleTestFactory.MakeCombatant("Replacement", faction, health: 10), new Vector3I(1, 0, 0));
    Assert.Equal(replacement.State, session.GetUnitAt(destination).RequireSome());
    Assert.Equal(new Vector3I(1, 0, 0), session.GetUnitPosition(replacement.State).RequireSome().Raw);
  }

  [TestCase(TestName = "MoveUnit rejects invalid composed steps")]
  public void MoveUnitRejectsInvalidComposedSteps()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Runner", faction, actionPoints: 5), new Vector3I(0, 0, 0));
    StartBattle(session);

    var action = BattleAction.MoveUnit(
      unit.AliveIn(session),
      [session.Board.At(1, 0, 0), session.Board.At(3, 0, 0)]);
    var executor = ExecutorFor(session);

    Assert.Throws<System.InvalidOperationException>(() => executor.Submit(action));
    Assert.Equal(new Vector3I(0, 0, 0), session.GetUnitPosition(unit.State).RequireSome().Raw);
    Assert.Equal(5, unit.CurrentActionPoints);
    Assert.True(session.Board.IsOccupied(session.Board.At(0, 0, 0)));
  }

  [TestCase(TestName = "MoveUnit rejects routes that cost more AP than the unit has")]
  public void MoveUnitRejectsRoutesThatCostMoreApThanTheUnitHas()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Runner", faction, actionPoints: 1), new Vector3I(0, 0, 0));
    StartBattle(session);

    BattleBoardState.ValidatedPoint[] validatedPath = Query(session, new FindPathForUnit(unit.AliveIn(session), session.Board.At(2, 0, 0)));
    BattleBoardState.ValidatedPoint[] path = validatedPath.Skip(1).ToArray();
    Assert.Equal(3, validatedPath.Length);
    Assert.Equal(2, path.Length);

    var action = BattleAction.MoveUnit(unit.AliveIn(session), path);
    var executor = ExecutorFor(session);

    Assert.Throws<System.InvalidOperationException>(() => executor.Submit(action));
    Assert.Equal(new Vector3I(0, 0, 0), session.GetUnitPosition(unit.State).RequireSome().Raw);
    Assert.Equal(1, unit.CurrentActionPoints);
    Assert.True(session.Board.IsOccupied(session.Board.At(0, 0, 0)));
  }

  [TestCase(TestName = "Passing a unit ends its activation while keeping the next ally available")]
  public void PassingAUnitEndsItsActivationWhileKeepingTheNextAllyAvailable()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unitA = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
    var unitB = SpawnUnit(session, BattleTestFactory.MakeCombatant("Bravo", faction), new Vector3I(1, 0, 0));
    StartBattle(session);

    PassUnit(session, unitA.State);

    Assert.False(Query(session, new IsUnitStillAvailableThisTurn(unitA)));
    Assert.False(Query(session, new CanUnitActNow(unitA)));
    Assert.True(Query(session, new IsUnitStillAvailableThisTurn(unitB)));
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

    ApplyDamage(session, unitA.State, 10);

    Assert.False(session.Board.IsOccupied(session.Board.At(0, 0, 0)));
    Assert.Equal(factionA, session.ActiveSide);
    Assert.False(session.AliveUnits.Contains(unitA.State));
    Assert.True(session.DeadUnits.Contains(unitA.State));
    Assert.False(Query(session, new GetFactionAliveUnits(factionA)).Select(u => u.State).Contains(unitA));
    Assert.True(Query(session, new GetFactionDeadUnits(factionA)).Select(d => d.State).Contains(unitA));
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

    ApplyDamage(session, unitA1.State, 10);

    Assert.Equal(factionA, session.ActiveSide);
    Assert.True(Query(session, new CanUnitActNow(unitA2)));
    Assert.False(session.Board.IsOccupied(session.Board.At(0, 0, 0)));
    Assert.True(session.DeadUnits.Contains(unitA1.State));
  }

  [TestCase(TestName = "Unit can throw a grenade in battle session")]
  public void UnitCanThrowAGrenadeInBattleSession()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Thrower", faction, actionPoints: 4), new Vector3I(1, 0, 1));
    var grenade = BattleTestFactory.MakeGrenade("Practice Grenade", throwRange: 4);
    unit.AddInventoryItem(grenade.Item);

    Option<ItemThrownBattleEvent> thrownEvent = None;
    session.BattleEventCommitted += battleEvent =>
    {
      if (battleEvent is ItemThrownBattleEvent itemThrownEvent)
        thrownEvent = Some(itemThrownEvent);
    };

    StartBattle(session);
    var executor = ExecutorFor(session);
    executor.Submit(BattleAction.ThrowItem(unit.AliveIn(session), grenade, session.Board.At(3, 0, 1)));

    Assert.False(unit.HasInventoryItem(grenade.Item));
    Assert.Equal(3, unit.CurrentActionPoints);
    Assert.True(thrownEvent.IsSome);
    ItemThrownBattleEvent itemThrownEvent = thrownEvent.RequireSome();
    Assert.Equal(unit.UnitId, itemThrownEvent.Unit.Id);
    Assert.Equal(new Vector3I(3, 0, 1), itemThrownEvent.Position.Raw);
  }

  [TestCase(TestName = "AddUnit throws when a validated placement can no longer be applied")]
  public void AddUnitThrowsWhenValidatedPlacementCanNoLongerBeApplied()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    BattleBoardState.ValidatedPoint occupiedPoint = session.Board.At(1, 0, 1);
    var occupiedUnit = session.AddUnit(BattleTestFactory.MakeCombatant("Alpha", faction), occupiedPoint, None, None).Unit;

    Assert.Throws<InvalidOperationException>(() =>
      session.AddUnit(BattleTestFactory.MakeCombatant("Bravo", faction), occupiedPoint, None, None));
    Assert.Equal(1, session.AliveUnits.Count());
    Assert.True(session.AliveUnits.Contains(occupiedUnit));
    Assert.False(session.AliveUnits.Any(unit => unit.Combatant.Name == "Bravo"));
  }

  [TestCase(TestName = "EndUnitActivation throws when the unit is already unavailable")]
  public void EndUnitActivationThrowsWhenTheUnitIsAlreadyUnavailable()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Bravo", faction), new Vector3I(1, 0, 0));
    StartBattle(session);

    session.EndUnitActivation(unit.State);

    Assert.False(Query(session, new IsUnitStillAvailableThisTurn(unit)));
    Assert.Throws<InvalidOperationException>(() => session.EndUnitActivation(unit.State));
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

    Assert.False(Query(session, new IsUnitStillAvailableThisTurn(unitA)));
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

  [TestCase(TestName = "RollPercent with the same seed produces the same in-range sequence")]
  public void RollPercentWithTheSameSeedProducesTheSameInRangeSequence()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var first = BattleTestFactory.MakeSession(new Vector3I(2, 1, 2), [faction], randomSeed: 1234);
    var second = BattleTestFactory.MakeSession(new Vector3I(2, 1, 2), [faction], randomSeed: 1234);

    for (int i = 0; i < 20; i++)
    {
      int roll = first.RollPercent();
      Assert.True(roll >= 0);
      Assert.True(roll < 100);
      Assert.Equal(roll, second.RollPercent());
    }
  }

  [TestCase(TestName = "Session defaults to the standard hit chance calculator")]
  public void SessionDefaultsToTheStandardHitChanceCalculator()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(2, 1, 2), [faction]);

    Assert.True(session.HitChanceCalculator is StandardHitChanceCalculator);
  }

}
