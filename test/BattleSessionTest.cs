using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Stats;
using Godot;
using System;
using System.Linq;

public partial class BattleSessionTest : TestRunner
{
  public override void _Ready()
  {
    T("AddUnit occupies its tile", () =>
    {
      var session = new BattleSession(4, 4, 2);
      var faction = MakeFaction("City Guard");
      var unit = session.AddUnit(MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));

      var tile = session.Board.GetTile(new Vector3I(1, 0, 1));
      Assert.True(tile.IsOccupied);
      Assert.Equal(unit.UnitId, tile.OccupantUnitId!.Value);
    });

    T("AddUnit rejects occupied tile", () =>
    {
      var session = new BattleSession(4, 4, 1);
      var faction = MakeFaction("City Guard");

      session.AddUnit(MakeCombatant("Alpha", faction), new Vector3I(1, 0, 1));

      Assert.Throws<InvalidOperationException>(() =>
        session.AddUnit(MakeCombatant("Bravo", faction), new Vector3I(1, 0, 1)));
    });

    T("StartBattle selects first participating faction", () =>
    {
      var session = new BattleSession(4, 4, 1);
      var playerFaction = MakeFaction("Player");
      var enemyFaction = MakeFaction("Enemy");

      session.AddUnit(MakeCombatant("Alpha", playerFaction), new Vector3I(0, 0, 0));
      session.AddUnit(MakeCombatant("Bandit", enemyFaction), new Vector3I(1, 0, 0));

      session.StartBattle();

      Assert.Equal(playerFaction, session.ActiveSide);
      Assert.Equal(1, session.TurnNumber);
      Assert.Equal(playerFaction, session.SelectedUnit!.Side);
    });

    T("AdvanceTurn rotates only participating factions", () =>
    {
      var session = new BattleSession(5, 5, 1);
      var factionA = MakeFaction("A");
      var factionB = MakeFaction("B");
      var factionC = MakeFaction("C");

      session.AddUnit(MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
      session.AddUnit(MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
      session.AddUnit(MakeCombatant("C1", factionC), new Vector3I(2, 0, 0));
      session.StartBattle();

      Assert.Equal(factionA, session.ActiveSide);

      session.AdvanceTurn();
      Assert.Equal(factionB, session.ActiveSide);
      Assert.Equal(1, session.TurnNumber);

      session.AdvanceTurn();
      Assert.Equal(factionC, session.ActiveSide);
      Assert.Equal(1, session.TurnNumber);

      session.AdvanceTurn();
      Assert.Equal(factionA, session.ActiveSide);
      Assert.Equal(2, session.TurnNumber);
    });

    T("Move updates unit position occupancy and action points", () =>
    {
      var session = new BattleSession(4, 4, 2);
      var faction = MakeFaction("Player");
      var unit = session.AddUnit(MakeCombatant("Alpha", faction, actionPoints: 5), new Vector3I(1, 0, 1));
      session.StartBattle();

      var moved = session.TryMoveSelectedUnitStep(new Vector3I(1, 1, 1), actionPointCost: 2);

      Assert.True(moved);
      Assert.Equal(new Vector3I(1, 1, 1), unit.Position);
      Assert.False(session.Board.GetTile(new Vector3I(1, 0, 1)).IsOccupied);
      Assert.True(session.Board.GetTile(new Vector3I(1, 1, 1)).IsOccupied);
      Assert.Equal(3, unit.CurrentActionPoints);
    });

    T("Move rejects non-adjacent destination", () =>
    {
      var session = new BattleSession(5, 5, 1);
      var faction = MakeFaction("Player");

      session.AddUnit(MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
      session.StartBattle();

      Assert.False(session.TryMoveSelectedUnitStep(new Vector3I(2, 0, 0)));
    });

    T("Vertical move is allowed as a one-cell step", () =>
    {
      var session = new BattleSession(3, 3, 3);
      var faction = MakeFaction("Player");
      var unit = session.AddUnit(MakeCombatant("Climber", faction), new Vector3I(1, 0, 1));
      session.StartBattle();

      Assert.True(session.TryMoveSelectedUnitStep(new Vector3I(1, 1, 1)));
      Assert.Equal(new Vector3I(1, 1, 1), unit.Position);
    });

    T("Killing a selected unit clears occupancy selection and faction queue", () =>
    {
      var session = new BattleSession(4, 4, 1);
      var factionA = MakeFaction("A");
      var factionB = MakeFaction("B");
      var unitA = session.AddUnit(MakeCombatant("A1", factionA, health: 10), new Vector3I(0, 0, 0));
      session.AddUnit(MakeCombatant("B1", factionB, health: 10), new Vector3I(1, 0, 0));
      session.StartBattle();

      Assert.Equal(factionA, session.ActiveSide);
      Assert.Equal(unitA.UnitId, session.SelectedUnitId);

      session.ApplyDamage(unitA.UnitId, 10);

      Assert.False(session.Board.GetTile(new Vector3I(0, 0, 0)).IsOccupied);
      Assert.Equal(null, session.SelectedUnitId);
      Assert.False(session.TurnQueue.Contains(factionA));
      Assert.True(session.TurnQueue.Contains(factionB));
    });

    Report();
  }

  private static Combatant MakeCombatant(string name, Faction faction, int health = 20, int actionPoints = 4, int movement = 12)
  {
    return new Combatant(new CombatantData
    {
      Name = name,
      HealthStat = new Stat { StatType = StatType.Health, BaseValue = health },
      ActionPointsStat = new Stat { StatType = StatType.ActionPoints, BaseValue = actionPoints },
      WillStat = new Stat { StatType = StatType.Will, BaseValue = 50 },
      MovementStat = new Stat { StatType = StatType.Movement, BaseValue = movement },
      AimStat = new Stat { StatType = StatType.Aim, BaseValue = 65 },
      BaseArmorStat = new Stat { StatType = StatType.BaseArmor, BaseValue = 0 },
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
}
