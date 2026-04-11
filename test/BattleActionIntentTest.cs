using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Stats;
using Godot;
using System;

public partial class BattleActionIntentTest : TestRunner
{
  public override void _Ready()
  {
    T("MoveStep intent captures expected metadata", () =>
    {
      var intent = BattleActionIntent.MoveStep(7, new Vector3I(2, 1, 3), 2);

      Assert.Equal("move_step", intent.ActionId);
      Assert.Equal(7, intent.UnitId);
      Assert.Equal(new Vector3I(2, 1, 3), intent.TargetCell!.Value);
      Assert.Equal(2, (int)intent.Payload!.Value);
    });

    T("MoveStep applies step movement through the session", () =>
    {
      var session = new BattleSession(4, 4, 2);
      var faction = MakeFaction("Player");
      var unit = session.AddUnit(MakeCombatant("Alpha", faction, actionPoints: 5), new Vector3I(1, 0, 1));
      session.StartBattle();

      var moved = BattleActionIntent.MoveStep(unit.UnitId, new Vector3I(1, 1, 1), 2).Apply(session);

      Assert.True(moved);
      Assert.Equal(new Vector3I(1, 1, 1), unit.Position);
      Assert.Equal(3, unit.CurrentActionPoints);
    });

    T("MoveStep fails when the target step is illegal", () =>
    {
      var session = new BattleSession(4, 4, 1);
      var faction = MakeFaction("Player");
      var unit = session.AddUnit(MakeCombatant("Alpha", faction), new Vector3I(0, 0, 0));
      session.StartBattle();

      var moved = BattleActionIntent.MoveStep(unit.UnitId, new Vector3I(2, 0, 0)).Apply(session);

      Assert.False(moved);
      Assert.Equal(new Vector3I(0, 0, 0), unit.Position);
    });

    T("EndTurn intent advances the active faction", () =>
    {
      var session = new BattleSession(4, 4, 1);
      var factionA = MakeFaction("A");
      var factionB = MakeFaction("B");

      var unitA = session.AddUnit(MakeCombatant("A1", factionA), new Vector3I(0, 0, 0));
      session.AddUnit(MakeCombatant("B1", factionB), new Vector3I(1, 0, 0));
      session.StartBattle();

      var applied = BattleActionIntent.EndTurn(unitA.UnitId).Apply(session);

      Assert.True(applied);
      Assert.Equal(factionB, session.ActiveSide);
    });

    T("Custom intent executes provided resolver", () =>
    {
      var session = new BattleSession(3, 3, 1);
      var intent = BattleActionIntent.Custom(
        actionId: "custom_ping",
        unitId: 11,
        resolver: (_, self) =>
        {
          Assert.Equal("custom_ping", self.ActionId);
          Assert.Equal(11, self.UnitId);
          Assert.Equal(new Vector3I(1, 0, 1), self.TargetCell!.Value);
          Assert.Equal(99, self.TargetUnitId!.Value);
          Assert.Equal("hello", (string)self.Payload!.Value);
          return true;
        },
        targetCell: new Vector3I(1, 0, 1),
        targetUnitId: 99,
        payload: Variant.From("hello"));

      Assert.True(intent.Apply(session));
    });

    T("WithResolver preserves metadata and swaps behavior", () =>
    {
      var original = BattleActionIntent.Custom(
        actionId: "swap_test",
        unitId: 3,
        resolver: (_, _) => false,
        targetCell: new Vector3I(1, 2, 3),
        targetUnitId: 4,
        payload: Variant.From(12));

      var swapped = original.WithResolver((_, self) =>
      {
        Assert.Equal("swap_test", self.ActionId);
        Assert.Equal(3, self.UnitId);
        Assert.Equal(new Vector3I(1, 2, 3), self.TargetCell!.Value);
        Assert.Equal(4, self.TargetUnitId!.Value);
        Assert.Equal(12, (int)self.Payload!.Value);
        return true;
      });

      Assert.True(swapped.Apply(new BattleSession(2, 2, 1)));
    });

    T("Apply rejects null session", () =>
    {
      var intent = BattleActionIntent.Custom("null_session", 1, (_, _) => true);
      Assert.Throws<ArgumentNullException>(() => intent.Apply(null!));
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
