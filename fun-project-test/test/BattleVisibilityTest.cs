using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Tests;
using GdUnit4;
using Godot;
using static BattleActionTestHelper;
using static BattleQueryTestHelper;

[TestSuite]
[RequireGodotRuntime]
public class BattleVisibilityTest
{
  [TestCase(TestName = "Open space visibility succeeds between units")]
  public void OpenSpaceVisibilitySucceedsBetweenUnits()
  {
    var (session, playerFaction, _, observer, target) = TwoUnitScenario(new Vector3I(4, 1, 1), new Vector3I(0, 0, 0), 4, new Vector3I(2, 0, 0));

    StartBattle(session);

    Assert.True(GetValue(Query(session, new IsUnitVisibleToUnit(observer.State, target.State))));
    Assert.True(GetValue(Query(session, new IsUnitVisibleToFaction(playerFaction, target.State))));
  }

  [TestCase(TestName = "Unit state tracks currently visible units")]
  public void UnitStateTracksCurrentlyVisibleUnits()
  {
    var (session, _, _, observer, target) = TwoUnitScenario(new Vector3I(4, 1, 1), new Vector3I(0, 0, 0), 4, new Vector3I(2, 0, 0));

    StartBattle(session);

    Assert.True(observer.State.VisibleUnits.Contains(target.State));
    Assert.True(observer.State.VisibleTiles.Contains(ValidateTile(session, new Vector3I(2, 0, 0))));
  }

  [TestCase(TestName = "Blocking tiles break line of sight")]
  public void BlockingTilesBreakLineOfSight()
  {
    var (session, playerFaction, _, observer, target) = TwoUnitScenario(new Vector3I(4, 1, 1), new Vector3I(0, 0, 0), 4, new Vector3I(2, 0, 0));

    session.Board.GetTile(session.Board.ValidatePoint(new Vector3I(1, 0, 0)).RequireSome()).BlocksLineOfSight = true;
    StartBattle(session);

    Assert.False(GetValue(Query(session, new IsUnitVisibleToUnit(observer.State, target.State))));
    Assert.False(GetValue(Query(session, new IsUnitVisibleToFaction(playerFaction, target.State))));
  }

  // Verifies that a blocking tile is visible (it IS the wall you look at) but the tile directly
  // behind it on the line is shadowed, while off-axis open tiles remain visible.
  [TestCase(TestName = "Walls cast line-of-sight shadows")]
  public void WallsCastLineOfSightShadows()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 2), [playerFaction]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Observer", playerFaction, vision: 3), new Vector3I(0, 0, 0));
    BattleBoardState.ValidatedPoint blockingTile = ValidateTile(session, new Vector3I(1, 0, 0));
    BattleBoardState.ValidatedPoint shadowedTile = ValidateTile(session, new Vector3I(2, 0, 0));
    BattleBoardState.ValidatedPoint openTile = ValidateTile(session, new Vector3I(0, 0, 1));

    session.Board.GetTile(blockingTile).BlocksLineOfSight = true;
    StartBattle(session);

    // The wall tile itself is visible (endpoint rule — you see the wall you look at).
    Assert.True(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, blockingTile))));
    // The tile directly behind the wall on the straight line is in shadow.
    Assert.False(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, shadowedTile))));
    // An off-axis tile with no blocker on its LOS ray is still visible.
    Assert.True(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, openTile))));
  }

  [TestCase(TestName = "Zero vision units see only their own tile")]
  public void ZeroVisionUnitsSeeOnlyTheirOwnTile()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(2, 1, 1), [playerFaction]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Observer", playerFaction, vision: 0), new Vector3I(0, 0, 0));
    BattleBoardState.ValidatedPoint observerTile = ValidateTile(session, new Vector3I(0, 0, 0));
    BattleBoardState.ValidatedPoint adjacentTile = ValidateTile(session, new Vector3I(1, 0, 0));

    StartBattle(session);

    Assert.True(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, observerTile))));
    Assert.False(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, adjacentTile))));
  }

  [TestCase(TestName = "Adjacent diagonal tiles remain visible around corners")]
  public void AdjacentDiagonalTilesRemainVisibleAroundCorners()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(2, 1, 2), [playerFaction]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Observer", playerFaction, vision: 3), new Vector3I(0, 0, 0));
    BattleBoardState.ValidatedPoint blockingTile = ValidateTile(session, new Vector3I(1, 0, 0));
    BattleBoardState.ValidatedPoint diagonalTile = ValidateTile(session, new Vector3I(1, 0, 1));

    session.Board.GetTile(blockingTile).BlocksLineOfSight = true;
    StartBattle(session);

    Assert.True(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, blockingTile))));
    Assert.True(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, diagonalTile))));
  }

  [TestCase(TestName = "Diagonal corner peeking does not reveal tiles behind blockers")]
  public void DiagonalCornerPeekingDoesNotRevealTilesBehindBlockers()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 1, 2), [playerFaction]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Observer", playerFaction, vision: 3), new Vector3I(0, 0, 0));
    BattleBoardState.ValidatedPoint firstBlockingTile = ValidateTile(session, new Vector3I(1, 0, 0));
    BattleBoardState.ValidatedPoint secondBlockingTile = ValidateTile(session, new Vector3I(1, 0, 1));
    BattleBoardState.ValidatedPoint hiddenTile = ValidateTile(session, new Vector3I(2, 0, 1));

    session.Board.GetTile(firstBlockingTile).BlocksLineOfSight = true;
    session.Board.GetTile(secondBlockingTile).BlocksLineOfSight = true;
    StartBattle(session);

    Assert.True(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, firstBlockingTile))));
    Assert.True(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, secondBlockingTile))));
    Assert.False(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, hiddenTile))));
  }

  [TestCase(TestName = "Adjacent diagonal peeking does not see through sealed corners")]
  public void AdjacentDiagonalPeekingDoesNotSeeThroughSealedCorners()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(2, 1, 2), [playerFaction]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Observer", playerFaction, vision: 3), new Vector3I(0, 0, 0));
    BattleBoardState.ValidatedPoint firstBlockingTile = ValidateTile(session, new Vector3I(1, 0, 0));
    BattleBoardState.ValidatedPoint secondBlockingTile = ValidateTile(session, new Vector3I(0, 0, 1));
    BattleBoardState.ValidatedPoint sealedCornerTile = ValidateTile(session, new Vector3I(1, 0, 1));

    session.Board.GetTile(firstBlockingTile).BlocksLineOfSight = true;
    session.Board.GetTile(secondBlockingTile).BlocksLineOfSight = true;
    StartBattle(session);

    Assert.True(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, firstBlockingTile))));
    Assert.True(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, secondBlockingTile))));
    Assert.False(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, sealedCornerTile))));
  }

  // With no vertical blocking flag, a unit can see down an open column to the level below.
  [TestCase(TestName = "Sight passes through an open column below the observer")]
  public void SightPassesThroughOpenColumnBelow()
  {
    var (session, playerFaction, _, observer, target) = TwoUnitScenario(new Vector3I(1, 2, 1), new Vector3I(0, 1, 0), 3, new Vector3I(0, 0, 0));

    StartBattle(session);

    Assert.True(GetValue(Query(session, new IsUnitVisibleToUnit(observer.State, target.State))));
    Assert.True(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, ValidateTile(session, new Vector3I(0, 0, 0))))));
  }

  // BlocksVerticalLineOfSight on the observer's own cell (the floor) seals downward sight.
  [TestCase(TestName = "BlocksVerticalLineOfSight prevents seeing below when set on observer cell")]
  public void BlocksVerticalLineOfSightPreventsSeeingBelow()
  {
    var (session, playerFaction, _, observer, target) = TwoUnitScenario(new Vector3I(1, 2, 1), new Vector3I(0, 1, 0), 3, new Vector3I(0, 0, 0));

    session.Board.GetTile(ValidateTile(session, new Vector3I(0, 1, 0))).BlocksVerticalLineOfSight = true;
    StartBattle(session);

    Assert.False(GetValue(Query(session, new IsUnitVisibleToUnit(observer.State, target.State))));
    Assert.False(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, ValidateTile(session, new Vector3I(0, 0, 0))))));
  }

  // With no vertical blocking flag, a unit can see up an open column to the level above.
  [TestCase(TestName = "Sight passes through an open column above the observer")]
  public void SightPassesThroughOpenColumnAbove()
  {
    var (session, playerFaction, _, observer, target) = TwoUnitScenario(new Vector3I(1, 3, 1), new Vector3I(0, 0, 0), 5, new Vector3I(0, 2, 0));

    StartBattle(session);

    Assert.True(GetValue(Query(session, new IsUnitVisibleToUnit(observer.State, target.State))));
    Assert.True(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, ValidateTile(session, new Vector3I(0, 2, 0))))));
  }

  // BlocksVerticalLineOfSight on an intermediate floor cell seals upward sight through it.
  [TestCase(TestName = "BlocksVerticalLineOfSight on intermediate floor prevents seeing above")]
  public void BlocksVerticalLineOfSightOnIntermediateFloorPreventsSeeingAbove()
  {
    var (session, playerFaction, _, observer, target) = TwoUnitScenario(new Vector3I(1, 3, 1), new Vector3I(0, 0, 0), 5, new Vector3I(0, 2, 0));

    session.Board.GetTile(ValidateTile(session, new Vector3I(0, 1, 0))).BlocksVerticalLineOfSight = true;
    StartBattle(session);

    Assert.False(GetValue(Query(session, new IsUnitVisibleToUnit(observer.State, target.State))));
    Assert.False(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, ValidateTile(session, new Vector3I(0, 2, 0))))));
  }

  // A 3D-diagonal ray (X and Y both change, simultaneous crossing) is sealed when the mid cell
  // has BlocksVerticalLineOfSight — exercises the Rule 3 vertical check on a diagonal step.
  [TestCase(TestName = "BlocksVerticalLineOfSight seals a diagonal ray on a simultaneous crossing")]
  public void BlocksVerticalLineOfSightSealsDiagonalRay()
  {
    var (session, _, _, observer, target) = TwoUnitScenario(new Vector3I(3, 3, 1), new Vector3I(0, 0, 0), 3, new Vector3I(2, 2, 0));
    session.Board.GetTile(ValidateTile(session, new Vector3I(1, 1, 0))).BlocksVerticalLineOfSight = true;
    StartBattle(session);
    Assert.False(GetValue(Query(session, new IsUnitVisibleToUnit(observer.State, target.State))));
  }

  // Mirror: with no vertical blocker on the crossing cell, the same diagonal ray is clear.
  [TestCase(TestName = "A diagonal ray through an open column is clear")]
  public void DiagonalRayThroughOpenColumnIsClear()
  {
    var (session, _, _, observer, target) = TwoUnitScenario(new Vector3I(3, 3, 1), new Vector3I(0, 0, 0), 3, new Vector3I(2, 2, 0));
    StartBattle(session);
    Assert.True(GetValue(Query(session, new IsUnitVisibleToUnit(observer.State, target.State))));
  }

  [TestCase(TestName = "Vision stat changes which targets are visible")]
  public void VisionStatChangesWhichTargetsAreVisible()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 3), [playerFaction, enemyFaction]);
    var shortSighted = SpawnUnit(session, BattleTestFactory.MakeCombatant("Short", playerFaction, vision: 2), new Vector3I(0, 0, 0));
    var longSighted = SpawnUnit(session, BattleTestFactory.MakeCombatant("Long", playerFaction, vision: 3), new Vector3I(0, 0, 1));
    var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Target", enemyFaction, vision: 1), new Vector3I(2, 0, 2));

    StartBattle(session);

    Assert.False(GetValue(Query(session, new IsUnitVisibleToUnit(shortSighted.State, target.State))));
    Assert.True(GetValue(Query(session, new IsUnitVisibleToUnit(longSighted.State, target.State))));
  }

  [TestCase(TestName = "Faction visible tiles are the union of all living allies")]
  public void FactionVisibleTilesAreTheUnionOfAllLivingAllies()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [playerFaction]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", playerFaction, vision: 1), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Bravo", playerFaction, vision: 1), new Vector3I(4, 0, 4));

    StartBattle(session);

    Assert.True(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, ValidateTile(session, new Vector3I(1, 0, 0))))));
    Assert.True(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, ValidateTile(session, new Vector3I(4, 0, 3))))));
    Assert.False(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, ValidateTile(session, new Vector3I(2, 0, 2))))));
  }

  [TestCase(TestName = "Tile visibility includes tiles at the edge of vision range")]
  public void TileVisibilityIncludesTilesAtTheEdgeOfVisionRange()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(6, 1, 1), [playerFaction]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Scout", playerFaction, vision: 3), new Vector3I(0, 0, 0));

    StartBattle(session);

    Assert.True(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, ValidateTile(session, new Vector3I(3, 0, 0))))));
    Assert.False(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, ValidateTile(session, new Vector3I(4, 0, 0))))));
  }

  [TestCase(TestName = "Explored tiles persist after they leave current visibility")]
  public void ExploredTilesPersistAfterTheyLeaveCurrentVisibility()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 3), [playerFaction]);
    var observer = SpawnUnit(session, BattleTestFactory.MakeCombatant("Scout", playerFaction, vision: 2), new Vector3I(1, 0, 1));
    BattleBoardState.ValidatedPoint tile = ValidateTile(session, new Vector3I(3, 0, 1));

    StartBattle(session);
    Assert.True(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, tile))));

    var moveExecutor = new BattleActionExecutor(session);
    var moveResult = moveExecutor.Submit(BattleAction.MoveUnit(observer.State, [new Vector3I(0, 0, 1)])).RequireSingleResult();
    Assert.True(moveResult.Succeeded);

    Assert.False(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, tile))));
    Assert.True(GetValue(Query(session, new HasFactionExploredTile(playerFaction, tile))));
  }

  [TestCase(TestName = "Enemy units drop from faction visibility when sight is broken")]
  public void EnemyUnitsDropFromFactionVisibilityWhenSightIsBroken()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 1), [playerFaction, enemyFaction]);
    var observer = SpawnUnit(session, BattleTestFactory.MakeCombatant("Scout", playerFaction, vision: 3), new Vector3I(1, 0, 0));
    var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Target", enemyFaction, vision: 1), new Vector3I(4, 0, 0));

    StartBattle(session);
    Assert.True(GetValue(Query(session, new IsUnitVisibleToFaction(playerFaction, target.State))));

    var moveExecutor = new BattleActionExecutor(session);
    var moveResult = moveExecutor.Submit(BattleAction.MoveUnit(observer.State, [new Vector3I(0, 0, 0)])).RequireSingleResult();
    Assert.True(moveResult.Succeeded);

    Assert.False(GetValue(Query(session, new IsUnitVisibleToFaction(playerFaction, target.State))));
  }

  [TestCase(TestName = "Own units remain known to their faction without direct line of sight")]
  public void OwnUnitsRemainKnownToTheirFactionWithoutDirectLineOfSight()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 1), [playerFaction]);
    var alpha = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", playerFaction, vision: 1), new Vector3I(0, 0, 0));
    var bravo = SpawnUnit(session, BattleTestFactory.MakeCombatant("Bravo", playerFaction, vision: 1), new Vector3I(4, 0, 0));

    StartBattle(session);

    Assert.False(GetValue(Query(session, new IsUnitVisibleToUnit(alpha.State, bravo.State))));
    Assert.True(GetValue(Query(session, new IsUnitVisibleToFaction(playerFaction, bravo.State))));
    Assert.Equal(2, GetValue(Query(session, new GetVisibleUnitsForFaction(playerFaction))).Count);
  }

  [TestCase(TestName = "SpawnUnit refreshes visibility caches")]
  public void SpawnUnitRefreshesVisibilityCaches()
  {
    var (session, playerFaction, _, _, target) = TwoUnitScenario(new Vector3I(4, 1, 1), new Vector3I(0, 0, 0), 4, new Vector3I(2, 0, 0));

    Assert.True(GetValue(Query(session, new IsUnitVisibleToFaction(playerFaction, target.State))));
  }

  [TestCase(TestName = "StartBattle refreshes visibility caches")]
  public void StartBattleRefreshesVisibilityCaches()
  {
    var (session, _, _, observer, target) = TwoUnitScenario(new Vector3I(4, 1, 1), new Vector3I(0, 0, 0), 4, new Vector3I(2, 0, 0));

    Assert.True(GetValue(Query(session, new IsUnitVisibleToUnit(observer.State, target.State))));
    session.Board.GetTile(session.Board.ValidatePoint(new Vector3I(1, 0, 0)).RequireSome()).BlocksLineOfSight = true;

    StartBattle(session);

    Assert.False(GetValue(Query(session, new IsUnitVisibleToUnit(observer.State, target.State))));
  }

  [TestCase(TestName = "Lethal damage refreshes visibility caches")]
  public void LethalDamageRefreshesVisibilityCaches()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [playerFaction, enemyFaction]);
    var observer = SpawnUnit(session, BattleTestFactory.MakeCombatant("Observer", playerFaction, health: 10, vision: 4), new Vector3I(0, 0, 0));
    var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Target", enemyFaction, health: 10, vision: 1), new Vector3I(2, 0, 0));

    StartBattle(session);
    Assert.True(GetValue(Query(session, new IsUnitVisibleToFaction(playerFaction, target.State))));

    var damageExecutor = new BattleActionExecutor(session);
    var damageResult = damageExecutor.Submit(BattleAction.ApplyDamage(observer.State, 10)).RequireSingleResult();
    Assert.True(damageResult.Succeeded);

    Assert.False(GetValue(Query(session, new IsUnitVisibleToFaction(playerFaction, target.State))));
    Assert.Equal(0, GetValue(Query(session, new GetVisibleUnitsForFaction(playerFaction))).Count);
  }

  [TestCase(TestName = "InvalidateVisibility forces recompute after runtime tile LOS change")]
  public void InvalidateVisibilityForcesRecomputeAfterRuntimeTileLosChange()
  {
    // Board: 5 wide × 1 tall × 3 deep. Observer and target share z=0; mover sits at z=2 so
    // its step stays on the observer's side and cannot restore the observer→target line.
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 3), [playerFaction, enemyFaction]);
    var observer = SpawnUnit(session, BattleTestFactory.MakeCombatant("Observer", playerFaction, vision: 5), new Vector3I(0, 0, 0));
    var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Target", enemyFaction, vision: 1), new Vector3I(3, 0, 0));
    var mover = SpawnUnit(session, BattleTestFactory.MakeCombatant("Mover", playerFaction, vision: 1), new Vector3I(0, 0, 2));

    StartBattle(session);
    Assert.True(GetValue(Query(session, new IsUnitVisibleToFaction(playerFaction, target.State))));

    // Seal the entire x=1 column at runtime so sight cannot reach the target by any route.
    session.Board.GetTile(ValidateTile(session, new Vector3I(1, 0, 0))).BlocksLineOfSight = true;
    session.Board.GetTile(ValidateTile(session, new Vector3I(1, 0, 1))).BlocksLineOfSight = true;
    session.Board.GetTile(ValidateTile(session, new Vector3I(1, 0, 2))).BlocksLineOfSight = true;
    session.InvalidateVisibility();

    var moveResult = new BattleActionExecutor(session).Submit(BattleAction.MoveUnit(mover.State, [new Vector3I(0, 0, 1)])).RequireSingleResult();
    Assert.True(moveResult.Succeeded);

    Assert.False(GetValue(Query(session, new IsUnitVisibleToFaction(playerFaction, target.State))));
  }

  // 3D Euclidean range check includes Y: a target one level up within vision² by 3D distance
  // is visible, while one that exceeds the 3D distance is not — even when the XZ distance alone
  // would admit it on a same-level check.
  [TestCase(TestName = "3D Euclidean range includes vertical distance")]
  public void ThreeDEuclideanRangeIncludesVerticalDistance()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    // Board: 3 wide × 2 tall × 1 deep. Observer at (0,0,0) vision 2 (radius 2, range² = 4).
    // Enemy A at (2,0,0): distance² = 4 ≤ 4 → within range.
    // Enemy B at (2,1,0): distance² = 4+1 = 5 > 4 → outside range (Y term is decisive).
    var session = BattleTestFactory.MakeSession(new Vector3I(3, 2, 1), [playerFaction, enemyFaction]);
    var observer = SpawnUnit(session, BattleTestFactory.MakeCombatant("Observer", playerFaction, vision: 2), new Vector3I(0, 0, 0));
    var enemyA = SpawnUnit(session, BattleTestFactory.MakeCombatant("EnemyA", enemyFaction, vision: 1), new Vector3I(2, 0, 0));
    var enemyB = SpawnUnit(session, BattleTestFactory.MakeCombatant("EnemyB", enemyFaction, vision: 1), new Vector3I(2, 1, 0));

    StartBattle(session);

    // Enemy A is at exactly the vision boundary in XZ (distance² == vision²): visible.
    Assert.True(GetValue(Query(session, new IsUnitVisibleToUnit(observer.State, enemyA.State))));
    // Enemy B is one step above A: the added Y distance pushes it beyond vision²: not visible.
    Assert.False(GetValue(Query(session, new IsUnitVisibleToUnit(observer.State, enemyB.State))));
  }

  // UnitSpottedBattleEvent fires exactly once for a unit that newly enters the observer's sight,
  // and does not fire for a unit that was already visible before the move.
  [TestCase(TestName = "UnitSpottedBattleEvent fires when a unit moves into sight")]
  public void UnitSpottedBattleEventFiresWhenUnitMovesIntoSight()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    // Board: 6 wide × 1 tall × 1 deep. Observer (player) starts at (2,0,0) vision 2.
    // Enemy A at (0,0,0): distance 2 ≤ 2 → already visible at battle start.
    // Enemy B at (5,0,0): distance 3 > 2 → not visible at battle start.
    // Move observer one step right to (3,0,0):
    //   Enemy A at (0,0,0): distance 3 > 2 → drops out of sight (no spotted event).
    //   Enemy B at (5,0,0): distance 2 ≤ 2 → enters sight → UnitSpottedBattleEvent fires.
    var session = BattleTestFactory.MakeSession(new Vector3I(6, 1, 1), [playerFaction, enemyFaction]);
    var observer = SpawnUnit(session, BattleTestFactory.MakeCombatant("Observer", playerFaction, vision: 2), new Vector3I(2, 0, 0));
    var enemyA = SpawnUnit(session, BattleTestFactory.MakeCombatant("EnemyA", enemyFaction, vision: 1), new Vector3I(0, 0, 0));
    var enemyB = SpawnUnit(session, BattleTestFactory.MakeCombatant("EnemyB", enemyFaction, vision: 1), new Vector3I(5, 0, 0));

    StartBattle(session);
    Assert.True(GetValue(Query(session, new IsUnitVisibleToUnit(observer.State, enemyA.State))));
    Assert.False(GetValue(Query(session, new IsUnitVisibleToUnit(observer.State, enemyB.State))));

    var spottedEvents = new List<UnitSpottedBattleEvent>();
    session.BattleEventCommitted += evt =>
    {
      if (evt is UnitSpottedBattleEvent e)
        spottedEvents.Add(e);
    };

    var moveResult = new BattleActionExecutor(session)
      .Submit(BattleAction.MoveUnit(observer.State, [new Vector3I(3, 0, 0)]))
      .RequireSingleResult();
    Assert.True(moveResult.Succeeded);

    // Exactly one spotted event: observer newly sees enemy B.
    Assert.Equal(1, spottedEvents.Count(e => ReferenceEquals(e.Unit, observer.State) && ReferenceEquals(e.Target, enemyB.State)));
    // No spotted event for enemy A (it was already visible, then dropped — never newly spotted).
    Assert.Equal(0, spottedEvents.Count(e => ReferenceEquals(e.Target, enemyA.State)));
  }

  // A unit that leaves and re-enters an observer's line of sight is spotted only ONCE: the observer
  // remembers it, so UnitSpottedBattleEvent fires on the first spotting and never again.
  [TestCase(TestName = "UnitSpottedBattleEvent fires only the first time a unit is spotted")]
  public void UnitSpottedBattleEventFiresOnlyOnFirstSpotting()
  {
    // Board: 6 wide × 1 tall × 1 deep. Observer (player) vision 2 starts at (0,0,0); the target
    // (enemy) is stationary at (3,0,0), distance 3 > 2 → NOT visible at battle start (so there is
    // no start-time spotting before we subscribe).
    var (session, _, _, observer, target) = TwoUnitScenario(new Vector3I(6, 1, 1), new Vector3I(0, 0, 0), 2, new Vector3I(3, 0, 0));

    StartBattle(session);
    Assert.False(GetValue(Query(session, new IsUnitVisibleToUnit(observer.State, target.State))));

    var spottings = new List<UnitSpottedBattleEvent>();
    session.BattleEventCommitted += evt =>
    {
      if (evt is UnitSpottedBattleEvent e
          && ReferenceEquals(e.Unit, observer.State)
          && ReferenceEquals(e.Target, target.State))
        spottings.Add(e);
    };

    var executor = new BattleActionExecutor(session);

    // Step into sight: (0,0,0) → (1,0,0), distance 2 ≤ 2 → first spotting fires.
    Assert.True(executor.Submit(BattleAction.MoveUnit(observer.State, [new Vector3I(1, 0, 0)])).RequireSingleResult().Succeeded);
    Assert.True(GetValue(Query(session, new IsUnitVisibleToUnit(observer.State, target.State))));
    Assert.Equal(1, spottings.Count);

    // Step back out of sight: (1,0,0) → (0,0,0), distance 3 > 2 → target drops from sight.
    Assert.True(executor.Submit(BattleAction.MoveUnit(observer.State, [new Vector3I(0, 0, 0)])).RequireSingleResult().Succeeded);
    Assert.False(GetValue(Query(session, new IsUnitVisibleToUnit(observer.State, target.State))));

    // Step back into sight: (0,0,0) → (1,0,0). Re-entering sight does not re-fire; the observer
    // remembers the target, so the count stays at one.
    Assert.True(executor.Submit(BattleAction.MoveUnit(observer.State, [new Vector3I(1, 0, 0)])).RequireSingleResult().Succeeded);
    Assert.True(GetValue(Query(session, new IsUnitVisibleToUnit(observer.State, target.State))));

    Assert.Equal(1, spottings.Count);
  }

  private static BattleBoardState.ValidatedPoint ValidateTile(BattleSession session, Vector3I tile)
  {
    return session.Board.ValidatePoint(tile).RequireSome();
  }

  private static (BattleSession Session, Faction Player, Faction Enemy, BattleTestUnit Observer, BattleTestUnit Target)
    TwoUnitScenario(Vector3I dims, Vector3I observerPos, int observerVision, Vector3I targetPos, int targetVision = 1)
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(dims, [player, enemy]);
    var observer = SpawnUnit(session, BattleTestFactory.MakeCombatant("Observer", player, vision: observerVision), observerPos);
    var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Target", enemy, vision: targetVision), targetPos);
    return (session, player, enemy, observer, target);
  }
}
