using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Items;
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

    T("Passing a unit ends its activation and selects the next ally", () =>
    {
      var session = new BattleSession(4, 4, 1);
      var faction = MakeFaction("Player");
      var unitA = session.AddUnit(MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
      var unitB = session.AddUnit(MakeCombatant("Bravo", faction), new Vector3I(1, 0, 0));
      session.StartBattle();

      var passed = session.TryPassSelectedUnit();

      Assert.True(passed);
      Assert.True(unitA.HasEndedActivationThisTurn);
      Assert.Equal(unitB.UnitId, session.SelectedUnitId!.Value);
      Assert.Equal(faction, session.ActiveSide);
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
      Assert.False(session.SelectedUnitId.HasValue);
      Assert.False(session.TurnQueue.Contains(factionA));
      Assert.True(session.TurnQueue.Contains(factionB));
    });

    T("Selected unit can throw a grenade in battle session", () =>
    {
      var session = new BattleSession(5, 5, 1);
      var faction = MakeFaction("Player");
      var unit = session.AddUnit(MakeCombatant("Thrower", faction, actionPoints: 4), new Vector3I(1, 0, 1));
      var grenade = MakeGrenade("Practice Grenade", throwRange: 4);
      unit.AddInventoryItem(grenade);

      BattleEvent? thrownEvent = null;
      session.EventRaised += battleEvent =>
      {
        if (battleEvent.Type == BattleEventType.ItemThrown)
          thrownEvent = battleEvent;
      };

      session.StartBattle();
      var threw = session.TryThrowSelectedUnitItem(grenade, new Vector3I(3, 0, 1));

      Assert.True(threw);
      Assert.False(unit.HasInventoryItem(grenade));
      Assert.Equal(3, unit.CurrentActionPoints);
      Assert.True(thrownEvent.HasValue);
      Assert.Equal(BattleEventType.ItemThrown, thrownEvent!.Value.Type);
      Assert.Equal(unit.UnitId, thrownEvent.Value.UnitId!.Value);
      Assert.Equal(new Vector3I(3, 0, 1), thrownEvent.Value.Position!.Value);
    });

    Report();
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
