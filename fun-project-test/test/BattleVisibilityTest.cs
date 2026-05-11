using FunProject.Battle;
using FunProject.Tests;
using GdUnit4;
using Godot;
using System.Linq;
using static BattleActionTestHelper;
using static BattleQueryTestHelper;

[TestSuite]
[RequireGodotRuntime]
public class BattleVisibilityTest
{
  [TestCase(TestName = "Open space visibility succeeds between units")]
  public void OpenSpaceVisibilitySucceedsBetweenUnits()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [playerFaction, enemyFaction]);
    var observer = SpawnUnit(session, BattleTestFactory.MakeCombatant("Observer", playerFaction, vision: 4), new Vector3I(0, 0, 0));
    var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Target", enemyFaction, vision: 1), new Vector3I(2, 0, 0));

    StartBattle(session);

    Assert.True(GetValue(Query(session, new IsUnitVisibleToUnit(observer.Handle, target.Handle))));
    Assert.True(GetValue(Query(session, new IsUnitVisibleToFaction(playerFaction, target.Handle))));
  }

  [TestCase(TestName = "Blocking tiles break line of sight")]
  public void BlockingTilesBreakLineOfSight()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [playerFaction, enemyFaction]);
    var observer = SpawnUnit(session, BattleTestFactory.MakeCombatant("Observer", playerFaction, vision: 4), new Vector3I(0, 0, 0));
    var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Target", enemyFaction, vision: 1), new Vector3I(2, 0, 0));

    session.Board.GetTile(session.Board.ValidatePoint(new Vector3I(1, 0, 0)).RequireSome()).BlocksLineOfSight = true;
    StartBattle(session);

    Assert.False(GetValue(Query(session, new IsUnitVisibleToUnit(observer.Handle, target.Handle))));
    Assert.False(GetValue(Query(session, new IsUnitVisibleToFaction(playerFaction, target.Handle))));
  }

  [TestCase(TestName = "Vertical line of sight works across levels")]
  public void VerticalLineOfSightWorksAcrossLevels()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(1, 3, 1), [playerFaction, enemyFaction]);
    var observer = SpawnUnit(session, BattleTestFactory.MakeCombatant("Observer", playerFaction, vision: 5), new Vector3I(0, 0, 0));
    var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Target", enemyFaction, vision: 1), new Vector3I(0, 2, 0));

    StartBattle(session);

    Assert.True(GetValue(Query(session, new IsUnitVisibleToUnit(observer.Handle, target.Handle))));
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

    Assert.False(GetValue(Query(session, new IsUnitVisibleToUnit(shortSighted.Handle, target.Handle))));
    Assert.True(GetValue(Query(session, new IsUnitVisibleToUnit(longSighted.Handle, target.Handle))));
  }

  [TestCase(TestName = "Faction visible tiles are the union of all living allies")]
  public void FactionVisibleTilesAreTheUnionOfAllLivingAllies()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [playerFaction]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", playerFaction, vision: 1), new Vector3I(0, 0, 0));
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Bravo", playerFaction, vision: 1), new Vector3I(4, 0, 4));

    StartBattle(session);

    Assert.True(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, new Vector3I(1, 0, 0)))));
    Assert.True(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, new Vector3I(4, 0, 3)))));
    Assert.False(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, new Vector3I(2, 0, 2)))));
  }

  [TestCase(TestName = "Tile visibility includes tiles at the edge of vision range")]
  public void TileVisibilityIncludesTilesAtTheEdgeOfVisionRange()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(6, 1, 1), [playerFaction]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Scout", playerFaction, vision: 3), new Vector3I(0, 0, 0));

    StartBattle(session);

    Assert.True(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, new Vector3I(3, 0, 0)))));
    Assert.False(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, new Vector3I(4, 0, 0)))));
  }

  [TestCase(TestName = "Explored tiles persist after they leave current visibility")]
  public void ExploredTilesPersistAfterTheyLeaveCurrentVisibility()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 3), [playerFaction]);
    var observer = SpawnUnit(session, BattleTestFactory.MakeCombatant("Scout", playerFaction, vision: 2), new Vector3I(1, 0, 1));
    var tile = new Vector3I(3, 0, 1);

    StartBattle(session);
    Assert.True(GetValue(Query(session, new IsTileVisibleToFaction(playerFaction, tile))));

    var moveExecutor = new BattleActionExecutor(session);
    var moveResult = moveExecutor.Submit(BattleAction.MoveUnitStep(observer.Handle, new Vector3I(0, 0, 1))).RequireSingleResult();
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
    Assert.True(GetValue(Query(session, new IsUnitVisibleToFaction(playerFaction, target.Handle))));

    var moveExecutor = new BattleActionExecutor(session);
    var moveResult = moveExecutor.Submit(BattleAction.MoveUnitStep(observer.Handle, new Vector3I(0, 0, 0))).RequireSingleResult();
    Assert.True(moveResult.Succeeded);

    Assert.False(GetValue(Query(session, new IsUnitVisibleToFaction(playerFaction, target.Handle))));
  }

  [TestCase(TestName = "Own units remain known to their faction without direct line of sight")]
  public void OwnUnitsRemainKnownToTheirFactionWithoutDirectLineOfSight()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 1), [playerFaction]);
    var alpha = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", playerFaction, vision: 1), new Vector3I(0, 0, 0));
    var bravo = SpawnUnit(session, BattleTestFactory.MakeCombatant("Bravo", playerFaction, vision: 1), new Vector3I(4, 0, 0));

    StartBattle(session);

    Assert.False(GetValue(Query(session, new IsUnitVisibleToUnit(alpha.Handle, bravo.Handle))));
    Assert.True(GetValue(Query(session, new IsUnitVisibleToFaction(playerFaction, bravo.Handle))));
    Assert.Equal(2, GetValue(Query(session, new GetVisibleUnitsForFaction(playerFaction))).Count);
  }

  [TestCase(TestName = "SpawnUnit refreshes visibility caches")]
  public void SpawnUnitRefreshesVisibilityCaches()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [playerFaction, enemyFaction]);
    SpawnUnit(session, BattleTestFactory.MakeCombatant("Observer", playerFaction, vision: 4), new Vector3I(0, 0, 0));

    var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Target", enemyFaction, vision: 1), new Vector3I(2, 0, 0));

    Assert.True(GetValue(Query(session, new IsUnitVisibleToFaction(playerFaction, target.Handle))));
  }

  [TestCase(TestName = "StartBattle refreshes visibility caches")]
  public void StartBattleRefreshesVisibilityCaches()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [playerFaction, enemyFaction]);
    var observer = SpawnUnit(session, BattleTestFactory.MakeCombatant("Observer", playerFaction, vision: 4), new Vector3I(0, 0, 0));
    var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Target", enemyFaction, vision: 1), new Vector3I(2, 0, 0));

    Assert.True(GetValue(Query(session, new IsUnitVisibleToUnit(observer.Handle, target.Handle))));
    session.Board.GetTile(session.Board.ValidatePoint(new Vector3I(1, 0, 0)).RequireSome()).BlocksLineOfSight = true;

    StartBattle(session);

    Assert.False(GetValue(Query(session, new IsUnitVisibleToUnit(observer.Handle, target.Handle))));
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
    Assert.True(GetValue(Query(session, new IsUnitVisibleToFaction(playerFaction, target.Handle))));

    var damageExecutor = new BattleActionExecutor(session);
    var damageResult = damageExecutor.Submit(BattleAction.ApplyDamage(observer.Handle, 10)).RequireSingleResult();
    Assert.True(damageResult.Succeeded);

    Assert.False(GetValue(Query(session, new IsUnitVisibleToFaction(playerFaction, target.Handle))));
    Assert.Equal(0, GetValue(Query(session, new GetVisibleUnitsForFaction(playerFaction))).Count);
  }

}
