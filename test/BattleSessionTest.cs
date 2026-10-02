using FunProject.Battle;
using FunProject.Buffs;
using FunProject.Combatants;
using FunProject.Stats;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleSessionTest
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
    var unit = battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));

    var point = battle.Board.At(1, 0, 1);
    Assert.True(battle.Board.IsOccupied(point));
    Assert.Equal(unit.Id, battle.Board.GetOccupant(point).RequireSome());
  }

  [TestCase(TestName = "A running reinforcement onto an occupied tile rejects and throws")]
  public void SpawnUnitRejectsOccupiedTile()
  {
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), new Vector3I(1, 0, 1));

    Assert.Throws<InvalidOperationException>(
      () => battle.Submit(BattleAction.SpawnUnit(
        TestData.MakeCombatant("Bravo", battle.PlayerFaction), battle.At(1, 0, 1))));
  }

  [TestCase(TestName = "Start activates the first prepared faction at round 1")]
  public void StartBattleActivatesFirstParticipatingFaction()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [playerFaction, enemyFaction]);

    battle.Spawn(TestData.MakeCombatant("Alpha", playerFaction), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("Bandit", enemyFaction), new Vector3I(1, 0, 0));
    battle.Start();

    BattleTurn turn = battle.Query(new GetCurrentTurnQuery()).RequireSome();
    Assert.Equal(playerFaction, turn.ActiveFaction);
    Assert.Equal(1, turn.RoundNumber);
  }

  [TestCase(TestName = "Preparation keeps each faction only once in the global order")]
  public void GlobalFactionOrderKeepsEachFactionOnlyOnce()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [factionA]);

    battle.Spawn(TestData.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("A2", factionA), new Vector3I(1, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB), new Vector3I(2, 0, 0));
    battle.Start();

    var order = battle.Query(new GetGlobalFactionTurnOrderQuery());
    Assert.Equal(2, order.Count);
    Assert.Equal(factionA, order[0]);
    Assert.Equal(factionB, order[1]);
  }

  [TestCase(TestName = "The scheduler is valid on construction from the ordered factions")]
  public void SchedulerIsValidOnConstruction()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    var factionC = TestData.MakeFaction("C");
    var preparation = new BattlePreparation(new BattleBoardState(new Vector3I(4, 1, 4)),
      [factionA, factionB, factionC]);
    var state = preparation.State;
    var unitA = preparation.AddUnit(TestData.MakeCombatant("A1", factionA), state.Board.At(0, 0, 0), None, None);
    var unitB = preparation.AddUnit(TestData.MakeCombatant("B1", factionB), state.Board.At(1, 0, 0), None, None);

    var scheduler = new TurnScheduler(state);

    BattleTurn turn = scheduler.CurrentTurn;
    Assert.Equal(factionA, turn.ActiveFaction);
    Assert.Equal(1, turn.RoundNumber);
    Assert.True(scheduler.IsUnitAvailable(unitA));
    Assert.False(scheduler.IsUnitAvailable(unitB));
  }

  [TestCase(TestName = "Preparation can use a prebuilt board state")]
  public void ConstructorCanUseAPrebuiltBoardState()
  {
    var faction = TestData.MakeFaction("A");
    var board = new BattleBoardState(new Vector3I(3, 1, 3));
    board.SetTileWalkable(board.ValidatePoint(new Vector3I(1, 0, 0)).RequireSome(), false);

    var preparation = new BattlePreparation(board, [faction]);
    Assert.Throws<InvalidOperationException>(() =>
      preparation.AddUnit(TestData.MakeCombatant("A1", faction), board.At(1, 0, 0), None, None));

    Assert.True(ReferenceEquals(board, preparation.State.Board));
  }

  [TestCase(TestName = "Construction rejects preparations without factions")]
  public void ConstructorRejectsSessionsWithoutFactions()
  {
    var board = new BattleBoardState(new Vector3I(3, 1, 3));
    var preparation = new BattlePreparation(board, []);

    Assert.Throws<ArgumentException>(() => preparation.Complete());
  }

  [TestCase(TestName = "A prepared unit keeps its equipped weapon")]
  public void SpawnUnitAcceptsEquippedWeaponOption()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [faction]);
    var weapon = new MeleeWeapon(TestData.MakeWeaponData(damage: 10, critChance: 5, range: 1));

    battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0), weapon);
    battle.Start();

    Assert.Equal(weapon, battle.UnitAt(new Vector3I(0, 0, 0)).EquippedWeapon.RequireSome());
  }

  [TestCase(TestName = "Advancing turns rotates A/B/C in supplied order without incrementing early")]
  public void AdvanceTurnRotatesOnlyParticipatingFactionsWithoutIncrementingEarly()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    var factionC = TestData.MakeFaction("C");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [factionA, factionB, factionC]);

    battle.Spawn(TestData.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    battle.Spawn(TestData.MakeCombatant("C1", factionC), new Vector3I(2, 0, 0));
    battle.Start();

    Assert.Equal(factionA, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);

    battle.AdvanceTurn();
    Assert.Equal(factionB, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
    Assert.Equal(1, battle.Query(new GetCurrentTurnQuery()).RequireSome().RoundNumber);

    battle.AdvanceTurn();
    Assert.Equal(factionC, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
    Assert.Equal(1, battle.Query(new GetCurrentTurnQuery()).RequireSome().RoundNumber);

    battle.AdvanceTurn();
    Assert.Equal(factionA, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
    Assert.Equal(2, battle.Query(new GetCurrentTurnQuery()).RequireSome().RoundNumber);
  }

  [TestCase(TestName = "Passing the last available unit advances the global turn counter")]
  public void PassingTheLastAvailableUnitAdvancesTheGlobalTurnCounter()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [factionA, factionB]);
    var unitA = battle.Spawn(TestData.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    var unitB = battle.Spawn(TestData.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    battle.Start();

    Assert.Equal(1, battle.Query(new GetCurrentTurnQuery()).RequireSome().RoundNumber);
    Assert.Equal(factionA, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);

    battle.Pass(unitA);
    Assert.Equal(1, battle.Query(new GetCurrentTurnQuery()).RequireSome().RoundNumber);
    Assert.Equal(factionB, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);

    battle.Pass(unitB);
    Assert.Equal(2, battle.Query(new GetCurrentTurnQuery()).RequireSome().RoundNumber);
    Assert.Equal(factionA, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
  }

  [TestCase(TestName = "Eliminating an unacted faction does not block the global turn counter")]
  public void EliminatingAnUnactedFactionDoesNotBlockTheGlobalTurnCounter()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    var factionC = TestData.MakeFaction("C");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [factionA, factionB, factionC]);

    battle.Spawn(TestData.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    var unitC = battle.Spawn(TestData.MakeCombatant("C1", factionC, health: 10), new Vector3I(2, 0, 0));
    battle.Start();

    battle.ApplyDamage(unitC, 10);

    battle.AdvanceTurn();
    Assert.Equal(1, battle.Query(new GetCurrentTurnQuery()).RequireSome().RoundNumber);
    Assert.Equal(factionB, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);

    battle.AdvanceTurn();
    Assert.Equal(2, battle.Query(new GetCurrentTurnQuery()).RequireSome().RoundNumber);
    Assert.Equal(factionA, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
  }

  [TestCase(TestName = "Adding a faction mid-round delays the global turn counter until it acts")]
  public void AddingAFactionMidRoundDelaysTheGlobalTurnCounterUntilItActs()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    var factionC = TestData.MakeFaction("C");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [factionA, factionB]);

    battle.Spawn(TestData.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    battle.Start();

    battle.Spawn(TestData.MakeCombatant("C1", factionC), new Vector3I(2, 0, 0));
    // The scheduler owns the authoritative ordered list: the new faction registered without
    // any separate caller-side registration.
    Assert.Equal(3, battle.Query(new GetGlobalFactionTurnOrderQuery()).Count);

    Assert.Equal(1, battle.Query(new GetCurrentTurnQuery()).RequireSome().RoundNumber);

    battle.AdvanceTurn();
    Assert.Equal(1, battle.Query(new GetCurrentTurnQuery()).RequireSome().RoundNumber);
    Assert.Equal(factionB, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);

    battle.AdvanceTurn();
    Assert.Equal(1, battle.Query(new GetCurrentTurnQuery()).RequireSome().RoundNumber);
    Assert.Equal(factionC, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);

    battle.AdvanceTurn();
    Assert.Equal(2, battle.Query(new GetCurrentTurnQuery()).RequireSome().RoundNumber);
    Assert.Equal(factionA, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
  }

  [TestCase(TestName = "Adding units to a faction that already acted waits until the next round")]
  public void AddingUnitsToAFactionThatAlreadyActedWaitsUntilTheNextRound()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [factionA, factionB]);

    var unitA = battle.Spawn(TestData.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    battle.Start();

    battle.AdvanceTurn();
    Assert.Equal(factionB, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
    Assert.Equal(1, battle.Query(new GetCurrentTurnQuery()).RequireSome().RoundNumber);

    // A acted this round, was eliminated, and then reinforced: its acted history must
    // survive, so the reinforcement still waits for round 2 instead of rejoining round 1.
    battle.ApplyDamage(unitA, 999);
    battle.Spawn(TestData.MakeCombatant("A2", factionA), new Vector3I(2, 0, 0));

    battle.AdvanceTurn();
    Assert.Equal(2, battle.Query(new GetCurrentTurnQuery()).RequireSome().RoundNumber);
    Assert.Equal(factionA, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
  }

  [TestCase(TestName = "A mid-round reinforcement joins its side only when that side's turn starts")]
  public void AddingAUnitMidBattleReconcilesTheFactionQueue()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [factionA, factionB]);

    battle.Spawn(TestData.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    battle.Start();

    battle.Spawn(TestData.MakeCombatant("A2", factionA), new Vector3I(2, 0, 0));

    Assert.Equal(factionA, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
    Assert.True(battle.Query(new IsUnitStillAvailableThisTurn(battle.UnitAt(new Vector3I(2, 0, 0)))));

    battle.AdvanceTurn();
    Assert.Equal(factionB, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
    Assert.False(battle.Query(new IsUnitStillAvailableThisTurn(battle.UnitAt(new Vector3I(2, 0, 0)))));
  }

  [TestCase(TestName = "Move updates unit position occupancy and action points")]
  public void MoveUpdatesUnitPositionOccupancyAndActionPoints()
  {
    using var battle = BattleFixture.Solo(new Vector3I(4, 2, 4), new Vector3I(1, 0, 1), actionPoints: 5);
    var unit = battle.Unit;

    battle.Move(unit, [new Vector3I(1, 1, 1)], 2);

    Assert.Equal(new Vector3I(1, 1, 1), battle.PositionOf(unit).RequireSome().Raw);
    Assert.False(battle.Board.IsOccupied(battle.Board.At(1, 0, 1)));
    Assert.True(battle.Board.IsOccupied(battle.Board.At(1, 1, 1)));
    Assert.Equal(3, unit.CurrentActionPoints);
  }

  [TestCase(TestName = "MoveUnit follows a multi-step route and spends AP per step")]
  public void MoveUnitFollowsAMultiStepRouteAndSpendsAPPerStep()
  {
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), new Vector3I(0, 0, 0), actionPoints: 5);
    var unit = battle.Unit;

    BattleBoardState.ValidatedPoint[] validatedPath = battle.Query(new FindPathForUnit(battle.Alive(unit), battle.At(2, 0, 0)));
    BattleBoardState.ValidatedPoint[] path = validatedPath.AsValueEnumerable().Skip(1).ToArray();
    Assert.Equal(3, validatedPath.Length);
    Assert.Equal(2, path.Length);

    BattleActionExecResult moveResult = battle.Submit(BattleAction.MoveUnit(battle.Alive(unit), path)).RequireSome();

    Assert.True(moveResult.Action is MoveUnit);
    Assert.Equal(new Vector3I(2, 0, 0), battle.PositionOf(unit).RequireSome().Raw);
    Assert.False(battle.Board.IsOccupied(battle.Board.At(0, 0, 0)));
    Assert.True(battle.Board.IsOccupied(battle.Board.At(2, 0, 0)));
    Assert.Equal(3, unit.CurrentActionPoints);
  }

  [TestCase(TestName = "Battle-owned unit position follows movement")]
  public void SessionOwnedUnitPositionFollowsMovement()
  {
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), new Vector3I(0, 0, 0), health: 10, actionPoints: 5);
    var unit = battle.Unit;

    Assert.Equal(new Vector3I(0, 0, 0), battle.PositionOf(unit).RequireSome().Raw);

    battle.Move(unit, [new Vector3I(1, 0, 0)]);

    Assert.Equal(new Vector3I(1, 0, 0), battle.PositionOf(unit).RequireSome().Raw);

    BattleBoardState.ValidatedPoint destination = battle.Board.At(1, 0, 0);
    Assert.Equal(unit, battle.UnitAt(new Vector3I(1, 0, 0)));

    battle.ApplyDamage(unit, 10);
    Assert.True(battle.PositionOf(unit).IsNone);
    Assert.True(battle.Query(new GetUnitAtTile(destination)).IsNone);

    var replacement = battle.Spawn(TestData.MakeCombatant("Replacement", battle.PlayerFaction, health: 10), new Vector3I(1, 0, 0));
    Assert.Equal(replacement, battle.UnitAt(new Vector3I(1, 0, 0)));
    Assert.Equal(new Vector3I(1, 0, 0), battle.PositionOf(replacement).RequireSome().Raw);
  }

  [TestCase(TestName = "MoveUnit rejects routes that cost more AP than the unit has")]
  public void MoveUnitRejectsRoutesThatCostMoreApThanTheUnitHas()
  {
    using var battle = BattleFixture.Solo(new Vector3I(4, 1, 4), new Vector3I(0, 0, 0), actionPoints: 1);
    var unit = battle.Unit;

    BattleBoardState.ValidatedPoint[] validatedPath = battle.Query(new FindPathForUnit(battle.Alive(unit), battle.At(2, 0, 0)));
    BattleBoardState.ValidatedPoint[] path = validatedPath.AsValueEnumerable().Skip(1).ToArray();
    Assert.Equal(3, validatedPath.Length);
    Assert.Equal(2, path.Length);

    var action = BattleAction.MoveUnit(battle.Alive(unit), path);

    Assert.Throws<InvalidOperationException>(() => battle.Submit(action));
    Assert.Equal(new Vector3I(0, 0, 0), battle.PositionOf(unit).RequireSome().Raw);
    Assert.Equal(1, unit.CurrentActionPoints);
    Assert.True(battle.Board.IsOccupied(battle.Board.At(0, 0, 0)));
  }

  [TestCase(TestName = "Passing a unit ends its activation while keeping the next ally available")]
  public void PassingAUnitEndsItsActivationWhileKeepingTheNextAllyAvailable()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [faction]);
    var unitA = battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
    var unitB = battle.Spawn(TestData.MakeCombatant("Bravo", faction), new Vector3I(1, 0, 0));
    battle.Start();

    battle.Pass(unitA);

    Assert.False(battle.Query(new IsUnitStillAvailableThisTurn(unitA)));
    Assert.False(battle.Query(new CanUnitActNow(unitA)));
    Assert.True(battle.Query(new IsUnitStillAvailableThisTurn(unitB)));
    Assert.Equal(faction, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
  }

  [TestCase]
  public void KnockedOutActiveFactionKeepsItsQueueHeadUntilEndTurnThenIsSkipped()
  {
    using var battle = BattleFixture.Duel();
    battle.ApplyDamage(battle.PlayerUnit, 20, DamageKind.Stun);

    Assert.Equal(battle.PlayerFaction, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);

    battle.EndFactionTurn(battle.PlayerFaction);
    Assert.Equal(battle.EnemyFaction, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
    battle.AdvanceTurn();
    Assert.Equal(battle.EnemyFaction, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
    Assert.Equal(2, battle.Query(new GetCurrentTurnQuery()).RequireSome().RoundNumber);
  }

  [TestCase]
  public void ConsciousAllyRemainsAvailableWhileTheBodyStaysUnavailableNextRound()
  {
    using var battle = BattleFixture.Duel();
    var ally = battle.Spawn(TestData.MakeCombatant("Support", battle.PlayerFaction), new Vector3I(0, 0, 0));
    battle.ApplyDamage(battle.PlayerUnit, 20, DamageKind.Stun);

    battle.AdvanceTurn();
    battle.AdvanceTurn();

    Assert.Equal(battle.PlayerFaction, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
    Assert.True(battle.Query(new IsUnitStillAvailableThisTurn(ally)));
    Assert.False(battle.Query(new IsUnitStillAvailableThisTurn(battle.PlayerUnit)));
  }

  [TestCase]
  public void ImmobilizationAndExhaustedApDoNotRemoveConsciousFactionsFromFutureTurns()
  {
    using var battle = BattleFixture.Duel(playerControlled: true);
    battle.PlayerUnit.ApplyStatusEffect(MakeStun(duration: 2));
    battle.EnemyUnit.SpendActionPoints(battle.EnemyUnit.CurrentActionPoints);
    Assert.False(battle.Query(new CanUnitActNow(battle.PlayerUnit)));

    battle.EndFactionTurn(battle.PlayerFaction);
    Assert.Equal(battle.EnemyFaction, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
    Assert.True(battle.Query(new IsUnitStillAvailableThisTurn(battle.EnemyUnit)));

    battle.EndFactionTurn(battle.EnemyFaction);
    Assert.Equal(battle.PlayerFaction, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
    Assert.True(battle.Query(new IsUnitStillAvailableThisTurn(battle.PlayerUnit)));
  }

  [TestCase]
  public void SchedulerIgnoresDisabledSpawnCandidates()
  {
    using var battle = BattleFixture.Duel();
    battle.ApplyDamage(battle.PlayerUnit, 20, DamageKind.Stun);
    var state = battle.Read.State;
    var scheduler = new TurnScheduler(state);

    scheduler.RegisterReinforcement(battle.PlayerUnit);

    Assert.False(scheduler.IsUnitAvailable(battle.PlayerUnit));
  }

  [TestCase(TestName = "Preparation rejects a wholly unconscious side, naming it and keeping the other side valid")]
  public void PreparationRejectsOnlyUnconsciousForces()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    var preparation = new BattlePreparation(new BattleBoardState(new Vector3I(3, 1, 1)),
      [playerFaction, enemyFaction]);
    var unit = preparation.AddUnit(
      TestData.MakeCombatant("Solo", playerFaction, health: 20),
      preparation.State.Board.At(0, 0, 0), None, None);

    // Raw stun through the unit, then the shared participant reconciliation (occupancy
    // retained, disabled visibility resolved) before the Complete check.
    unit.ReceiveStun(20);
    preparation.ReconcileParticipant(unit);
    Assert.Equal(20, unit.CurrentStun);
    Assert.True(unit.IsUnconscious);

    var rival = preparation.AddUnit(
      TestData.MakeCombatant("Rival", enemyFaction),
      preparation.State.Board.At(2, 0, 0), None, None);
    Assert.False(rival.IsUnconscious);

    Either<BattleSetupFailure, BattleState> outcome = preparation.Complete();

    Assert.True(outcome.IsLeft);
    outcome.IfLeft(failure =>
    {
      Assert.Equal(BattleSetupFailureReason.NoConsciousUnits, failure.Reason);
      Assert.True(failure.Message.Contains("Player"));
      Assert.False(failure.Message.Contains("Enemy"));
    });
  }

  [TestCase(TestName = "A conscious teammate permits dead and unconscious companions on the same side")]
  public void ConsciousTeammatePermitsDisabledCompanions()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(6, 1, 1), [faction]);
    var roused = TestData.MakeBuff("Roused", new AlwaysMetBuffCondition(),
      statMods: [new ActionPointsStatMod { Modifiers = [StatModifier.Add(2)] }]);
    var conscious = battle.Spawn(TestData.MakeCombatant("Alive", faction, vision: 2), Vector3I.Zero);
    var dead = battle.Spawn(TestData.MakeCombatant("Dead", faction), new Vector3I(1, 0, 0));
    var stillborn = battle.Spawn(TestData.MakeCombatant("Stillborn", faction, health: 0, buffs: [roused]), new Vector3I(4, 0, 0));
    var unconscious = battle.Spawn(TestData.MakeCombatant("Out", faction), new Vector3I(3, 0, 0));
    var exclusiveTile = battle.At(5, 0, 0); // only the fighter at (3,0,0) sees it
    var sharedTile = battle.At(2, 0, 0); // inside the conscious teammate's vision 2
    Assert.True(battle.Query(new IsTileVisibleToFaction(faction, exclusiveTile)));
    Assert.True(battle.Query(new IsTileVisibleToFaction(faction, sharedTile)));

    battle.Damage(dead, 999);
    battle.Damage(unconscious, 20, DamageKind.Stun);

    // Immediately after the preset — no intervening spawn, event, or Start — the knocked-out
    // fighter's exclusive tile is hidden while the teammate's own vision still answers.
    Assert.False(battle.Query(new IsTileVisibleToFaction(faction, exclusiveTile)));
    Assert.True(battle.Query(new IsTileVisibleToFaction(faction, sharedTile)));

    // Preparation bookkeeping: the dead bodies left the board (their tiles are reusable),
    // the unconscious body still occupies its tile, and every identity stays in the pool.
    Assert.True(battle.PositionOf(dead).IsNone);
    Assert.False(battle.Board.IsOccupied(battle.Board.At(1, 0, 0)));
    Assert.True(battle.PositionOf(stillborn).IsNone);
    Assert.False(battle.Board.IsOccupied(battle.Board.At(4, 0, 0)));
    battle.Damage(dead, 999); // reconciliation is idempotent: the body is already off the board
    Assert.True(battle.PositionOf(dead).IsNone);
    var tileHeir = battle.Spawn(TestData.MakeCombatant("Fill", faction), new Vector3I(1, 0, 0));
    Assert.True(battle.Board.IsOccupied(battle.Board.At(3, 0, 0)));

    // The session-start observer reads before the hook pass: it captures the preparation
    // boundary's dead-companion AP. The drain hook then spends everyone's AP, so the value
    // left after Start can only come from the opening dispatch's own normalization pass.
    int stillbornApAtSessionStart = -1;
    battle.OnCommitted(battleEvent =>
    {
      if (battleEvent is SessionStartedBattleEvent)
        stillbornApAtSessionStart = stillborn.CurrentActionPoints;
    });
    battle.RegisterHook<SessionStartedBattleEvent>(new SpendActionPointsHook([conscious, stillborn]));

    battle.Start();

    Assert.True(battle.Query(new GetCompletedBattleQuery()).IsNone);
    Assert.True(conscious.IsAlive);
    Assert.True(dead.IsDead);
    Assert.True(stillborn.IsDead);
    Assert.True(unconscious.IsUnconscious);
    Assert.False(battle.Query(new CanUnitActNow(unconscious)));
    Assert.False(battle.Query(new CanUnitActNow(stillborn)));
    Assert.True(battle.PositionOf(dead).IsNone);
    Assert.True(battle.PositionOf(stillborn).IsNone);
    Assert.True(battle.PositionOf(tileHeir).IsSome);
    Assert.True(battle.Board.IsOccupied(battle.Board.At(3, 0, 0)));
    // The dead companion's spawn-active grant raised its effective maximum, and both
    // normalization passes treat it as the initial participant it is.
    Assert.Equal(6, stillbornApAtSessionStart);
    Assert.Equal(6, stillborn.MaxActionPoints);
    Assert.Equal(6, stillborn.CurrentActionPoints);
  }

  // Spawn buffs can genuinely extend vision after the placement dispatch: the observer's
  // base-vision placement stream cannot reach the target, so the first spot is discovered
  // only after buff evaluation — and must notify exactly once, through the opening stream.
  [TestCase(TestName = "A conscious spawn's buff-discovered first spot notifies exactly once")]
  public void SpawnBuffVisionDiscoveryNotifiesExactlyOnce()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    // The blind target's only role is being seen, so the stream holds exactly one spotting.
    using var battle = new BattleFixture(new Vector3I(4, 1, 1), [playerFaction, enemyFaction]);
    var buff = TestData.MakeBuff("EagleEye", new AlwaysMetBuffCondition(),
      statMods: [new VisionStatMod { Modifiers = [StatModifier.Add(3)] }]);
    var target = battle.Spawn(TestData.MakeCombatant("Target", enemyFaction, vision: 0), new Vector3I(3, 0, 0));
    var observer = battle.Spawn(TestData.MakeCombatant("Observer", playerFaction, vision: 1, buffs: [buff]), new Vector3I(0, 0, 0));

    battle.Start();

    Assert.Equal(1, battle.Events.EventsOf<UnitSpottedBattleEvent>().AsValueEnumerable()
      .Count(e => ReferenceEquals(e.Unit, observer) && ReferenceEquals(e.Target, target)));
    Assert.Equal(1, battle.Events.EventsOf<UnitSpottedBattleEvent>().AsValueEnumerable().Count());
  }

  [TestCase(TestName = "Killing the only active unit on an eliminated faction does not auto-advance")]
  public void KillingTheOnlyActiveUnitOnAnEliminatedFactionDoesNotAutoAdvance()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [factionA, factionB]);
    var unitA = battle.Spawn(TestData.MakeCombatant("A1", factionA, health: 10), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB, health: 10), new Vector3I(1, 0, 0));
    battle.Start();

    Assert.Equal(factionA, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);

    battle.ApplyDamage(unitA, 10);

    Assert.False(battle.Board.IsOccupied(battle.Board.At(0, 0, 0)));
    Assert.Equal(factionA, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
    Assert.False(battle.PositionOf(unitA).IsSome);
    Assert.True(battle.Query(new GetFactionDeadUnits(factionA)).AsValueEnumerable().Select(d => d.State).Contains(unitA));

    battle.AdvanceTurn();
    Assert.Equal(factionB, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
  }

  [TestCase(TestName = "Killing an active unit leaves another surviving ally actable")]
  public void KillingAnActiveUnitLeavesAnotherSurvivingAllyActable()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [factionA, factionB]);
    var unitA1 = battle.Spawn(TestData.MakeCombatant("A1", factionA, health: 10), new Vector3I(0, 0, 0));
    var unitA2 = battle.Spawn(TestData.MakeCombatant("A2", factionA, health: 10), new Vector3I(1, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB, health: 10), new Vector3I(2, 0, 0));
    battle.Start();

    battle.ApplyDamage(unitA1, 10);

    Assert.Equal(factionA, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
    Assert.True(battle.Query(new CanUnitActNow(unitA2)));
    Assert.False(battle.Board.IsOccupied(battle.Board.At(0, 0, 0)));
    Assert.True(unitA1.IsDead);
  }

  [TestCase(TestName = "Unit can throw a grenade in battle session")]
  public void UnitCanThrowAGrenadeInBattleSession()
  {
    using var battle = BattleFixture.Solo(new Vector3I(5, 1, 5), new Vector3I(1, 0, 1), actionPoints: 4, start: false);
    var unit = battle.Unit;
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

  [TestCase(TestName = "Preparation rejects a placement that can no longer be applied")]
  public void AddUnitThrowsWhenValidatedPlacementCanNoLongerBeApplied()
  {
    var faction = TestData.MakeFaction("Player");
    var preparation = new BattlePreparation(new BattleBoardState(new Vector3I(4, 1, 4)), [faction]);
    BattleBoardState.ValidatedPoint occupiedPoint = preparation.State.Board.At(1, 0, 1);
    preparation.State.AddUnit(TestData.MakeCombatant("Alpha", faction), occupiedPoint, None, None);

    Assert.Throws<InvalidOperationException>(() =>
      preparation.AddUnit(TestData.MakeCombatant("Bravo", faction), occupiedPoint, None, None));
    Assert.Equal(1, preparation.State.AliveUnits.AsValueEnumerable().Count());
  }

  [TestCase(TestName = "EndUnitActivation throws when the unit is already unavailable")]
  public void EndUnitActivationThrowsWhenTheUnitIsAlreadyUnavailable()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [faction]);
    var unit = battle.Spawn(TestData.MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("Bravo", faction), new Vector3I(1, 0, 0));
    battle.Start();

    battle.Session.EndUnitActivation(unit);

    Assert.False(battle.Query(new IsUnitStillAvailableThisTurn(unit)));
    Assert.Throws<InvalidOperationException>(() => battle.Session.EndUnitActivation(unit));
  }

  [TestCase(TestName = "EndUnitActivation advances the turn when no active units remain")]
  public void EndUnitActivationAdvancesTheTurnWhenNoActiveUnitsRemain()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [factionA, factionB]);
    var unitA = battle.Spawn(TestData.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    battle.Start();

    battle.Session.EndUnitActivation(unitA);

    Assert.False(battle.Query(new IsUnitStillAvailableThisTurn(unitA)));
    Assert.Equal(factionB, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
    Assert.Equal(1, battle.Query(new GetCurrentTurnQuery()).RequireSome().RoundNumber);
  }

  [TestCase(TestName = "EndFactionTurn throws when the expected side is not active")]
  public void EndFactionTurnThrowsWhenTheExpectedSideIsNotActive()
  {
    var factionA = TestData.MakeFaction("A");
    var factionB = TestData.MakeFaction("B");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [factionA, factionB]);

    battle.Spawn(TestData.MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
    battle.Start();

    Assert.Throws<InvalidOperationException>(() => battle.Session.EndFactionTurn(factionB));
    Assert.Equal(factionA, battle.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction);
  }

  [TestCase(TestName = "RollPercent with the same seed produces the same in-range sequence")]
  public void RollPercentWithTheSameSeedProducesTheSameInRangeSequence()
  {
    var faction = TestData.MakeFaction("Player");
    var first = new BattlePreparation(new BattleBoardState(new Vector3I(2, 1, 2)), [faction], randomSeed: 1234);
    var second = new BattlePreparation(new BattleBoardState(new Vector3I(2, 1, 2)), [faction], randomSeed: 1234);

    for (int i = 0; i < 20; i++)
    {
      int roll = first.State.RollPercent();
      Assert.True(roll >= 0);
      Assert.True(roll < 100);
      Assert.Equal(roll, second.State.RollPercent());
    }
  }

  [TestCase(TestName = "Preparation defaults to the standard hit chance calculator")]
  public void SessionDefaultsToTheStandardHitChanceCalculator()
  {
    var faction = TestData.MakeFaction("Player");
    var preparation = new BattlePreparation(new BattleBoardState(new Vector3I(2, 1, 2)), [faction]);

    Assert.True(preparation.State.HitChanceCalculator is StandardHitChanceCalculator);
  }
}
