#nullable enable
using FunProject.Battle;
using FunProject.Combatants;
using GdUnit4;
using Godot;
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

    Assert.True(board.IsInBounds(new Vector3I(3, 1, 4)));
    Assert.False(board.IsInBounds(new Vector3I(3, 2, 4)));
    Assert.False(board.IsInBounds(new Vector3I(3, 1, 5)));

    var tile = board.GetTileOrNull(new Vector3I(3, 1, 4));
    Assert.True(tile != null);
    Assert.Equal(new Vector3I(3, 1, 4), tile!.Coordinates);
  }

  [TestCase(TestName = "SpawnUnit occupies its tile")]
  public void SpawnUnitOccupiesItsTile()
  {
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 2, 4));
    var faction = BattleTestFactory.MakeFaction("City Guard");
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));

    var tile = session.Board.GetTile(new Vector3I(1, 0, 1));
    Assert.True(tile.IsOccupied);
    Assert.Equal(unit.UnitId, tile.OccupantUnitId!.Value);
  }

  [TestCase(TestName = "SpawnUnit rejects occupied tile")]
  public void SpawnUnitRejectsOccupiedTile()
  {
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4));
    var faction = BattleTestFactory.MakeFaction("City Guard");

    SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));
    var result = BattleSessionMutation.SpawnUnit(BattleTestFactory.MakeCombatant("Bravo", faction), new Vector3I(1, 0, 1)).Execute(session);

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
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4));
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");

    SpawnUnit(session, BattleTestFactory.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("A2", factionA), new Vector3I(1, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("B1", factionB), new Vector3I(2, 0, 0));

    Assert.Equal(2, session.GlobalFactionTurnOrder.Count);
    Assert.Equal(factionA, session.GlobalFactionTurnOrder.First());
    Assert.Equal(factionB, session.GlobalFactionTurnOrder.Last());
  }

  [TestCase(TestName = "Constructor seeds global faction order and faction rosters")]
  public void ConstructorSeedsGlobalFactionOrderAndFactionRosters()
  {
    var factionA = BattleTestFactory.MakeFaction("A");
    var factionB = BattleTestFactory.MakeFaction("B");
    var combatantA = BattleTestFactory.MakeCombatant("A1", factionA);
    var combatantB = BattleTestFactory.MakeCombatant("B1", factionB);
    var session = new BattleSession(
      new Vector3I(4, 1, 4),
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
    Assert.Equal(0, session.AliveUnits.Count);
    Assert.False(session.Board.GetTile(new Vector3I(0, 0, 0)).IsOccupied);
  }

  [TestCase(TestName = "Constructor can use a prebuilt board state")]
  public void ConstructorCanUseAPrebuiltBoardState()
  {
    var faction = BattleTestFactory.MakeFaction("A");
    var board = new BattleBoardState(new Vector3I(3, 1, 3));
    board.GetTile(new Vector3I(1, 0, 0)).IsWalkable = false;

    var session = new BattleSession(
      board,
      [faction],
      new Dictionary<Faction, IEnumerable<Combatant>>());

    var result = BattleSessionMutation.SpawnUnit(BattleTestFactory.MakeCombatant("A1", faction), new Vector3I(1, 0, 0)).Execute(session);

    Assert.True(object.ReferenceEquals(board, session.Board));
    Assert.False(result.Succeeded);
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

    PassUnit(session, unitA.UnitId);
    Assert.Equal(1, session.TurnNumber);
    Assert.Equal(factionB, session.ActiveSide);

    PassUnit(session, unitB.UnitId);
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

    ApplyDamage(session, unitC.UnitId, 10);
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

    var moved = BattleSessionMutation.MoveUnitStep(unit.UnitId, new Vector3I(1, 1, 1), 2).Execute(session);

    Assert.True(moved.Succeeded);
    Assert.Equal(new Vector3I(1, 1, 1), unit.Position);
    Assert.False(session.Board.GetTile(new Vector3I(1, 0, 1)).IsOccupied);
    Assert.True(session.Board.GetTile(new Vector3I(1, 1, 1)).IsOccupied);
    Assert.Equal(3, unit.CurrentActionPoints);
  }

  [TestCase(TestName = "Move rejects non-adjacent destination")]
  public void MoveRejectsNonAdjacentDestination()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
    StartBattle(session);

    var result = BattleSessionMutation.MoveUnitStep(unit.UnitId, new Vector3I(2, 0, 0)).Execute(session);
    Assert.False(result.Succeeded);
  }

  [TestCase(TestName = "Vertical move is allowed as a one-cell step")]
  public void VerticalMoveIsAllowedAsAOneCellStep()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 3, 3), [faction]);
    var unit = SpawnUnit(session, BattleTestFactory.MakeCombatant("Climber", faction), new Vector3I(1, 0, 1));
    StartBattle(session);

    var result = BattleSessionMutation.MoveUnitStep(unit.UnitId, new Vector3I(1, 1, 1)).Execute(session);
    Assert.True(result.Succeeded);
    Assert.Equal(new Vector3I(1, 1, 1), unit.Position);
  }

  [TestCase(TestName = "Passing a unit ends its activation while keeping the next ally available")]
  public void PassingAUnitEndsItsActivationWhileKeepingTheNextAllyAvailable()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 4), [faction]);
    var unitA = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
    var unitB = SpawnUnit(session, BattleTestFactory.MakeCombatant("Bravo", faction), new Vector3I(1, 0, 0));
    StartBattle(session);

    PassUnit(session, unitA.UnitId);

    Assert.False(session.IsUnitStillAvailableThisTurn(unitA.UnitId));
    Assert.False(session.CanUnitActNow(unitA.UnitId));
    Assert.True(session.IsUnitStillAvailableThisTurn(unitB.UnitId));
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

    ApplyDamage(session, unitA.UnitId, 10);

    Assert.False(session.Board.GetTile(new Vector3I(0, 0, 0)).IsOccupied);
    Assert.Equal(factionA, session.ActiveSide);
    Assert.False(session.AliveUnits.Contains(unitA));
    Assert.True(session.DeadUnits.Contains(unitA));
    Assert.False(session.GetFactionAlive(factionA).Contains(unitA));
    Assert.True(session.GetFactionDead(factionA).Contains(unitA));
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

    ApplyDamage(session, unitA1.UnitId, 10);

    Assert.Equal(factionA, session.ActiveSide);
    Assert.True(session.CanUnitActNow(unitA2.UnitId));
    Assert.False(session.Board.GetTile(unitA1.Position).IsOccupied);
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

    Assert.False(session.CanUnitActNow(unitA2.UnitId));

    ApplyDamage(session, unitA1.UnitId, 10);

    Assert.Equal(factionA, session.ActiveSide);
    Assert.Equal(1, session.TurnNumber);
    Assert.False(session.CanUnitActNow(unitA2.UnitId));

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

    BattleEvent? thrownEvent = null;
    session.EventRaised += battleEvent =>
    {
      if (battleEvent.Type == BattleEventType.ItemThrown)
        thrownEvent = battleEvent;
    };

    StartBattle(session);
    var threw = BattleSessionMutation.ThrowItem(unit.UnitId, grenade, new Vector3I(3, 0, 1)).Execute(session);

    Assert.True(threw.Succeeded);
    Assert.False(unit.HasInventoryItem(grenade));
    Assert.Equal(3, unit.CurrentActionPoints);
    Assert.True(thrownEvent.HasValue);
    Assert.Equal(BattleEventType.ItemThrown, thrownEvent!.Value.Type);
    Assert.Equal(unit.UnitId, thrownEvent.Value.UnitId!.Value);
    Assert.Equal(new Vector3I(3, 0, 1), thrownEvent.Value.Position!.Value);
  }

  private static BattleUnitState SpawnUnit(BattleSession session, Combatant combatant, Vector3I position)
  {
    var result = BattleSessionMutation.SpawnUnit(combatant, position).Execute(session);
    Assert.True(result.Succeeded);
    Assert.True(result.AffectedUnit != null);
    return result.AffectedUnit!;
  }

  private static void StartBattle(BattleSession session)
  {
    var result = BattleSessionMutation.StartBattle().Execute(session);
    Assert.True(result.Succeeded);
  }

  private static void AdvanceTurn(BattleSession session)
  {
    var activeSide = session.ActiveSide;
    Assert.True(activeSide != null);
    var result = BattleSessionMutation.EndFactionTurn(activeSide!).Execute(session);
    Assert.True(result.Succeeded);
  }

  private static void PassUnit(BattleSession session, int unitId)
  {
    var result = BattleSessionMutation.PassUnit(unitId).Execute(session);
    Assert.True(result.Succeeded);
  }

  private static void ApplyDamage(BattleSession session, int unitId, int amount)
  {
    var result = BattleSessionMutation.ApplyDamage(unitId, amount).Execute(session);
    Assert.True(result.Succeeded);
  }
}
