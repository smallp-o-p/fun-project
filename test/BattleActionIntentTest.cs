using FunProject.Battle;
using Godot;
using System;

public partial class BattleActionIntentTest : TestRunner
{
  public override void _Ready()
  {
    T("MoveStep intent captures expected metadata", () =>
    {
      var intent = BattleActionIntent.MoveStep(7, new Vector3I(2, 1, 3), 2);

      Assert.True(intent is MoveStepBattleActionIntent);
      Assert.Equal(BattleActionIntent.MoveStepActionId, intent.ActionId);
      var moveIntent = (MoveStepBattleActionIntent)intent;
      Assert.Equal(7, moveIntent.UnitId);
      Assert.Equal(new Vector3I(2, 1, 3), moveIntent.TargetCell);
      Assert.Equal(2, moveIntent.ActionPointCost);
    });

    T("PassUnit intent captures expected metadata", () =>
    {
      var intent = BattleActionIntent.PassUnit(5);

      Assert.True(intent is PassUnitBattleActionIntent);
      Assert.Equal(BattleActionIntent.PassUnitActionId, intent.ActionId);
      var passIntent = intent;
      Assert.Equal(5, passIntent.UnitId);
    });

    T("EndFactionTurn intent captures expected metadata", () =>
    {
      var faction = BattleTestFactory.MakeFaction("Player");
      var intent = BattleActionIntent.EndFactionTurn(faction);

      Assert.True(intent is EndFactionTurnBattleActionIntent);
      Assert.Equal(BattleActionIntent.EndFactionTurnActionId, intent.ActionId);
      var endIntent = intent;
      Assert.Equal(faction, endIntent.IssuingSide);
    });

    T("ThrowItem intent captures throwable item and target cell", () =>
    {
      var grenade = BattleTestFactory.MakeGrenade("Practice");
      var intent = BattleActionIntent.ThrowItem(3, grenade, new Vector3I(4, 0, 2));

      Assert.True(intent is ThrowItemBattleActionIntent);
      Assert.Equal(BattleActionIntent.ThrowItemActionId, intent.ActionId);
      var throwIntent = intent;
      Assert.Equal(3, throwIntent.UnitId);
      Assert.Equal(new Vector3I(4, 0, 2), throwIntent.TargetCell);
      Assert.Equal(grenade, throwIntent.Item);
    });

    T("Named intent preserves unknown action metadata", () =>
    {
      var intent = BattleActionIntent.Named("swap_test", 3);

      Assert.True(intent is NamedBattleActionIntent);
      Assert.Equal("swap_test", intent.ActionId);
      var namedIntent = intent;
      Assert.Equal(3, namedIntent.UnitId);
    });

    T("Custom action uses the provided resolver when executed by the executor", () =>
    {
      bool invoked = false;
      var executor = new BattleActionExecutor(new BattleSession(2, 2, 1));
      var intent = BattleActionIntent.Custom("custom_ping", (_, self) =>
      {
        invoked = true;
        Assert.Equal("custom_ping", self.ActionId);
        Assert.Equal(11, self.UnitId);
        Assert.Equal(7, (int)self.Payload!.Value);
        return true;
      }, unitId: 11, payload: Variant.From(7));

      executor.Enqueue(intent);
      var result = executor.Tick();

      Assert.True(invoked);
      Assert.True(result.HasValue);
      Assert.True(result!.Value.Succeeded);
    });

    Report();
  }
}
