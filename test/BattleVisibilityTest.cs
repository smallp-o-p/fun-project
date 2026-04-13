#nullable enable
using FunProject.Battle;
using FunProject.Combatants;
using Godot;
using System.Linq;

public partial class BattleVisibilityTest : TestRunner
{
  public override void _Ready()
  {
    T("Open space visibility succeeds between units", () =>
    {
      var playerFaction = BattleTestFactory.MakeFaction("Player");
      var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
      var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [playerFaction, enemyFaction]);
      var observer = SpawnUnit(session, BattleTestFactory.MakeCombatant("Observer", playerFaction, vision: 4), new Vector3I(0, 0, 0));
      var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Target", enemyFaction, vision: 1), new Vector3I(2, 0, 0));

      StartBattle(session);

      Assert.True(session.IsUnitVisibleToUnit(observer.UnitId, target.UnitId));
      Assert.True(session.IsUnitVisibleToFaction(playerFaction, target.UnitId));
    });

    T("Blocking tiles break line of sight", () =>
    {
      var playerFaction = BattleTestFactory.MakeFaction("Player");
      var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
      var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [playerFaction, enemyFaction]);
      var observer = SpawnUnit(session, BattleTestFactory.MakeCombatant("Observer", playerFaction, vision: 4), new Vector3I(0, 0, 0));
      var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Target", enemyFaction, vision: 1), new Vector3I(2, 0, 0));

      session.Board.GetTile(new Vector3I(1, 0, 0)).BlocksLineOfSight = true;
      StartBattle(session);

      Assert.False(session.IsUnitVisibleToUnit(observer.UnitId, target.UnitId));
      Assert.False(session.IsUnitVisibleToFaction(playerFaction, target.UnitId));
    });

    T("Vertical line of sight works across levels", () =>
    {
      var playerFaction = BattleTestFactory.MakeFaction("Player");
      var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
      var session = BattleTestFactory.MakeSession(new Vector3I(1, 3, 1), [playerFaction, enemyFaction]);
      var observer = SpawnUnit(session, BattleTestFactory.MakeCombatant("Observer", playerFaction, vision: 5), new Vector3I(0, 0, 0));
      var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Target", enemyFaction, vision: 1), new Vector3I(0, 2, 0));

      StartBattle(session);

      Assert.True(session.IsUnitVisibleToUnit(observer.UnitId, target.UnitId));
    });

    T("Vision stat changes which targets are visible", () =>
    {
      var playerFaction = BattleTestFactory.MakeFaction("Player");
      var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
      var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 2), [playerFaction, enemyFaction]);
      var shortSighted = SpawnUnit(session, BattleTestFactory.MakeCombatant("Short", playerFaction, vision: 2), new Vector3I(0, 0, 0));
      var longSighted = SpawnUnit(session, BattleTestFactory.MakeCombatant("Long", playerFaction, vision: 3), new Vector3I(0, 0, 1));
      var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Target", enemyFaction, vision: 1), new Vector3I(2, 0, 0));

      StartBattle(session);

      Assert.False(session.IsUnitVisibleToUnit(shortSighted.UnitId, target.UnitId));
      Assert.True(session.IsUnitVisibleToUnit(longSighted.UnitId, target.UnitId));
    });

    T("Faction visible tiles are the union of all living allies", () =>
    {
      var playerFaction = BattleTestFactory.MakeFaction("Player");
      var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 5), [playerFaction]);
      SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", playerFaction, vision: 1), new Vector3I(0, 0, 0));
      SpawnUnit(session, BattleTestFactory.MakeCombatant("Bravo", playerFaction, vision: 1), new Vector3I(4, 0, 4));

      StartBattle(session);

      Assert.True(session.IsTileVisibleToFaction(playerFaction, new Vector3I(1, 0, 0)));
      Assert.True(session.IsTileVisibleToFaction(playerFaction, new Vector3I(4, 0, 3)));
      Assert.False(session.IsTileVisibleToFaction(playerFaction, new Vector3I(2, 0, 2)));
    });

    T("Tile visibility includes tiles at the edge of vision range", () =>
    {
      var playerFaction = BattleTestFactory.MakeFaction("Player");
      var session = BattleTestFactory.MakeSession(new Vector3I(6, 1, 1), [playerFaction]);
      SpawnUnit(session, BattleTestFactory.MakeCombatant("Scout", playerFaction, vision: 3), new Vector3I(0, 0, 0));

      StartBattle(session);

      Assert.True(session.IsTileVisibleToFaction(playerFaction, new Vector3I(3, 0, 0)));
      Assert.False(session.IsTileVisibleToFaction(playerFaction, new Vector3I(4, 0, 0)));
    });

    T("Explored tiles persist after they leave current visibility", () =>
    {
      var playerFaction = BattleTestFactory.MakeFaction("Player");
      var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 3), [playerFaction]);
      var observer = SpawnUnit(session, BattleTestFactory.MakeCombatant("Scout", playerFaction, vision: 2), new Vector3I(1, 0, 1));

      StartBattle(session);
      Assert.True(session.IsTileVisibleToFaction(playerFaction, new Vector3I(2, 0, 1)));

      var moveResult = BattleSessionMutation.MoveUnitStep(observer.UnitId, new Vector3I(0, 0, 1)).Execute(session);
      Assert.True(moveResult.Succeeded);

      Assert.False(session.IsTileVisibleToFaction(playerFaction, new Vector3I(2, 0, 1)));
      Assert.True(session.HasFactionExploredTile(playerFaction, new Vector3I(2, 0, 1)));
    });

    T("Enemy units drop from faction visibility when sight is broken", () =>
    {
      var playerFaction = BattleTestFactory.MakeFaction("Player");
      var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
      var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [playerFaction, enemyFaction]);
      var observer = SpawnUnit(session, BattleTestFactory.MakeCombatant("Scout", playerFaction, vision: 3), new Vector3I(1, 0, 0));
      var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Target", enemyFaction, vision: 1), new Vector3I(3, 0, 0));

      StartBattle(session);
      Assert.True(session.IsUnitVisibleToFaction(playerFaction, target.UnitId));

      var moveResult = BattleSessionMutation.MoveUnitStep(observer.UnitId, new Vector3I(0, 0, 0)).Execute(session);
      Assert.True(moveResult.Succeeded);

      Assert.False(session.IsUnitVisibleToFaction(playerFaction, target.UnitId));
    });

    T("Own units remain known to their faction without direct line of sight", () =>
    {
      var playerFaction = BattleTestFactory.MakeFaction("Player");
      var session = BattleTestFactory.MakeSession(new Vector3I(5, 1, 1), [playerFaction]);
      var alpha = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", playerFaction, vision: 1), new Vector3I(0, 0, 0));
      var bravo = SpawnUnit(session, BattleTestFactory.MakeCombatant("Bravo", playerFaction, vision: 1), new Vector3I(4, 0, 0));

      StartBattle(session);

      Assert.False(session.IsUnitVisibleToUnit(alpha.UnitId, bravo.UnitId));
      Assert.True(session.IsUnitVisibleToFaction(playerFaction, bravo.UnitId));
      Assert.Equal(2, session.GetVisibleUnitsForFaction(playerFaction).Count());
    });

    T("SpawnUnit refreshes visibility caches", () =>
    {
      var playerFaction = BattleTestFactory.MakeFaction("Player");
      var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
      var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [playerFaction, enemyFaction]);
      SpawnUnit(session, BattleTestFactory.MakeCombatant("Observer", playerFaction, vision: 4), new Vector3I(0, 0, 0));

      var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Target", enemyFaction, vision: 1), new Vector3I(2, 0, 0));

      Assert.True(session.IsUnitVisibleToFaction(playerFaction, target.UnitId));
    });

    T("StartBattle refreshes visibility caches", () =>
    {
      var playerFaction = BattleTestFactory.MakeFaction("Player");
      var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
      var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [playerFaction, enemyFaction]);
      var observer = SpawnUnit(session, BattleTestFactory.MakeCombatant("Observer", playerFaction, vision: 4), new Vector3I(0, 0, 0));
      var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Target", enemyFaction, vision: 1), new Vector3I(2, 0, 0));

      Assert.True(session.IsUnitVisibleToUnit(observer.UnitId, target.UnitId));
      session.Board.GetTile(new Vector3I(1, 0, 0)).BlocksLineOfSight = true;

      StartBattle(session);

      Assert.False(session.IsUnitVisibleToUnit(observer.UnitId, target.UnitId));
    });

    T("Lethal damage refreshes visibility caches", () =>
    {
      var playerFaction = BattleTestFactory.MakeFaction("Player");
      var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
      var session = BattleTestFactory.MakeSession(new Vector3I(4, 1, 1), [playerFaction, enemyFaction]);
      var observer = SpawnUnit(session, BattleTestFactory.MakeCombatant("Observer", playerFaction, health: 10, vision: 4), new Vector3I(0, 0, 0));
      var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Target", enemyFaction, health: 10, vision: 1), new Vector3I(2, 0, 0));

      StartBattle(session);
      Assert.True(session.IsUnitVisibleToFaction(playerFaction, target.UnitId));

      var damageResult = BattleSessionMutation.ApplyDamage(observer.UnitId, 10).Execute(session);
      Assert.True(damageResult.Succeeded);

      Assert.False(session.IsUnitVisibleToFaction(playerFaction, target.UnitId));
      Assert.Equal(0, session.GetVisibleUnitsForFaction(playerFaction).Count());
    });

    Report();
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
}
