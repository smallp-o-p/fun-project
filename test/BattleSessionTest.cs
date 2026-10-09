using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Stats;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class BattleSessionTest
{
  // Factory-ready multi-side fixture: domain tuples converted to placements, then built
  // through the shared Started scenario (factions grouped by first appearance, one inert
  // objective per faction).
  private static BattleFixture FixtureFromSides(Vector3I dimensions,
    params (Faction Faction, string UnitName, Vector3I Position, int Health)[] sides) =>
    BattleFixture.Started(dimensions,
      [.. sides.AsValueEnumerable().Select(side => new UnitPlacement(
        new UnitLoadout(TestData.MakeCombatant(side.UnitName, side.Faction, health: side.Health)),
        side.Position))]);

  [TestCase(TestName = "SpawnUnit occupies its tile")]
  public static void SpawnUnitOccupiesItsTile()
  {
    var faction = TestData.MakeFaction("City Guard");
    using var battle = FixtureFromSides(new Vector3I(4, 2, 4), (faction, "Seed", new Vector3I(0, 0, 0), 20));
    var session = battle.Session;
    var unit = battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));

    var point = session.Board.At(1, 0, 1);
    Assert.True(session.Board.IsOccupied(point));
    Assert.Equal(unit.Id, session.Board.GetOccupant(point).RequireSome());
    // An active-side reinforcement is immediately available this turn.
    Assert.True(battle.Query(new IsUnitStillAvailableThisTurn(unit)));
  }

  [TestCase(TestName = "SpawnUnit rejects occupied tile")]
  public static void SpawnUnitRejectsOccupiedTile()
  {
    var faction = TestData.MakeFaction("City Guard");
    using var battle = FixtureFromSides(new Vector3I(4, 1, 4), (faction, "Alpha", new Vector3I(1, 0, 1), 20));

    Assert.Throws<System.InvalidOperationException>(
      () => battle.Submit(BattleAction.SpawnUnit(TestData.MakeCombatant("Bravo", faction), battle.At(1, 0, 1))));
  }

  [TestCase(TestName = "Global faction order keeps each faction only once")]
  public static void GlobalFactionOrderKeepsEachFactionOnlyOnce()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = FixtureFromSides(new Vector3I(4, 1, 4), (factionA, "A1", new Vector3I(0, 0, 0), 20));
    var session = battle.Session;

    battle.Spawn(TestData.MakeCombatant("A2", factionA), new Vector3I(1, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB), new Vector3I(2, 0, 0));

    Assert.Equal(2, session.GlobalFactionTurnOrder.Count);
    Assert.Equal(factionA, session.GlobalFactionTurnOrder.AsValueEnumerable().First());
    Assert.Equal(factionB, session.GlobalFactionTurnOrder.AsValueEnumerable().Last());
    Assert.Equal(2, session.TurnQueue.Count);
    Assert.Equal(factionA, session.TurnQueue.AsValueEnumerable().First());
    Assert.Equal(factionB, session.TurnQueue.AsValueEnumerable().Last());
  }

  [TestCase(TestName = "Constructor seeds global faction order from the materialized roster")]
  public static void ConstructorSeedsGlobalFactionOrder()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    var board = new BattleBoardState(new Vector3I(4, 1, 4));
    var b1 = TestData.PlacedUnit(board, 0, TestData.MakeCombatant("B1", factionB), new Vector3I(0, 0, 0));
    var a1 = TestData.PlacedUnit(board, 1, TestData.MakeCombatant("A1", factionA), new Vector3I(1, 0, 0));
    var session = new BattleSession(board, [factionB, factionA, factionB], [b1, a1], [], []);

    Assert.Equal(2, session.GlobalFactionTurnOrder.Count);
    Assert.Equal(factionB, session.GlobalFactionTurnOrder.AsValueEnumerable().First());
    Assert.Equal(factionA, session.GlobalFactionTurnOrder.AsValueEnumerable().Last());
    Assert.Equal(factionB, session.ActiveSide);
    Assert.Equal(factionB, session.TurnQueue.AsValueEnumerable().First());
    Assert.Equal(2, session.AliveUnits.AsValueEnumerable().Count());
    Assert.True(session.Board.IsOccupied(session.Board.ValidatePoint(new Vector3I(0, 0, 0)).RequireSome()));
  }

  [TestCase(TestName = "Constructor can use a prebuilt board state")]
  public static void ConstructorCanUseAPrebuiltBoardState()
  {
    var faction = TestData.MakeFaction("A");
    var board = new BattleBoardState(new Vector3I(3, 1, 3));
    board.SetTileWalkable(board.ValidatePoint(new Vector3I(1, 0, 0)).RequireSome(), false);
    var unit = TestData.PlacedUnit(board, 0, TestData.MakeCombatant("A1", faction), new Vector3I(0, 0, 0));

    var session = new BattleSession(board, [faction], [unit], [], []);

    using var executor = new BattleActionExecutor(session);
    Assert.Throws<System.InvalidOperationException>(
      () => executor.Submit(BattleAction.SpawnUnit(TestData.MakeCombatant("A2", faction), board.At(1, 0, 0))));

    Assert.True(ReferenceEquals(board, session.Board));
  }

  [TestCase(TestName = "Constructor rejects sessions without factions")]
  public static void ConstructorRejectsSessionsWithoutFactions()
  {
    var board = new BattleBoardState(new Vector3I(3, 1, 3));

    Assert.Throws<ArgumentException>(() => _ = new BattleSession(
      board,
      [], [], [], []));
  }

  [TestCase(TestName = "SpawnUnit accepts equipped weapon option")]
  public static void SpawnUnitAcceptsEquippedWeaponOption()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = FixtureFromSides(new Vector3I(4, 1, 4), (faction, "Seed", new Vector3I(3, 0, 3), 20));
    var weapon = new MeleeWeapon(TestData.MakeWeaponData(damage: 10, critChance: 5, range: 1));

    battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0), weapon);

    Assert.Equal(weapon, battle.Session.GetUnitAt(battle.At(0, 0, 0)).RequireSome().EquippedWeapon.RequireSome());
  }

  [TestCase(TestName = "AdvanceTurn rotates only participating factions without incrementing early")]
  public static void AdvanceTurnRotatesOnlyParticipatingFactionsWithoutIncrementingEarly()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    var factionC = TestData.MakeFaction("C");
    using var battle = FixtureFromSides(new Vector3I(5, 1, 5),
      (factionA, "A1", new Vector3I(0, 0, 0), 20),
      (factionB, "B1", new Vector3I(1, 0, 0), 20),
      (factionC, "C1", new Vector3I(2, 0, 0), 20));
    var session = battle.Session;

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
  public static void PassingTheLastAvailableUnitAdvancesTheGlobalTurnCounter()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = FixtureFromSides(new Vector3I(4, 1, 4),
      (factionA, "A1", new Vector3I(0, 0, 0), 20),
      (factionB, "B1", new Vector3I(1, 0, 0), 20));
    var session = battle.Session;
    var unitA = battle.UnitAt(new Vector3I(0, 0, 0));
    var unitB = battle.UnitAt(new Vector3I(1, 0, 0));

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
  public static void EliminatingAnUnactedFactionDoesNotBlockTheGlobalTurnCounter()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    var factionC = TestData.MakeFaction("C");
    using var battle = FixtureFromSides(new Vector3I(5, 1, 5),
      (factionA, "A1", new Vector3I(0, 0, 0), 20),
      (factionB, "B1", new Vector3I(1, 0, 0), 20),
      (factionC, "C1", new Vector3I(2, 0, 0), 10));
    var session = battle.Session;
    var unitC = battle.UnitAt(new Vector3I(2, 0, 0));

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
  public static void AddingAFactionMidRoundDelaysTheGlobalTurnCounterUntilItActs()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    var factionC = TestData.MakeFaction("C");
    using var battle = FixtureFromSides(new Vector3I(5, 1, 5),
      (factionA, "A1", new Vector3I(0, 0, 0), 20),
      (factionB, "B1", new Vector3I(1, 0, 0), 20));
    var session = battle.Session;

    battle.Spawn(TestData.MakeCombatant("C1", factionC), new Vector3I(2, 0, 0));

    Assert.Equal(factionA, session.ActiveSide);
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
  public static void AddingUnitsToAFactionThatAlreadyActedWaitsUntilTheNextRound()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = FixtureFromSides(new Vector3I(5, 1, 5),
      (factionA, "A1", new Vector3I(0, 0, 0), 20),
      (factionB, "B1", new Vector3I(1, 0, 0), 20));
    var session = battle.Session;

    battle.AdvanceTurn();
    Assert.Equal(factionB, session.ActiveSide);
    Assert.Equal(1, session.TurnNumber);

    battle.Spawn(TestData.MakeCombatant("A2", factionA), new Vector3I(2, 0, 0));

    battle.AdvanceTurn();
    Assert.Equal(2, session.TurnNumber);
    Assert.Equal(factionA, session.ActiveSide);
  }

  [TestCase(TestName = "Move updates unit position occupancy and action points")]
  public static void MoveUpdatesUnitPositionOccupancyAndActionPoints()
  {
    using var battle = BattleFixture.Solo(new Vector3I(4, 2, 4), new Vector3I(1, 0, 1), actionPoints: 5);
    var session = battle.Session;
    var unit = battle.Unit;

    battle.Move(unit, [new Vector3I(1, 1, 1)], 2);

    Assert.Equal(new Vector3I(1, 1, 1), session.GetUnitPosition(unit).RequireSome().Raw);
    Assert.False(session.Board.IsOccupied(session.Board.At(1, 0, 1)));
    Assert.True(session.Board.IsOccupied(session.Board.At(1, 1, 1)));
    Assert.Equal(3, unit.CurrentActionPoints);
  }

  [TestCase(TestName = "MoveUnit follows a multi-step route and spends AP per step")]
  public static void MoveUnitFollowsAMultiStepRouteAndSpendsAPPerStep()
  {
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), new Vector3I(0, 0, 0), actionPoints: 5);
    var session = battle.Session;
    var unit = battle.Unit;

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
  public static void SessionOwnedUnitPositionFollowsMovement()
  {
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), new Vector3I(0, 0, 0), health: 10, actionPoints: 5);
    var session = battle.Session;
    var unit = battle.Unit;

    Assert.Equal(new Vector3I(0, 0, 0), session.GetUnitPosition(unit).RequireSome().Raw);

    battle.Move(unit, [new Vector3I(1, 0, 0)]);

    Assert.Equal(new Vector3I(1, 0, 0), session.GetUnitPosition(unit).RequireSome().Raw);

    BattleBoardState.ValidatedPoint destination = session.Board.At(1, 0, 0);
    Assert.Equal(unit, session.GetUnitAt(destination).RequireSome());

    battle.ApplyDamage(unit, 10);
    Assert.True(session.GetUnitPosition(unit).IsNone);
    Assert.True(session.GetUnitAt(destination).IsNone);

    var replacement = battle.Spawn(TestData.MakeCombatant("Replacement", battle.PlayerFaction, health: 10), new Vector3I(1, 0, 0));
    Assert.Equal(replacement, session.GetUnitAt(destination).RequireSome());
    Assert.Equal(new Vector3I(1, 0, 0), session.GetUnitPosition(replacement).RequireSome().Raw);
  }

  [TestCase(TestName = "MoveUnit rejects routes that cost more AP than the unit has")]
  public static void MoveUnitRejectsRoutesThatCostMoreApThanTheUnitHas()
  {
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), new Vector3I(0, 0, 0), actionPoints: 1);
    var session = battle.Session;
    var unit = battle.Unit;

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
  public static void PassingAUnitEndsItsActivationWhileKeepingTheNextAllyAvailable()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = FixtureFromSides(new Vector3I(4, 1, 4),
      (faction, "Alpha", new Vector3I(0, 0, 0), 20),
      (faction, "Bravo", new Vector3I(1, 0, 0), 20));
    var session = battle.Session;
    var unitA = battle.UnitAt(new Vector3I(0, 0, 0));
    var unitB = battle.UnitAt(new Vector3I(1, 0, 0));

    battle.Pass(unitA);

    Assert.False(battle.Query(new IsUnitStillAvailableThisTurn(unitA)));
    Assert.False(battle.Query(new CanUnitActNow(unitA)));
    Assert.True(battle.Query(new IsUnitStillAvailableThisTurn(unitB)));
    Assert.Equal(faction, session.ActiveSide);
  }

  [TestCase]
  public static void KnockedOutActiveFactionKeepsItsQueueHeadUntilEndTurnThenIsSkipped()
  {
    using var battle = BattleFixture.Duel();
    battle.ApplyDamage(battle.PlayerUnit, 20, DamageKind.Stun);

    Assert.Equal(battle.PlayerFaction, battle.Session.ActiveSide);
    Assert.Equal(battle.PlayerFaction, battle.Session.TurnQueue.AsValueEnumerable().First());

    battle.EndFactionTurn(battle.PlayerFaction);
    Assert.Equal(battle.EnemyFaction, battle.Session.ActiveSide);
    battle.AdvanceTurn();
    Assert.Equal(battle.EnemyFaction, battle.Session.ActiveSide);
    Assert.Equal(2, battle.Session.TurnNumber);
    Assert.False(battle.Session.TurnQueue.AsValueEnumerable().Contains(battle.PlayerFaction));
  }

  [TestCase]
  public static void ConsciousAllyRemainsAvailableWhileTheBodyStaysUnavailableNextRound()
  {
    using var battle = BattleFixture.Duel();
    var ally = battle.Spawn(TestData.MakeCombatant("Support", battle.PlayerFaction), new Vector3I(0, 0, 0));
    battle.ApplyDamage(battle.PlayerUnit, 20, DamageKind.Stun);

    battle.AdvanceTurn();
    battle.AdvanceTurn();

    Assert.Equal(battle.PlayerFaction, battle.Session.ActiveSide);
    Assert.True(battle.Query(new IsUnitStillAvailableThisTurn(ally)));
    Assert.False(battle.Query(new IsUnitStillAvailableThisTurn(battle.PlayerUnit)));
  }

  [TestCase]
  public static void ImmobilizationAndExhaustedApDoNotRemoveConsciousFactionsFromFutureTurns()
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    battle.PlayerUnit.ApplyStatusEffect(MakeStun(duration: 2));
    battle.EnemyUnit.SpendActionPoints(battle.EnemyUnit.CurrentActionPoints);
    Assert.False(battle.Query(new CanUnitActNow(battle.PlayerUnit)));

    battle.EndFactionTurn(battle.PlayerFaction);
    Assert.Equal(battle.EnemyFaction, battle.Session.ActiveSide);
    Assert.True(battle.Query(new IsUnitStillAvailableThisTurn(battle.EnemyUnit)));

    battle.EndFactionTurn(battle.EnemyFaction);
    Assert.Equal(battle.PlayerFaction, battle.Session.ActiveSide);
    Assert.True(battle.Query(new IsUnitStillAvailableThisTurn(battle.PlayerUnit)));
  }

  // The no-conscious-start invariants these setup-session tests covered moved to their
  // Task 3 owner: TurnSchedulerTest's construction rows (skipped ineligible first faction,
  // rejected empty eligible roster).

  [TestCase(TestName = "Killing the only active unit on an eliminated faction does not auto-advance")]
  public static void KillingTheOnlyActiveUnitOnAnEliminatedFactionDoesNotAutoAdvance()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = FixtureFromSides(new Vector3I(4, 1, 4),
      (factionA, "A1", new Vector3I(0, 0, 0), 10),
      (factionB, "B1", new Vector3I(1, 0, 0), 10));
    var session = battle.Session;
    var unitA = battle.UnitAt(new Vector3I(0, 0, 0));

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
  public static void KillingAnActiveUnitLeavesAnotherSurvivingAllyActable()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = FixtureFromSides(new Vector3I(4, 1, 4),
      (factionA, "A1", new Vector3I(0, 0, 0), 10),
      (factionA, "A2", new Vector3I(1, 0, 0), 10),
      (factionB, "B1", new Vector3I(2, 0, 0), 10));
    var session = battle.Session;
    var unitA1 = battle.UnitAt(new Vector3I(0, 0, 0));
    var unitA2 = battle.UnitAt(new Vector3I(1, 0, 0));

    battle.ApplyDamage(unitA1, 10);

    Assert.Equal(factionA, session.ActiveSide);
    Assert.True(battle.Query(new CanUnitActNow(unitA2)));
    Assert.False(session.Board.IsOccupied(session.Board.At(0, 0, 0)));
    Assert.True(session.DeadUnits.AsValueEnumerable().Contains(unitA1));
  }

  [TestCase(TestName = "Unit can throw a grenade in battle session")]
  public static void UnitCanThrowAGrenadeInBattleSession()
  {
    using var battle = BattleFixture.Solo(new Vector3I(5, 1, 5), new Vector3I(1, 0, 1), actionPoints: 4);
    var unit = battle.Unit;
    var grenade = TestData.MakeGrenade("Practice Grenade", throwRange: 4);
    unit.AddInventoryItem(grenade.Item);

    battle.Throw(unit, grenade, new Vector3I(3, 0, 1));

    Assert.False(unit.HasInventoryItem(grenade.Item));
    Assert.Equal(3, unit.CurrentActionPoints);
    ItemThrownBattleEvent itemThrownEvent = battle.Events.SingleEvent<ItemThrownBattleEvent>();
    Assert.Equal(unit.Id, itemThrownEvent.Unit.Id);
    Assert.Equal(new Vector3I(3, 0, 1), itemThrownEvent.Position.Raw);
  }

  [TestCase(TestName = "AddUnit throws when a validated placement can no longer be applied")]
  public static void AddUnitThrowsWhenValidatedPlacementCanNoLongerBeApplied()
  {
    var faction = TestData.MakeFaction("Player");
    var board = new BattleBoardState(new Vector3I(4, 1, 4));
    var occupiedUnit = TestData.PlacedUnit(board, 0, TestData.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));
    var session = new BattleSession(board, [faction], [occupiedUnit], [], []);

    Assert.Throws<InvalidOperationException>(() =>
      session.AddUnit(TestData.MakeCombatant("Bravo", faction), board.At(1, 0, 1), None, None));
    Assert.Equal(1, session.AliveUnits.AsValueEnumerable().Count());
    Assert.True(session.AliveUnits.AsValueEnumerable().Contains(occupiedUnit));
    Assert.False(session.AliveUnits.AsValueEnumerable().Any(unit => string.Equals(unit.Combatant.Name, "Bravo", StringComparison.Ordinal)));
  }

  [TestCase(TestName = "EndUnitActivation throws when the unit is already unavailable")]
  public static void EndUnitActivationThrowsWhenTheUnitIsAlreadyUnavailable()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = FixtureFromSides(new Vector3I(4, 1, 4),
      (faction, "Alpha", new Vector3I(0, 0, 0), 20),
      (faction, "Bravo", new Vector3I(1, 0, 0), 20));
    var session = battle.Session;
    var unit = battle.UnitAt(new Vector3I(0, 0, 0));

    session.EndUnitActivation(unit);

    Assert.False(battle.Query(new IsUnitStillAvailableThisTurn(unit)));
    Assert.Throws<InvalidOperationException>(() => session.EndUnitActivation(unit));
  }

  [TestCase(TestName = "EndFactionTurn throws when the expected side is not active")]
  public static void EndFactionTurnThrowsWhenTheExpectedSideIsNotActive()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = FixtureFromSides(new Vector3I(4, 1, 4),
      (factionA, "A1", new Vector3I(0, 0, 0), 20),
      (factionB, "B1", new Vector3I(1, 0, 0), 20));
    var session = battle.Session;

    Assert.Throws<InvalidOperationException>(() => session.EndFactionTurn(factionB));
    Assert.Equal(factionA, session.ActiveSide);
  }

  // Complete-input construction: a one-unit roster is the minimal live session.
  private static BattleSession FreshSession(int? randomSeed = null)
  {
    var faction = TestData.MakeFaction("Player");
    var board = new BattleBoardState(new Vector3I(2, 1, 2));
    var unit = TestData.PlacedUnit(board, 0, TestData.MakeCombatant("A1", faction), new Vector3I(0, 0, 0));
    return new BattleSession(board, [faction], [unit], [], [], randomSeed: randomSeed);
  }

  [TestCase(TestName = "RollPercent with the same seed produces the same in-range sequence")]
  public static void RollPercentWithTheSameSeedProducesTheSameInRangeSequence()
  {
    var first = FreshSession(randomSeed: 1234);
    var second = FreshSession(randomSeed: 1234);

    for (int i = 0; i < 20; i++)
    {
      int roll = first.RollPercent();
      Assert.True(roll >= 0);
      Assert.True(roll < 100);
      Assert.Equal(roll, second.RollPercent());
    }
  }

}
