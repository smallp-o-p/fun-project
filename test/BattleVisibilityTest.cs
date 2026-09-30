using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Weapons;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public class BattleVisibilityTest
{
  [TestCase(false, TestName = "Open space visibility succeeds between units and caches register them")]
  [TestCase(true, TestName = "Blocking tiles break line of sight and drop the target from the caches")]
  public void LineOfSightAndVisibilityCaches(bool blocked)
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(4, 1, 1), [playerFaction, enemyFaction]);
    var observer = battle.Spawn(TestData.MakeCombatant("Observer", playerFaction, vision: 4), new Vector3I(0, 0, 0));
    var target = battle.Spawn(TestData.MakeCombatant("Target", enemyFaction, vision: 1), new Vector3I(2, 0, 0));
    if (blocked)
      battle.Board.GetTile(battle.At(1, 0, 0)).BlocksLineOfSight = true;

    battle.Start();

    Assert.Equal(!blocked, battle.Query(new IsUnitVisibleToUnit(battle.Alive(observer), battle.Alive(target))));
    Assert.Equal(!blocked, battle.Query(new IsUnitVisibleToFaction(playerFaction, battle.Alive(target))));
    Assert.Equal(!blocked, observer.VisibleUnits.Contains(target));
    Assert.Equal(!blocked, observer.VisibleTiles.Contains(battle.At(new Vector3I(2, 0, 0))));
  }

  // Verifies that a blocking tile is visible (it IS the wall you look at) but the tile directly
  // behind it on the line is shadowed, while off-axis open tiles remain visible.
  [TestCase(TestName = "Walls cast line-of-sight shadows")]
  public void WallsCastLineOfSightShadows()
  {
    var playerFaction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 2), [playerFaction]);
    battle.Spawn(TestData.MakeCombatant("Observer", playerFaction, vision: 3), new Vector3I(0, 0, 0));
    BattleBoardState.ValidatedPoint blockingTile = battle.At(new Vector3I(1, 0, 0));
    BattleBoardState.ValidatedPoint shadowedTile = battle.At(new Vector3I(2, 0, 0));
    BattleBoardState.ValidatedPoint openTile = battle.At(new Vector3I(0, 0, 1));

    battle.Board.GetTile(blockingTile).BlocksLineOfSight = true;
    battle.Start();

    // The wall tile itself is visible (endpoint rule — you see the wall you look at).
    Assert.True(battle.Query(new IsTileVisibleToFaction(playerFaction, blockingTile)));
    // The tile directly behind the wall on the straight line is in shadow.
    Assert.False(battle.Query(new IsTileVisibleToFaction(playerFaction, shadowedTile)));
    // An off-axis tile with no blocker on its LOS ray is still visible.
    Assert.True(battle.Query(new IsTileVisibleToFaction(playerFaction, openTile)));
  }

  [TestCase(TestName = "Zero vision units see only their own tile")]
  public void ZeroVisionUnitsSeeOnlyTheirOwnTile()
  {
    var playerFaction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(2, 1, 1), [playerFaction]);
    battle.Spawn(TestData.MakeCombatant("Observer", playerFaction, vision: 0), new Vector3I(0, 0, 0));
    BattleBoardState.ValidatedPoint observerTile = battle.At(new Vector3I(0, 0, 0));
    BattleBoardState.ValidatedPoint adjacentTile = battle.At(new Vector3I(1, 0, 0));

    battle.Start();

    Assert.True(battle.Query(new IsTileVisibleToFaction(playerFaction, observerTile)));
    Assert.False(battle.Query(new IsTileVisibleToFaction(playerFaction, adjacentTile)));
  }

  [TestCase(TestName = "Adjacent diagonal tiles remain visible around corners")]
  public void AdjacentDiagonalTilesRemainVisibleAroundCorners()
  {
    var playerFaction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(2, 1, 2), [playerFaction]);
    battle.Spawn(TestData.MakeCombatant("Observer", playerFaction, vision: 3), new Vector3I(0, 0, 0));
    BattleBoardState.ValidatedPoint blockingTile = battle.At(new Vector3I(1, 0, 0));
    BattleBoardState.ValidatedPoint diagonalTile = battle.At(new Vector3I(1, 0, 1));

    battle.Board.GetTile(blockingTile).BlocksLineOfSight = true;
    battle.Start();

    Assert.True(battle.Query(new IsTileVisibleToFaction(playerFaction, blockingTile)));
    Assert.True(battle.Query(new IsTileVisibleToFaction(playerFaction, diagonalTile)));
  }

  [TestCase(TestName = "Diagonal corner peeking does not reveal tiles behind blockers")]
  public void DiagonalCornerPeekingDoesNotRevealTilesBehindBlockers()
  {
    var playerFaction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(3, 1, 2), [playerFaction]);
    battle.Spawn(TestData.MakeCombatant("Observer", playerFaction, vision: 3), new Vector3I(0, 0, 0));
    BattleBoardState.ValidatedPoint firstBlockingTile = battle.At(new Vector3I(1, 0, 0));
    BattleBoardState.ValidatedPoint secondBlockingTile = battle.At(new Vector3I(1, 0, 1));
    BattleBoardState.ValidatedPoint hiddenTile = battle.At(new Vector3I(2, 0, 1));

    battle.Board.GetTile(firstBlockingTile).BlocksLineOfSight = true;
    battle.Board.GetTile(secondBlockingTile).BlocksLineOfSight = true;
    battle.Start();

    Assert.True(battle.Query(new IsTileVisibleToFaction(playerFaction, firstBlockingTile)));
    Assert.True(battle.Query(new IsTileVisibleToFaction(playerFaction, secondBlockingTile)));
    Assert.False(battle.Query(new IsTileVisibleToFaction(playerFaction, hiddenTile)));
  }

  [TestCase(TestName = "Adjacent diagonal peeking does not see through sealed corners")]
  public void AdjacentDiagonalPeekingDoesNotSeeThroughSealedCorners()
  {
    var playerFaction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(2, 1, 2), [playerFaction]);
    battle.Spawn(TestData.MakeCombatant("Observer", playerFaction, vision: 3), new Vector3I(0, 0, 0));
    BattleBoardState.ValidatedPoint firstBlockingTile = battle.At(new Vector3I(1, 0, 0));
    BattleBoardState.ValidatedPoint secondBlockingTile = battle.At(new Vector3I(0, 0, 1));
    BattleBoardState.ValidatedPoint sealedCornerTile = battle.At(new Vector3I(1, 0, 1));

    battle.Board.GetTile(firstBlockingTile).BlocksLineOfSight = true;
    battle.Board.GetTile(secondBlockingTile).BlocksLineOfSight = true;
    battle.Start();

    Assert.True(battle.Query(new IsTileVisibleToFaction(playerFaction, firstBlockingTile)));
    Assert.True(battle.Query(new IsTileVisibleToFaction(playerFaction, secondBlockingTile)));
    Assert.False(battle.Query(new IsTileVisibleToFaction(playerFaction, sealedCornerTile)));
  }

  // With no vertical blocking flag, a unit sees down an open column; BlocksVerticalLineOfSight
  // set on the observer's own cell (the floor) seals downward sight.
  [TestCase(false, TestName = "Sight passes through an open column below the observer")]
  [TestCase(true, TestName = "BlocksVerticalLineOfSight prevents seeing below when set on observer cell")]
  public void VerticalSightBelow(bool blocked)
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(1, 2, 1), [playerFaction, enemyFaction]);
    var observer = battle.Spawn(TestData.MakeCombatant("Observer", playerFaction, vision: 3), new Vector3I(0, 1, 0));
    var target = battle.Spawn(TestData.MakeCombatant("Target", enemyFaction, vision: 1), new Vector3I(0, 0, 0));
    if (blocked)
      battle.Board.GetTile(battle.At(new Vector3I(0, 1, 0))).BlocksVerticalLineOfSight = true;

    battle.Start();

    Assert.Equal(!blocked, battle.Query(new IsUnitVisibleToUnit(battle.Alive(observer), battle.Alive(target))));
    Assert.Equal(!blocked, battle.Query(new IsTileVisibleToFaction(playerFaction, battle.At(new Vector3I(0, 0, 0)))));
  }

  // With no vertical blocking flag, a unit sees up an open column; BlocksVerticalLineOfSight
  // on an intermediate floor cell seals upward sight through it.
  [TestCase(false, TestName = "Sight passes through an open column above the observer")]
  [TestCase(true, TestName = "BlocksVerticalLineOfSight on intermediate floor prevents seeing above")]
  public void VerticalSightAbove(bool blocked)
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(1, 3, 1), [playerFaction, enemyFaction]);
    var observer = battle.Spawn(TestData.MakeCombatant("Observer", playerFaction, vision: 5), new Vector3I(0, 0, 0));
    var target = battle.Spawn(TestData.MakeCombatant("Target", enemyFaction, vision: 1), new Vector3I(0, 2, 0));
    if (blocked)
      battle.Board.GetTile(battle.At(new Vector3I(0, 1, 0))).BlocksVerticalLineOfSight = true;

    battle.Start();

    Assert.Equal(!blocked, battle.Query(new IsUnitVisibleToUnit(battle.Alive(observer), battle.Alive(target))));
    Assert.Equal(!blocked, battle.Query(new IsTileVisibleToFaction(playerFaction, battle.At(new Vector3I(0, 2, 0)))));
  }

  // A 3D-diagonal ray (X and Y both change, simultaneous crossing) is sealed when the mid cell
  // has BlocksVerticalLineOfSight — exercises the Rule 3 vertical check on a diagonal step.
  // Keep separate from the axial rows: simultaneous crossing is its own rule.
  [TestCase(true, TestName = "BlocksVerticalLineOfSight seals a diagonal ray on a simultaneous crossing")]
  [TestCase(false, TestName = "A diagonal ray through an open column is clear")]
  public void DiagonalRaySealedByVerticalBlocker(bool blocked)
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(3, 3, 1), [playerFaction, enemyFaction]);
    var observer = battle.Spawn(TestData.MakeCombatant("Observer", playerFaction, vision: 3), new Vector3I(0, 0, 0));
    var target = battle.Spawn(TestData.MakeCombatant("Target", enemyFaction, vision: 1), new Vector3I(2, 2, 0));
    if (blocked)
      battle.Board.GetTile(battle.At(new Vector3I(1, 1, 0))).BlocksVerticalLineOfSight = true;

    battle.Start();

    Assert.Equal(!blocked, battle.Query(new IsUnitVisibleToUnit(battle.Alive(observer), battle.Alive(target))));
  }

  [TestCase(TestName = "Vision stat changes which targets are visible")]
  public void VisionStatChangesWhichTargetsAreVisible()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(4, 1, 3), [playerFaction, enemyFaction]);
    var shortSighted = battle.Spawn(TestData.MakeCombatant("Short", playerFaction, vision: 2), new Vector3I(0, 0, 0));
    var longSighted = battle.Spawn(TestData.MakeCombatant("Long", playerFaction, vision: 3), new Vector3I(0, 0, 1));
    var target = battle.Spawn(TestData.MakeCombatant("Target", enemyFaction, vision: 1), new Vector3I(2, 0, 2));

    battle.Start();

    Assert.False(battle.Query(new IsUnitVisibleToUnit(battle.Alive(shortSighted), battle.Alive(target))));
    Assert.True(battle.Query(new IsUnitVisibleToUnit(battle.Alive(longSighted), battle.Alive(target))));
  }

  [TestCase(TestName = "Knockout removes only its own vision contribution before broadcast")]
  public void KnockoutRemovesOnlyItsOwnVisionContributionBeforeBroadcast()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(5, 1, 3), [playerFaction, enemyFaction]);
    var observer = battle.Spawn(TestData.MakeCombatant("Observer", playerFaction, vision: 5), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("Support", playerFaction, vision: 1), new Vector3I(0, 0, 2));
    battle.Spawn(TestData.MakeCombatant("Enemy", enemyFaction, vision: 5), new Vector3I(3, 0, 0));
    battle.Start();
    var exclusiveTile = battle.At(3, 0, 0);
    var sharedTile = battle.At(0, 0, 1);
    Assert.True(battle.Query(new IsTileVisibleToFaction(playerFaction, exclusiveTile)));

    bool observedKnockout = false;
    battle.Session.BattleEventCommitted += battleEvent =>
    {
      if (battleEvent is not UnitUnconsciousBattleEvent)
        return;
      observedKnockout = true;
      Assert.False(battle.Query(new IsTileVisibleToFaction(playerFaction, exclusiveTile)));
      Assert.True(battle.Query(new IsTileVisibleToFaction(playerFaction, sharedTile)));
    };

    battle.ApplyDamage(observer, 20, DamageKind.Stun);

    Assert.True(observedKnockout);
  }

  [TestCase(TestName = "Knockout preserves explored tiles")]
  public void KnockoutPreservesExploredTiles()
  {
    using var battle = BattleFixture.Duel();
    var enemyTile = battle.Alive(battle.EnemyUnit).Position;

    battle.ApplyDamage(battle.PlayerUnit, 20, DamageKind.Stun);

    Assert.True(battle.Query(new HasFactionExploredTile(battle.PlayerFaction, enemyTile)));
  }

  [TestCase(false)]
  [TestCase(true)]
  public void UnconsciousObserverCachesStayEmptyDuringVisibilityUpdates(bool forceFullRebuild)
  {
    using var battle = BattleFixture.Duel();
    var observer = battle.PlayerUnit;
    var support = battle.Spawn(TestData.MakeCombatant("Support", battle.PlayerFaction, vision: 1), Vector3I.Zero);
    battle.ApplyDamage(observer, 20, DamageKind.Stun);
    if (forceFullRebuild)
      battle.Session.InvalidateVisibility();

    battle.Move(support, [new Vector3I(0, 0, 1)]);

    Assert.Equal(0, observer.VisibleTiles.Count);
    Assert.Equal(0, observer.VisibleUnits.Count);
  }

  [TestCase(TestName = "Faction visible tiles are the union of conscious allies")]
  public void FactionVisibleTilesAreTheUnionOfConsciousAllies()
  {
    var playerFaction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [playerFaction]);
    battle.Spawn(TestData.MakeCombatant("Alpha", playerFaction, vision: 1), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("Bravo", playerFaction, vision: 1), new Vector3I(4, 0, 4));

    battle.Start();

    Assert.True(battle.Query(new IsTileVisibleToFaction(playerFaction, battle.At(new Vector3I(1, 0, 0)))));
    Assert.True(battle.Query(new IsTileVisibleToFaction(playerFaction, battle.At(new Vector3I(4, 0, 3)))));
    Assert.False(battle.Query(new IsTileVisibleToFaction(playerFaction, battle.At(new Vector3I(2, 0, 2)))));
  }

  [TestCase(TestName = "Tile visibility includes tiles at the edge of vision range")]
  public void TileVisibilityIncludesTilesAtTheEdgeOfVisionRange()
  {
    var playerFaction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(6, 1, 1), [playerFaction]);
    battle.Spawn(TestData.MakeCombatant("Scout", playerFaction, vision: 3), new Vector3I(0, 0, 0));

    battle.Start();

    Assert.True(battle.Query(new IsTileVisibleToFaction(playerFaction, battle.At(new Vector3I(3, 0, 0)))));
    Assert.False(battle.Query(new IsTileVisibleToFaction(playerFaction, battle.At(new Vector3I(4, 0, 0)))));
  }

  [TestCase(TestName = "Explored tiles persist after they leave current visibility")]
  public void ExploredTilesPersistAfterTheyLeaveCurrentVisibility()
  {
    var playerFaction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(4, 1, 3), [playerFaction]);
    var observer = battle.Spawn(TestData.MakeCombatant("Scout", playerFaction, vision: 2), new Vector3I(1, 0, 1));
    BattleBoardState.ValidatedPoint tile = battle.At(new Vector3I(3, 0, 1));

    battle.Start();
    Assert.True(battle.Query(new IsTileVisibleToFaction(playerFaction, tile)));

    battle.Move(observer, [new Vector3I(0, 0, 1)]);

    Assert.False(battle.Query(new IsTileVisibleToFaction(playerFaction, tile)));
    Assert.True(battle.Query(new HasFactionExploredTile(playerFaction, tile)));
  }

  [TestCase(TestName = "Visible objects follow explored-tile memory for each faction")]
  public void VisibleObjectsFollowExploredTileMemory()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(6, 1, 6), [playerFaction, enemyFaction]);
    var player = battle.Spawn(TestData.MakeCombatant("Scout", playerFaction, vision: 2), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("Enemy", enemyFaction, vision: 1), new Vector3I(5, 0, 5));

    BattleBoardState.ValidatedPoint nearPoint = battle.At(new Vector3I(1, 0, 0));
    BattleBoardState.ValidatedPoint farPoint = battle.At(new Vector3I(4, 0, 4));
    var nearData = new BattleSpecialObjectData { Name = "Near Bomb" };
    nearData.Capabilities.Add(new InteractiveCapabilityData { ActionPointCost = 1 });
    var farData = new BattleSpecialObjectData { Name = "Far Bomb" };
    farData.Capabilities.Add(new InteractiveCapabilityData { ActionPointCost = 1 });

    battle.Submit(BattleAction.PlaceObject(nearData, nearPoint));
    battle.Submit(BattleAction.PlaceObject(farData, farPoint));
    BattleObjectState[] objects = [.. battle.Session.Objects];
    BattleObjectState nearObject = objects[0];

    battle.Start();

    var playerVisible = battle.Query(new GetFactionVisibleObjectsQuery(playerFaction));
    Assert.Equal(1, playerVisible.Count);
    Assert.Equal(nearObject, playerVisible[0]);
    Assert.Equal(0, battle.Query(new GetFactionVisibleObjectsQuery(enemyFaction)).Count);

    battle.Submit(BattleAction.InteractWithObject(
      battle.Alive(player),
      battle.Live(nearObject)));

    playerVisible = battle.Query(new GetFactionVisibleObjectsQuery(playerFaction));
    Assert.Equal(1, playerVisible.Count);
    Assert.Equal(Some(ObjectStatus.Interacted), playerVisible[0].Status);
  }

  [TestCase(TestName = "Enemy units drop from faction visibility when sight is broken")]
  public void EnemyUnitsDropFromFactionVisibilityWhenSightIsBroken()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(5, 1, 1), [playerFaction, enemyFaction]);
    var observer = battle.Spawn(TestData.MakeCombatant("Scout", playerFaction, vision: 3), new Vector3I(1, 0, 0));
    var target = battle.Spawn(TestData.MakeCombatant("Target", enemyFaction, vision: 1), new Vector3I(4, 0, 0));

    battle.Start();
    Assert.True(battle.Query(new IsUnitVisibleToFaction(playerFaction, battle.Alive(target))));

    battle.Move(observer, [new Vector3I(0, 0, 0)]);

    Assert.False(battle.Query(new IsUnitVisibleToFaction(playerFaction, battle.Alive(target))));
  }

  [TestCase(TestName = "Own units remain known to their faction without direct line of sight")]
  public void OwnUnitsRemainKnownToTheirFactionWithoutDirectLineOfSight()
  {
    var playerFaction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new Vector3I(5, 1, 1), [playerFaction]);
    var alpha = battle.Spawn(TestData.MakeCombatant("Alpha", playerFaction, vision: 1), new Vector3I(0, 0, 0));
    var bravo = battle.Spawn(TestData.MakeCombatant("Bravo", playerFaction, vision: 1), new Vector3I(4, 0, 0));

    battle.Start();

    Assert.False(battle.Query(new IsUnitVisibleToUnit(battle.Alive(alpha), battle.Alive(bravo))));
    Assert.True(battle.Query(new IsUnitVisibleToFaction(playerFaction, battle.Alive(bravo))));
    Assert.Equal(2, battle.Query(new GetVisibleUnitsForFaction(playerFaction)).Count);
  }

  [TestCase(TestName = "SpawnUnit refreshes visibility caches")]
  public void SpawnUnitRefreshesVisibilityCaches()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(4, 1, 1), [playerFaction, enemyFaction]);
    battle.Spawn(TestData.MakeCombatant("Observer", playerFaction, vision: 4), new Vector3I(0, 0, 0));
    var target = battle.Spawn(TestData.MakeCombatant("Target", enemyFaction, vision: 1), new Vector3I(2, 0, 0));

    Assert.True(battle.Query(new IsUnitVisibleToFaction(playerFaction, battle.Alive(target))));
  }

  [TestCase(TestName = "StartBattle refreshes visibility caches")]
  public void StartBattleRefreshesVisibilityCaches()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(4, 1, 1), [playerFaction, enemyFaction]);
    var observer = battle.Spawn(TestData.MakeCombatant("Observer", playerFaction, vision: 4), new Vector3I(0, 0, 0));
    var target = battle.Spawn(TestData.MakeCombatant("Target", enemyFaction, vision: 1), new Vector3I(2, 0, 0));

    Assert.True(battle.Query(new IsUnitVisibleToUnit(battle.Alive(observer), battle.Alive(target))));
    battle.Board.GetTile(battle.At(1, 0, 0)).BlocksLineOfSight = true;

    battle.Start();

    Assert.False(battle.Query(new IsUnitVisibleToUnit(battle.Alive(observer), battle.Alive(target))));
  }

  [TestCase(TestName = "Lethal damage refreshes visibility caches")]
  public void LethalDamageRefreshesVisibilityCaches()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(4, 1, 1), [playerFaction, enemyFaction]);
    var observer = battle.Spawn(TestData.MakeCombatant("Observer", playerFaction, health: 10, vision: 4), new Vector3I(0, 0, 0));
    var target = battle.Spawn(TestData.MakeCombatant("Target", enemyFaction, health: 10, vision: 1), new Vector3I(2, 0, 0));

    battle.Start();
    Assert.True(battle.Query(new IsUnitVisibleToFaction(playerFaction, battle.Alive(target))));

    battle.ApplyDamage(observer, 10);

    Assert.False(battle.Query(new IsUnitVisibleToFaction(playerFaction, battle.Alive(target))));
    Assert.Equal(0, battle.Query(new GetVisibleUnitsForFaction(playerFaction)).Count);
  }

  [TestCase(TestName = "InvalidateVisibility forces recompute after runtime tile LOS change")]
  public void InvalidateVisibilityForcesRecomputeAfterRuntimeTileLosChange()
  {
    // Board: 5 wide × 1 tall × 3 deep. Observer and target share z=0; mover sits at z=2 so
    // its step stays on the observer's side and cannot restore the observer→target line.
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(5, 1, 3), [playerFaction, enemyFaction]);
    battle.Spawn(TestData.MakeCombatant("Observer", playerFaction, vision: 5), new Vector3I(0, 0, 0));
    var target = battle.Spawn(TestData.MakeCombatant("Target", enemyFaction, vision: 1), new Vector3I(3, 0, 0));
    var mover = battle.Spawn(TestData.MakeCombatant("Mover", playerFaction, vision: 1), new Vector3I(0, 0, 2));

    battle.Start();
    Assert.True(battle.Query(new IsUnitVisibleToFaction(playerFaction, battle.Alive(target))));

    // Seal the entire x=1 column at runtime so sight cannot reach the target by any route.
    battle.Board.GetTile(battle.At(new Vector3I(1, 0, 0))).BlocksLineOfSight = true;
    battle.Board.GetTile(battle.At(new Vector3I(1, 0, 1))).BlocksLineOfSight = true;
    battle.Board.GetTile(battle.At(new Vector3I(1, 0, 2))).BlocksLineOfSight = true;
    battle.Session.InvalidateVisibility();

    battle.Move(mover, [new Vector3I(0, 0, 1)]);

    Assert.False(battle.Query(new IsUnitVisibleToFaction(playerFaction, battle.Alive(target))));
  }

  // 3D Euclidean range check includes Y: a target one level up within vision² by 3D distance
  // is visible, while one that exceeds the 3D distance is not — even when the XZ distance alone
  // would admit it on a same-level check.
  [TestCase(TestName = "3D Euclidean range includes vertical distance")]
  public void ThreeDEuclideanRangeIncludesVerticalDistance()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    // Board: 3 wide × 2 tall × 1 deep. Observer at (0,0,0) vision 2 (radius 2, range² = 4).
    // Enemy A at (2,0,0): distance² = 4 ≤ 4 → within range.
    // Enemy B at (2,1,0): distance² = 4+1 = 5 > 4 → outside range (Y term is decisive).
    using var battle = new BattleFixture(new Vector3I(3, 2, 1), [playerFaction, enemyFaction]);
    var observer = battle.Spawn(TestData.MakeCombatant("Observer", playerFaction, vision: 2), new Vector3I(0, 0, 0));
    var enemyA = battle.Spawn(TestData.MakeCombatant("EnemyA", enemyFaction, vision: 1), new Vector3I(2, 0, 0));
    var enemyB = battle.Spawn(TestData.MakeCombatant("EnemyB", enemyFaction, vision: 1), new Vector3I(2, 1, 0));

    battle.Start();

    // Enemy A is at exactly the vision boundary in XZ (distance² == vision²): visible.
    Assert.True(battle.Query(new IsUnitVisibleToUnit(battle.Alive(observer), battle.Alive(enemyA))));
    // Enemy B is one step above A: the added Y distance pushes it beyond vision²: not visible.
    Assert.False(battle.Query(new IsUnitVisibleToUnit(battle.Alive(observer), battle.Alive(enemyB))));
  }

  // UnitSpottedBattleEvent fires exactly once for a unit that newly enters the observer's sight,
  // and does not fire for a unit that was already visible before the move.
  [TestCase(TestName = "UnitSpottedBattleEvent fires when a unit moves into sight")]
  public void UnitSpottedBattleEventFiresWhenUnitMovesIntoSight()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    // Board: 6 wide × 1 tall × 1 deep. Observer (player) starts at (2,0,0) vision 2.
    // Enemy A at (0,0,0): distance 2 ≤ 2 → already visible at battle start.
    // Enemy B at (5,0,0): distance 3 > 2 → not visible at battle start.
    // Move observer one step right to (3,0,0):
    //   Enemy A at (0,0,0): distance 3 > 2 → drops out of sight (no spotted event).
    //   Enemy B at (5,0,0): distance 2 ≤ 2 → enters sight → UnitSpottedBattleEvent fires.
    using var battle = new BattleFixture(new Vector3I(6, 1, 1), [playerFaction, enemyFaction]);
    var observer = battle.Spawn(TestData.MakeCombatant("Observer", playerFaction, vision: 2), new Vector3I(2, 0, 0));
    var enemyA = battle.Spawn(TestData.MakeCombatant("EnemyA", enemyFaction, vision: 1), new Vector3I(0, 0, 0));
    var enemyB = battle.Spawn(TestData.MakeCombatant("EnemyB", enemyFaction, vision: 1), new Vector3I(5, 0, 0));

    battle.Start();
    Assert.True(battle.Query(new IsUnitVisibleToUnit(battle.Alive(observer), battle.Alive(enemyA))));
    Assert.False(battle.Query(new IsUnitVisibleToUnit(battle.Alive(observer), battle.Alive(enemyB))));

    battle.ClearEvents();
    battle.Move(observer, [new Vector3I(3, 0, 0)]);

    // Exactly one spotted event: observer newly sees enemy B.
    Assert.Equal(1, battle.Events.EventsOf<UnitSpottedBattleEvent>().AsValueEnumerable().Count(e => ReferenceEquals(e.Unit, observer) && ReferenceEquals(e.Target, enemyB)));
    // No spotted event for enemy A (it was already visible, then dropped — never newly spotted).
    Assert.Equal(0, battle.Events.EventsOf<UnitSpottedBattleEvent>().AsValueEnumerable().Count(e => ReferenceEquals(e.Target, enemyA)));
  }

  // A unit that leaves and re-enters an observer's line of sight is spotted only ONCE: the observer
  // remembers it, so UnitSpottedBattleEvent fires on the first spotting and never again.
  [TestCase(TestName = "UnitSpottedBattleEvent fires only the first time a unit is spotted")]
  public void UnitSpottedBattleEventFiresOnlyOnFirstSpotting()
  {
    // Board: 6 wide × 1 tall × 1 deep. Observer (player) vision 2 starts at (0,0,0); the target
    // (enemy) is stationary at (3,0,0), distance 3 > 2 → NOT visible at battle start (so there is
    // no start-time spotting before the recording window opens).
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(6, 1, 1), [playerFaction, enemyFaction]);
    var observer = battle.Spawn(TestData.MakeCombatant("Observer", playerFaction, vision: 2), new Vector3I(0, 0, 0));
    var target = battle.Spawn(TestData.MakeCombatant("Target", enemyFaction, vision: 1), new Vector3I(3, 0, 0));

    battle.Start();
    Assert.False(battle.Query(new IsUnitVisibleToUnit(battle.Alive(observer), battle.Alive(target))));

    battle.ClearEvents();

    // Step into sight: (0,0,0) → (1,0,0), distance 2 ≤ 2 → first spotting fires.
    battle.Move(observer, [new Vector3I(1, 0, 0)]);
    Assert.True(battle.Query(new IsUnitVisibleToUnit(battle.Alive(observer), battle.Alive(target))));
    Assert.Equal(1, MatchingSpottings(battle, observer, target));

    // Step back out of sight: (1,0,0) → (0,0,0), distance 3 > 2 → target drops from sight.
    battle.Move(observer, [new Vector3I(0, 0, 0)]);
    Assert.False(battle.Query(new IsUnitVisibleToUnit(battle.Alive(observer), battle.Alive(target))));

    // Step back into sight: (0,0,0) → (1,0,0). Re-entering sight does not re-fire; the observer
    // remembers the target, so the count stays at one.
    battle.Move(observer, [new Vector3I(1, 0, 0)]);
    Assert.True(battle.Query(new IsUnitVisibleToUnit(battle.Alive(observer), battle.Alive(target))));

    Assert.Equal(1, MatchingSpottings(battle, observer, target));
  }

  // Mirror of the spot-on-move tests with the roles reversed: the TARGET moves while the
  // observer stays put. The observer is unaffected by the move, so only its visible-UNIT
  // membership is rebuilt (its visible-tile set is invariant) — that rebuild must replace
  // stale members, not merely add new ones.
  [TestCase(TestName = "Target moving out of sight drops from the stationary observer")]
  public void TargetMovingOutOfSightDropsFromStationaryObserver()
  {
    // Board: 6 wide × 1 tall × 1 deep. Enemy observer (vision 2) stationary at (4,0,0);
    // player target starts at (3,0,0), distance 1 ≤ 2 → visible at battle start.
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(6, 1, 1), [playerFaction, enemyFaction]);
    var observer = battle.Spawn(TestData.MakeCombatant("Observer", enemyFaction, vision: 2), new Vector3I(4, 0, 0));
    var target = battle.Spawn(TestData.MakeCombatant("Target", playerFaction, vision: 1), new Vector3I(3, 0, 0));

    battle.Start();
    Assert.True(battle.Query(new IsUnitVisibleToUnit(battle.Alive(observer), battle.Alive(target))));

    // Still within sight at distance 2, then out of sight at distance 3.
    battle.Move(target, [new Vector3I(2, 0, 0)]);
    Assert.True(battle.Query(new IsUnitVisibleToUnit(battle.Alive(observer), battle.Alive(target))));
    battle.Move(target, [new Vector3I(1, 0, 0)]);
    Assert.False(battle.Query(new IsUnitVisibleToUnit(battle.Alive(observer), battle.Alive(target))));

    // Returning to distance 2 makes the target visible to the observer again.
    battle.Move(target, [new Vector3I(2, 0, 0)]);
    Assert.True(battle.Query(new IsUnitVisibleToUnit(battle.Alive(observer), battle.Alive(target))));
  }

  private static int MatchingSpottings(BattleFixture battle, BattleUnitState observer, BattleUnitState target) =>
    battle.Events.EventsOf<UnitSpottedBattleEvent>()
      .AsValueEnumerable()
      .Count(e => ReferenceEquals(e.Unit, observer) && ReferenceEquals(e.Target, target));
}
