#nullable enable
using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Items;
using FunProject.Stats;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class BattleSessionTest : TestRunner
{
  public override void _Ready()
  {
    T("Board dimensions follow Vector3I axis order", () =>
    {
      var board = new BattleBoardState(new Vector3I(4, 2, 5));

      Assert.True(board.IsInBounds(new Vector3I(3, 1, 4)));
      Assert.False(board.IsInBounds(new Vector3I(3, 2, 4)));
      Assert.False(board.IsInBounds(new Vector3I(3, 1, 5)));

      var tile = board.GetTileOrNull(new Vector3I(3, 1, 4));
      Assert.True(tile != null);
      Assert.Equal(new Vector3I(3, 1, 4), tile!.Coordinates);
    });

    T("SpawnUnit occupies its tile", () =>
    {
      var session = MakeSession(new Vector3I(4, 2, 4));
      var faction = MakeFaction("City Guard");
      var unit = SpawnUnit(session, MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));

      var tile = session.Board.GetTile(new Vector3I(1, 0, 1));
      Assert.True(tile.IsOccupied);
      Assert.Equal(unit.UnitId, tile.OccupantUnitId!.Value);
    });

    T("SpawnUnit rejects occupied tile", () =>
    {
      var session = MakeSession(new Vector3I(4, 1, 4));
      var faction = MakeFaction("City Guard");

      SpawnUnit(session, MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));
      var result = BattleSessionMutation.SpawnUnit(MakeCombatant("Bravo", faction), new Vector3I(1, 0, 1)).Execute(session);

      Assert.False(result.Succeeded);
    });

    T("StartBattle activates first participating faction", () =>
    {
      var playerFaction = MakeFaction("Player");
      var enemyFaction = MakeFaction("Enemy");
      var session = MakeSession(new Vector3I(4, 1, 4), [playerFaction, enemyFaction]);

      SpawnUnit(session, MakeCombatant("Alpha", playerFaction), new Vector3I(0, 0, 0));
      SpawnUnit(session, MakeCombatant("Bandit", enemyFaction), new Vector3I(1, 0, 0));
      StartBattle(session);

      Assert.Equal(playerFaction, session.ActiveSide);
      Assert.Equal(1, session.TurnNumber);
    });

    T("Global faction order keeps each faction only once", () =>
    {
      var session = MakeSession(new Vector3I(4, 1, 4));
      var factionA = MakeFaction("A");
      var factionB = MakeFaction("B");

      SpawnUnit(session, MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
      SpawnUnit(session, MakeCombatant("A2", factionA), new Vector3I(1, 0, 0));
      SpawnUnit(session, MakeCombatant("B1", factionB), new Vector3I(2, 0, 0));

      Assert.Equal(2, session.GlobalFactionTurnOrder.Count);
      Assert.Equal(factionA, session.GlobalFactionTurnOrder.First());
      Assert.Equal(factionB, session.GlobalFactionTurnOrder.Last());
    });

    T("Constructor seeds global faction order and faction rosters", () =>
    {
      var factionA = MakeFaction("A");
      var factionB = MakeFaction("B");
      var combatantA = MakeCombatant("A1", factionA);
      var combatantB = MakeCombatant("B1", factionB);
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
    });

    T("AdvanceTurn rotates only participating factions without incrementing early", () =>
    {
      var factionA = MakeFaction("A");
      var factionB = MakeFaction("B");
      var factionC = MakeFaction("C");
      var session = MakeSession(new Vector3I(5, 1, 5), [factionA, factionB, factionC]);

      SpawnUnit(session, MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
      SpawnUnit(session, MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
      SpawnUnit(session, MakeCombatant("C1", factionC), new Vector3I(2, 0, 0));
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
    });

    T("Passing the last available unit advances the global turn counter", () =>
    {
      var factionA = MakeFaction("A");
      var factionB = MakeFaction("B");
      var session = MakeSession(new Vector3I(4, 1, 4), [factionA, factionB]);
      var unitA = SpawnUnit(session, MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
      var unitB = SpawnUnit(session, MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
      StartBattle(session);

      Assert.Equal(1, session.TurnNumber);
      Assert.Equal(factionA, session.ActiveSide);

      PassUnit(session, unitA.UnitId);
      Assert.Equal(1, session.TurnNumber);
      Assert.Equal(factionB, session.ActiveSide);

      PassUnit(session, unitB.UnitId);
      Assert.Equal(2, session.TurnNumber);
      Assert.Equal(factionA, session.ActiveSide);
    });

    T("Eliminating an unacted faction does not block the global turn counter", () =>
    {
      var factionA = MakeFaction("A");
      var factionB = MakeFaction("B");
      var factionC = MakeFaction("C");
      var session = MakeSession(new Vector3I(5, 1, 5), [factionA, factionB, factionC]);

      SpawnUnit(session, MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
      SpawnUnit(session, MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
      var unitC = SpawnUnit(session, MakeCombatant("C1", factionC, health: 10), new Vector3I(2, 0, 0));
      StartBattle(session);

      ApplyDamage(session, unitC.UnitId, 10);
      Assert.False(session.TurnQueue.Contains(factionC));

      AdvanceTurn(session);
      Assert.Equal(1, session.TurnNumber);
      Assert.Equal(factionB, session.ActiveSide);

      AdvanceTurn(session);
      Assert.Equal(2, session.TurnNumber);
      Assert.Equal(factionA, session.ActiveSide);
    });

    T("Adding a faction mid-round delays the global turn counter until it acts", () =>
    {
      var factionA = MakeFaction("A");
      var factionB = MakeFaction("B");
      var factionC = MakeFaction("C");
      var session = MakeSession(new Vector3I(5, 1, 5), [factionA, factionB]);

      SpawnUnit(session, MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
      SpawnUnit(session, MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
      StartBattle(session);

      SpawnUnit(session, MakeCombatant("C1", factionC), new Vector3I(2, 0, 0));

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
    });

    T("Adding units to a faction that already acted waits until the next round", () =>
    {
      var factionA = MakeFaction("A");
      var factionB = MakeFaction("B");
      var session = MakeSession(new Vector3I(5, 1, 5), [factionA, factionB]);

      SpawnUnit(session, MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
      SpawnUnit(session, MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
      StartBattle(session);

      AdvanceTurn(session);
      Assert.Equal(factionB, session.ActiveSide);
      Assert.Equal(1, session.TurnNumber);

      SpawnUnit(session, MakeCombatant("A2", factionA), new Vector3I(2, 0, 0));

      AdvanceTurn(session);
      Assert.Equal(2, session.TurnNumber);
      Assert.Equal(factionA, session.ActiveSide);
    });

    T("Adding a unit mid-battle reconciles the faction queue", () =>
    {
      var factionA = MakeFaction("A");
      var factionB = MakeFaction("B");
      var factionC = MakeFaction("C");
      var session = MakeSession(new Vector3I(5, 1, 5), [factionA, factionB]);

      SpawnUnit(session, MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
      SpawnUnit(session, MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
      StartBattle(session);

      SpawnUnit(session, MakeCombatant("C1", factionC), new Vector3I(2, 0, 0));

      Assert.Equal(factionA, session.ActiveSide);
      Assert.True(session.TurnQueue.Contains(factionA));
      Assert.True(session.TurnQueue.Contains(factionB));
      Assert.True(session.TurnQueue.Contains(factionC));
    });

    T("Move updates unit position occupancy and action points", () =>
    {
      var faction = MakeFaction("Player");
      var session = MakeSession(new Vector3I(4, 2, 4), [faction]);
      var unit = SpawnUnit(session, MakeCombatant("Alpha", faction, actionPoints: 5), new Vector3I(1, 0, 1));
      StartBattle(session);

      var moved = BattleSessionMutation.MoveUnitStep(unit.UnitId, new Vector3I(1, 1, 1), 2).Execute(session);

      Assert.True(moved.Succeeded);
      Assert.Equal(new Vector3I(1, 1, 1), unit.Position);
      Assert.False(session.Board.GetTile(new Vector3I(1, 0, 1)).IsOccupied);
      Assert.True(session.Board.GetTile(new Vector3I(1, 1, 1)).IsOccupied);
      Assert.Equal(3, unit.CurrentActionPoints);
    });

    T("Move rejects non-adjacent destination", () =>
    {
      var faction = MakeFaction("Player");
      var session = MakeSession(new Vector3I(5, 1, 5), [faction]);
      var unit = SpawnUnit(session, MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
      StartBattle(session);

      var result = BattleSessionMutation.MoveUnitStep(unit.UnitId, new Vector3I(2, 0, 0)).Execute(session);
      Assert.False(result.Succeeded);
    });

    T("Vertical move is allowed as a one-cell step", () =>
    {
      var faction = MakeFaction("Player");
      var session = MakeSession(new Vector3I(3, 3, 3), [faction]);
      var unit = SpawnUnit(session, MakeCombatant("Climber", faction), new Vector3I(1, 0, 1));
      StartBattle(session);

      var result = BattleSessionMutation.MoveUnitStep(unit.UnitId, new Vector3I(1, 1, 1)).Execute(session);
      Assert.True(result.Succeeded);
      Assert.Equal(new Vector3I(1, 1, 1), unit.Position);
    });

    T("Passing a unit ends its activation while keeping the next ally available", () =>
    {
      var faction = MakeFaction("Player");
      var session = MakeSession(new Vector3I(4, 1, 4), [faction]);
      var unitA = SpawnUnit(session, MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
      var unitB = SpawnUnit(session, MakeCombatant("Bravo", faction), new Vector3I(1, 0, 0));
      StartBattle(session);

      PassUnit(session, unitA.UnitId);

      Assert.False(session.IsUnitStillAvailableThisTurn(unitA.UnitId));
      Assert.False(session.CanUnitActNow(unitA.UnitId));
      Assert.True(session.IsUnitStillAvailableThisTurn(unitB.UnitId));
      Assert.Equal(faction, session.ActiveSide);
    });

    T("Killing the only active unit on an eliminated faction does not auto-advance", () =>
    {
      var factionA = MakeFaction("A");
      var factionB = MakeFaction("B");
      var session = MakeSession(new Vector3I(4, 1, 4), [factionA, factionB]);
      var unitA = SpawnUnit(session, MakeCombatant("A1", factionA, health: 10), new Vector3I(0, 0, 0));
      SpawnUnit(session, MakeCombatant("B1", factionB, health: 10), new Vector3I(1, 0, 0));
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
    });

    T("Killing an active unit leaves another surviving ally actable", () =>
    {
      var factionA = MakeFaction("A");
      var factionB = MakeFaction("B");
      var session = MakeSession(new Vector3I(4, 1, 4), [factionA, factionB]);
      var unitA1 = SpawnUnit(session, MakeCombatant("A1", factionA, health: 10), new Vector3I(0, 0, 0));
      var unitA2 = SpawnUnit(session, MakeCombatant("A2", factionA, health: 10), new Vector3I(1, 0, 0));
      SpawnUnit(session, MakeCombatant("B1", factionB, health: 10), new Vector3I(2, 0, 0));
      StartBattle(session);

      ApplyDamage(session, unitA1.UnitId, 10);

      Assert.Equal(factionA, session.ActiveSide);
      Assert.True(session.CanUnitActNow(unitA2.UnitId));
      Assert.False(session.Board.GetTile(unitA1.Position).IsOccupied);
      Assert.True(session.DeadUnits.Contains(unitA1));
    });

    T("Killing the last actable unit does not auto-advance even if the faction survives", () =>
    {
      var factionA = MakeFaction("A");
      var factionB = MakeFaction("B");
      var session = MakeSession(new Vector3I(4, 1, 4), [factionA, factionB]);
      var unitA1 = SpawnUnit(session, MakeCombatant("A1", factionA, health: 10, actionPoints: 4), new Vector3I(0, 0, 0));
      var unitA2 = SpawnUnit(session, MakeCombatant("A2", factionA, health: 10, actionPoints: 0), new Vector3I(1, 0, 0));
      SpawnUnit(session, MakeCombatant("B1", factionB, health: 10), new Vector3I(2, 0, 0));
      StartBattle(session);

      Assert.False(session.CanUnitActNow(unitA2.UnitId));

      ApplyDamage(session, unitA1.UnitId, 10);

      Assert.Equal(factionA, session.ActiveSide);
      Assert.Equal(1, session.TurnNumber);
      Assert.False(session.CanUnitActNow(unitA2.UnitId));

      AdvanceTurn(session);
      Assert.Equal(factionB, session.ActiveSide);
    });

    T("Unit can throw a grenade in battle session", () =>
    {
      var faction = MakeFaction("Player");
      var session = MakeSession(new Vector3I(5, 1, 5), [faction]);
      var unit = SpawnUnit(session, MakeCombatant("Thrower", faction, actionPoints: 4), new Vector3I(1, 0, 1));
      var grenade = MakeGrenade("Practice Grenade", throwRange: 4);
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
    });

    Report();
  }

  private static BattleSession MakeSession(
    Vector3I dimensions,
    IEnumerable<Faction>? globalFactionOrder = null,
    IDictionary<Faction, IEnumerable<Combatant>>? factionRosters = null)
  {
    return new BattleSession(
      dimensions,
      globalFactionOrder ?? [],
      factionRosters ?? new Dictionary<Faction, IEnumerable<Combatant>>());
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

  private static Combatant MakeCombatant(string name, Faction faction, int health = 20, int actionPoints = 4, int movement = 12)
  {
    return new Combatant(new CombatantData
    {
      Name = name,
      HealthStat = new HealthStat { BaseValue = health },
      ActionPointsStat = new ActionPointsStat { BaseValue = actionPoints },
      WillStat = new WillStat { BaseValue = 50 },
      MovementStat = new MovementStat { BaseValue = movement },
      AimStat = new AimStat { BaseValue = 65 },
      BaseArmorStat = new BaseArmorStat { BaseValue = 0 },
      ModSlotCount = 0,
    }, faction);
  }

  private static Faction MakeFaction(string name)
  {
    return new Faction(new FactionData
    {
      Name = name,
      Description = $"{name} faction"
    });
  }

  private static Grenade MakeGrenade(string name, int throwRange = 3, int actionPointCost = 1)
  {
    return new Grenade(new GrenadeData
    {
      Name = name,
      Description = $"{name} description",
      ThrowRange = throwRange,
      ActionPointCost = actionPointCost,
      MaxCharges = 1,
      ConsumesOnUse = true,
      BlastRadius = 1,
    });
  }
}
