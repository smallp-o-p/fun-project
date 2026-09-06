using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Stats;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;

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
    var faction = TestData.MakeFaction("City Guard");
    using var battle = new BattleFixture(new Vector3I(4, 2, 4), [faction]);
    var session = battle.Session;
    var unit = battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));

    var point = session.Board.At(1, 0, 1);
    Assert.True(session.Board.IsOccupied(point));
    Assert.Equal(unit.Id, session.Board.GetOccupant(point).RequireSome());
  }

  [TestCase(TestName = "SpawnUnit rejects occupied tile")]
  public void SpawnUnitRejectsOccupiedTile()
  {
    var faction = TestData.MakeFaction("City Guard");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [faction]);

    battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));
    Assert.Throws<System.InvalidOperationException>(
      () => battle.Submit(BattleAction.SpawnUnit(TestData.MakeCombatant("Bravo", faction), battle.At(1, 0, 1))));
  }

  [TestCase(TestName = "StartBattle activates first participating faction")]
  public void StartBattleActivatesFirstParticipatingFaction()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [playerFaction, enemyFaction]);
    var session = battle.Session;

    battle.Spawn(TestData.MakeCombatant("Alpha", playerFaction), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("Bandit", enemyFaction), new Vector3I(1, 0, 0));
    battle.Start();

    Assert.Equal(playerFaction, session.ActiveSide);
    Assert.Equal(1, session.TurnNumber);
  }

  [TestCase(TestName = "Global faction order keeps each faction only once")]
  public void GlobalFactionOrderKeepsEachFactionOnlyOnce()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [factionA]);
    var session = battle.Session;

    battle.Spawn(TestData.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("A2", factionA), new Vector3I(1, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB), new Vector3I(2, 0, 0));

    Assert.Equal(2, session.GlobalFactionTurnOrder.Count);
    Assert.Equal(factionA, session.GlobalFactionTurnOrder.AsValueEnumerable().First());
    Assert.Equal(factionB, session.GlobalFactionTurnOrder.AsValueEnumerable().Last());
    Assert.Equal(2, session.TurnQueue.Count);
    Assert.Equal(factionA, session.TurnQueue.AsValueEnumerable().First());
    Assert.Equal(factionB, session.TurnQueue.AsValueEnumerable().Last());
  }

  [TestCase(TestName = "Constructor seeds global faction order")]
  public void ConstructorSeedsGlobalFactionOrder()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    var session = new BattleSession(
      new BattleBoardState(new Vector3I(4, 1, 4)),
      [factionB, factionA, factionB]);

    Assert.Equal(2, session.GlobalFactionTurnOrder.Count);
    Assert.Equal(factionB, session.GlobalFactionTurnOrder.AsValueEnumerable().First());
    Assert.Equal(factionA, session.GlobalFactionTurnOrder.AsValueEnumerable().Last());
    Assert.Equal(factionB, session.ActiveSide);
    Assert.Equal(factionB, session.TurnQueue.AsValueEnumerable().First());
    Assert.Equal(0, session.AliveUnits.AsValueEnumerable().Count());
    Assert.False(session.Board.IsOccupied(session.Board.ValidatePoint(new Vector3I(0, 0, 0)).RequireSome()));
  }

  [TestCase(TestName = "Constructor can use a prebuilt board state")]
  public void ConstructorCanUseAPrebuiltBoardState()
  {
    var faction = TestData.MakeFaction("A");
    var board = new BattleBoardState(new Vector3I(3, 1, 3));
    board.SetTileWalkable(board.ValidatePoint(new Vector3I(1, 0, 0)).RequireSome(), false);

    var session = new BattleSession(
      board,
      [faction]);

    var executor = ExecutorFor(session);
    Assert.Throws<System.InvalidOperationException>(
      () => executor.Submit(BattleAction.SpawnUnit(TestData.MakeCombatant("A1", faction), board.At(1, 0, 0))));

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
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [faction]);
    var weapon = new MeleeWeapon(new WeaponData
    {
      Frame = TestData.MakeFrame(),
      DamageStat = new DamageStat { BaseValue = 10 },
      CriticalChanceStat = new CriticalChanceStat { BaseValue = 5 },
      RangeStat = new RangeStat { BaseValue = 1 },
    });

    battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0), weapon);

    Assert.Equal(weapon, battle.Session.GetUnitAt(battle.At(0, 0, 0)).RequireSome().EquippedWeapon.RequireSome());
  }

  [TestCase(TestName = "AdvanceTurn rotates only participating factions without incrementing early")]
  public void AdvanceTurnRotatesOnlyParticipatingFactionsWithoutIncrementingEarly()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    var factionC = TestData.MakeFaction("C");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [factionA, factionB, factionC]);
    var session = battle.Session;

    battle.Spawn(TestData.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    battle.Spawn(TestData.MakeCombatant("C1", factionC), new Vector3I(2, 0, 0));
    battle.Start();

    Assert.Equal(factionA, session.ActiveSide);

    battle.AdvanceTurn();
    Assert.Equal(factionB, session.ActiveSide);
    Assert.Equal(1, session.TurnNumber);

    battle.AdvanceTurn();
    Assert.Equal(factionC, session.ActiveSide);
    Assert.Equal(1, session.TurnNumber);

    battle.AdvanceTurn();
    Assert.Equal(factionA, session.ActiveSide);
    Assert.Equal(2, session.TurnNumber);
  }

  [TestCase(TestName = "Passing the last available unit advances the global turn counter")]
  public void PassingTheLastAvailableUnitAdvancesTheGlobalTurnCounter()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [factionA, factionB]);
    var session = battle.Session;
    var unitA = battle.Spawn(TestData.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    var unitB = battle.Spawn(TestData.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    battle.Start();

    Assert.Equal(1, session.TurnNumber);
    Assert.Equal(factionA, session.ActiveSide);

    battle.Pass(unitA);
    Assert.Equal(1, session.TurnNumber);
    Assert.Equal(factionB, session.ActiveSide);

    battle.Pass(unitB);
    Assert.Equal(2, session.TurnNumber);
    Assert.Equal(factionA, session.ActiveSide);
  }

  [TestCase(TestName = "Eliminating an unacted faction does not block the global turn counter")]
  public void EliminatingAnUnactedFactionDoesNotBlockTheGlobalTurnCounter()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    var factionC = TestData.MakeFaction("C");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [factionA, factionB, factionC]);
    var session = battle.Session;

    battle.Spawn(TestData.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    var unitC = battle.Spawn(TestData.MakeCombatant("C1", factionC, health: 10), new Vector3I(2, 0, 0));
    battle.Start();

    battle.ApplyDamage(unitC, 10);
    Assert.False(session.TurnQueue.AsValueEnumerable().Contains(factionC));

    battle.AdvanceTurn();
    Assert.Equal(1, session.TurnNumber);
    Assert.Equal(factionB, session.ActiveSide);

    battle.AdvanceTurn();
    Assert.Equal(2, session.TurnNumber);
    Assert.Equal(factionA, session.ActiveSide);
  }

  [TestCase(TestName = "Adding a faction mid-round delays the global turn counter until it acts")]
  public void AddingAFactionMidRoundDelaysTheGlobalTurnCounterUntilItActs()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    var factionC = TestData.MakeFaction("C");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [factionA, factionB]);
    var session = battle.Session;

    battle.Spawn(TestData.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    battle.Start();

    battle.Spawn(TestData.MakeCombatant("C1", factionC), new Vector3I(2, 0, 0));

    Assert.Equal(1, session.TurnNumber);
    Assert.True(session.TurnQueue.AsValueEnumerable().Contains(factionC));

    battle.AdvanceTurn();
    Assert.Equal(1, session.TurnNumber);
    Assert.Equal(factionB, session.ActiveSide);

    battle.AdvanceTurn();
    Assert.Equal(1, session.TurnNumber);
    Assert.Equal(factionC, session.ActiveSide);

    battle.AdvanceTurn();
    Assert.Equal(2, session.TurnNumber);
    Assert.Equal(factionA, session.ActiveSide);
  }

  [TestCase(TestName = "Adding units to a faction that already acted waits until the next round")]
  public void AddingUnitsToAFactionThatAlreadyActedWaitsUntilTheNextRound()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [factionA, factionB]);
    var session = battle.Session;

    battle.Spawn(TestData.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    battle.Start();

    battle.AdvanceTurn();
    Assert.Equal(factionB, session.ActiveSide);
    Assert.Equal(1, session.TurnNumber);

    battle.Spawn(TestData.MakeCombatant("A2", factionA), new Vector3I(2, 0, 0));

    battle.AdvanceTurn();
    Assert.Equal(2, session.TurnNumber);
    Assert.Equal(factionA, session.ActiveSide);
  }

  [TestCase(TestName = "Adding a unit mid-battle reconciles the faction queue")]
  public void AddingAUnitMidBattleReconcilesTheFactionQueue()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    var factionC = TestData.MakeFaction("C");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [factionA, factionB]);
    var session = battle.Session;

    battle.Spawn(TestData.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    battle.Start();

    battle.Spawn(TestData.MakeCombatant("C1", factionC), new Vector3I(2, 0, 0));

    Assert.Equal(factionA, session.ActiveSide);
    Assert.True(session.TurnQueue.AsValueEnumerable().Contains(factionA));
    Assert.True(session.TurnQueue.AsValueEnumerable().Contains(factionB));
    Assert.True(session.TurnQueue.AsValueEnumerable().Contains(factionC));
  }

  [TestCase(TestName = "Move updates unit position occupancy and action points")]
  public void MoveUpdatesUnitPositionOccupancyAndActionPoints()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 2, 4), [faction]);
    var session = battle.Session;
    var unit = battle.Spawn(TestData.MakeCombatant("Alpha", faction, actionPoints: 5), new Vector3I(1, 0, 1));
    battle.Start();

    battle.Move(unit, [new Vector3I(1, 1, 1)], 2);

    Assert.Equal(new Vector3I(1, 1, 1), session.GetUnitPosition(unit).RequireSome().Raw);
    Assert.False(session.Board.IsOccupied(session.Board.At(1, 0, 1)));
    Assert.True(session.Board.IsOccupied(session.Board.At(1, 1, 1)));
    Assert.Equal(3, unit.CurrentActionPoints);
  }

  [TestCase(TestName = "MoveUnit follows a multi-step route and spends AP per step")]
  public void MoveUnitFollowsAMultiStepRouteAndSpendsAPPerStep()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [faction]);
    var session = battle.Session;
    var unit = battle.Spawn(TestData.MakeCombatant("Runner", faction, actionPoints: 5), new Vector3I(0, 0, 0));
    battle.Start();

    BattleBoardState.ValidatedPoint[] validatedPath = battle.Query(new FindPathForUnit(battle.Alive(unit), battle.At(2, 0, 0)));
    BattleBoardState.ValidatedPoint[] path = validatedPath.AsValueEnumerable().Skip(1).ToArray();
    Assert.Equal(3, validatedPath.Length);
    Assert.Equal(2, path.Length);

    BattleActionExecResult moveResult = battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), path));

    Assert.True(moveResult.Action is MoveUnit);
    Assert.Equal(new Vector3I(2, 0, 0), session.GetUnitPosition(unit).RequireSome().Raw);
    Assert.False(session.Board.IsOccupied(session.Board.At(0, 0, 0)));
    Assert.True(session.Board.IsOccupied(session.Board.At(2, 0, 0)));
    Assert.Equal(3, unit.CurrentActionPoints);
  }

  [TestCase(TestName = "Session-owned unit position follows movement")]
  public void SessionOwnedUnitPositionFollowsMovement()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [faction]);
    var session = battle.Session;
    var unit = battle.Spawn(TestData.MakeCombatant("Runner", faction, health: 10, actionPoints: 5), new Vector3I(0, 0, 0));
    battle.Start();

    Assert.Equal(new Vector3I(0, 0, 0), session.GetUnitPosition(unit).RequireSome().Raw);

    battle.Move(unit, [new Vector3I(1, 0, 0)]);

    Assert.Equal(new Vector3I(1, 0, 0), session.GetUnitPosition(unit).RequireSome().Raw);

    BattleBoardState.ValidatedPoint destination = session.Board.At(1, 0, 0);
    Assert.Equal(unit, session.GetUnitAt(destination).RequireSome());

    battle.ApplyDamage(unit, 10);
    Assert.True(session.GetUnitPosition(unit).IsNone);
    Assert.True(session.GetUnitAt(destination).IsNone);

    var replacement = battle.Spawn(TestData.MakeCombatant("Replacement", faction, health: 10), new Vector3I(1, 0, 0));
    Assert.Equal(replacement, session.GetUnitAt(destination).RequireSome());
    Assert.Equal(new Vector3I(1, 0, 0), session.GetUnitPosition(replacement).RequireSome().Raw);
  }

  [TestCase(TestName = "MoveUnit rejects invalid composed steps")]
  public void MoveUnitRejectsInvalidComposedSteps()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [faction]);
    var session = battle.Session;
    var unit = battle.Spawn(TestData.MakeCombatant("Runner", faction, actionPoints: 5), new Vector3I(0, 0, 0));
    battle.Start();

    var action = BattleAction.MoveUnit(
      battle.Alive(unit),
      [battle.At(1, 0, 0), battle.At(3, 0, 0)]);

    Assert.Throws<System.InvalidOperationException>(() => battle.Submit(action));
    Assert.Equal(new Vector3I(0, 0, 0), session.GetUnitPosition(unit).RequireSome().Raw);
    Assert.Equal(5, unit.CurrentActionPoints);
    Assert.True(session.Board.IsOccupied(session.Board.At(0, 0, 0)));
  }

  [TestCase(TestName = "MoveUnit rejects routes that cost more AP than the unit has")]
  public void MoveUnitRejectsRoutesThatCostMoreApThanTheUnitHas()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [faction]);
    var session = battle.Session;
    var unit = battle.Spawn(TestData.MakeCombatant("Runner", faction, actionPoints: 1), new Vector3I(0, 0, 0));
    battle.Start();

    BattleBoardState.ValidatedPoint[] validatedPath = battle.Query(new FindPathForUnit(battle.Alive(unit), battle.At(2, 0, 0)));
    BattleBoardState.ValidatedPoint[] path = validatedPath.AsValueEnumerable().Skip(1).ToArray();
    Assert.Equal(3, validatedPath.Length);
    Assert.Equal(2, path.Length);

    var action = BattleAction.MoveUnit(battle.Alive(unit), path);

    Assert.Throws<System.InvalidOperationException>(() => battle.Submit(action));
    Assert.Equal(new Vector3I(0, 0, 0), session.GetUnitPosition(unit).RequireSome().Raw);
    Assert.Equal(1, unit.CurrentActionPoints);
    Assert.True(session.Board.IsOccupied(session.Board.At(0, 0, 0)));
  }

  [TestCase(TestName = "Passing a unit ends its activation while keeping the next ally available")]
  public void PassingAUnitEndsItsActivationWhileKeepingTheNextAllyAvailable()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [faction]);
    var session = battle.Session;
    var unitA = battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
    var unitB = battle.Spawn(TestData.MakeCombatant("Bravo", faction), new Vector3I(1, 0, 0));
    battle.Start();

    battle.Pass(unitA);

    Assert.False(battle.Query(new IsUnitStillAvailableThisTurn(unitA)));
    Assert.False(battle.Query(new CanUnitActNow(unitA)));
    Assert.True(battle.Query(new IsUnitStillAvailableThisTurn(unitB)));
    Assert.Equal(faction, session.ActiveSide);
  }

  [TestCase(TestName = "Killing the only active unit on an eliminated faction does not auto-advance")]
  public void KillingTheOnlyActiveUnitOnAnEliminatedFactionDoesNotAutoAdvance()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [factionA, factionB]);
    var session = battle.Session;
    var unitA = battle.Spawn(TestData.MakeCombatant("A1", factionA, health: 10), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB, health: 10), new Vector3I(1, 0, 0));
    battle.Start();

    Assert.Equal(factionA, session.ActiveSide);

    battle.ApplyDamage(unitA, 10);

    Assert.False(session.Board.IsOccupied(session.Board.At(0, 0, 0)));
    Assert.Equal(factionA, session.ActiveSide);
    Assert.False(session.AliveUnits.AsValueEnumerable().Contains(unitA));
    Assert.True(session.DeadUnits.AsValueEnumerable().Contains(unitA));
    Assert.False(battle.Query(new GetFactionAliveUnits(factionA)).AsValueEnumerable().Select(u => u.State).Contains(unitA));
    Assert.True(battle.Query(new GetFactionDeadUnits(factionA)).AsValueEnumerable().Select(d => d.State).Contains(unitA));
    Assert.True(session.TurnQueue.AsValueEnumerable().Contains(factionA));
    Assert.True(session.TurnQueue.AsValueEnumerable().Contains(factionB));

    battle.AdvanceTurn();
    Assert.Equal(factionB, session.ActiveSide);
  }

  [TestCase(TestName = "Killing an active unit leaves another surviving ally actable")]
  public void KillingAnActiveUnitLeavesAnotherSurvivingAllyActable()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [factionA, factionB]);
    var session = battle.Session;
    var unitA1 = battle.Spawn(TestData.MakeCombatant("A1", factionA, health: 10), new Vector3I(0, 0, 0));
    var unitA2 = battle.Spawn(TestData.MakeCombatant("A2", factionA, health: 10), new Vector3I(1, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB, health: 10), new Vector3I(2, 0, 0));
    battle.Start();

    battle.ApplyDamage(unitA1, 10);

    Assert.Equal(factionA, session.ActiveSide);
    Assert.True(battle.Query(new CanUnitActNow(unitA2)));
    Assert.False(session.Board.IsOccupied(session.Board.At(0, 0, 0)));
    Assert.True(session.DeadUnits.AsValueEnumerable().Contains(unitA1));
  }

  [TestCase(TestName = "Unit can throw a grenade in battle session")]
  public void UnitCanThrowAGrenadeInBattleSession()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [faction]);
    var unit = battle.Spawn(TestData.MakeCombatant("Thrower", faction, actionPoints: 4), new Vector3I(1, 0, 1));
    var grenade = TestData.MakeGrenade("Practice Grenade", throwRange: 4);
    unit.AddInventoryItem(grenade.Item);

    battle.Start();
    battle.Throw(unit, grenade, new Vector3I(3, 0, 1));

    Assert.False(unit.HasInventoryItem(grenade.Item));
    Assert.Equal(3, unit.CurrentActionPoints);
    ItemThrownBattleEvent itemThrownEvent = battle.Events.SingleEvent<ItemThrownBattleEvent>();
    Assert.Equal(unit.Id, itemThrownEvent.Unit.Id);
    Assert.Equal(new Vector3I(3, 0, 1), itemThrownEvent.Position.Raw);
  }

  [TestCase(TestName = "AddUnit throws when a validated placement can no longer be applied")]
  public void AddUnitThrowsWhenValidatedPlacementCanNoLongerBeApplied()
  {
    var faction = TestData.MakeFaction("Player");
    var session = new BattleSession(new BattleBoardState(new Vector3I(4, 1, 4)), [faction]);
    BattleBoardState.ValidatedPoint occupiedPoint = session.Board.At(1, 0, 1);
    var occupiedUnit = session.AddUnit(TestData.MakeCombatant("Alpha", faction), occupiedPoint, None, None).Unit;

    Assert.Throws<InvalidOperationException>(() =>
      session.AddUnit(TestData.MakeCombatant("Bravo", faction), occupiedPoint, None, None));
    Assert.Equal(1, session.AliveUnits.AsValueEnumerable().Count());
    Assert.True(session.AliveUnits.AsValueEnumerable().Contains(occupiedUnit));
    Assert.False(session.AliveUnits.AsValueEnumerable().Any(unit => unit.Combatant.Name == "Bravo"));
  }

  [TestCase(TestName = "EndUnitActivation throws when the unit is already unavailable")]
  public void EndUnitActivationThrowsWhenTheUnitIsAlreadyUnavailable()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [faction]);
    var session = battle.Session;
    var unit = battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("Bravo", faction), new Vector3I(1, 0, 0));
    battle.Start();

    session.EndUnitActivation(unit);

    Assert.False(battle.Query(new IsUnitStillAvailableThisTurn(unit)));
    Assert.Throws<InvalidOperationException>(() => session.EndUnitActivation(unit));
  }

  [TestCase(TestName = "EndUnitActivation advances the turn when no active units remain")]
  public void EndUnitActivationAdvancesTheTurnWhenNoActiveUnitsRemain()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [factionA, factionB]);
    var session = battle.Session;
    var unitA = battle.Spawn(TestData.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    battle.Start();

    session.EndUnitActivation(unitA);

    Assert.False(battle.Query(new IsUnitStillAvailableThisTurn(unitA)));
    Assert.Equal(factionB, session.ActiveSide);
    Assert.Equal(1, session.TurnNumber);
  }

  [TestCase(TestName = "EndFactionTurn throws when the expected side is not active")]
  public void EndFactionTurnThrowsWhenTheExpectedSideIsNotActive()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [factionA, factionB]);
    var session = battle.Session;

    battle.Spawn(TestData.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    battle.Start();

    Assert.Throws<InvalidOperationException>(() => session.EndFactionTurn(factionB));
    Assert.Equal(factionA, session.ActiveSide);
  }

  [TestCase(TestName = "RollPercent with the same seed produces the same in-range sequence")]
  public void RollPercentWithTheSameSeedProducesTheSameInRangeSequence()
  {
    var faction = TestData.MakeFaction("Player");
    var first = new BattleSession(new BattleBoardState(new Vector3I(2, 1, 2)), [faction], randomSeed: 1234);
    var second = new BattleSession(new BattleBoardState(new Vector3I(2, 1, 2)), [faction], randomSeed: 1234);

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
    var faction = TestData.MakeFaction("Player");
    var session = new BattleSession(new BattleBoardState(new Vector3I(2, 1, 2)), [faction]);

    Assert.True(session.HitChanceCalculator is StandardHitChanceCalculator);
  }

}
