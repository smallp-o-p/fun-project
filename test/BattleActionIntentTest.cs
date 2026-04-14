using FunProject.Battle;
using GdUnit4;
using Godot;
using System;

[TestSuite]
[RequireGodotRuntime]
public class BattleActionIntentTest
{
  [TestCase(TestName = "MoveStep intent captures expected metadata")]
  public void MoveStepIntentCapturesExpectedMetadata()
  {
    var intent = BattleActionIntent.MoveStep(7, new Vector3I(2, 1, 3), 2);

    Assert.True(intent is MoveStepBattleActionIntent);
    Assert.Equal(BattleActionIntent.MoveStepActionId, intent.ActionId);
    var moveIntent = (MoveStepBattleActionIntent)intent;
    Assert.Equal(7, moveIntent.UnitId);
    Assert.Equal(new Vector3I(2, 1, 3), moveIntent.TargetCell);
    Assert.Equal(2, moveIntent.ActionPointCost);
  }

  [TestCase(TestName = "PassUnit intent captures expected metadata")]
  public void PassUnitIntentCapturesExpectedMetadata()
  {
    var intent = BattleActionIntent.PassUnit(5);

    Assert.True(intent is PassUnitBattleActionIntent);
    Assert.Equal(BattleActionIntent.PassUnitActionId, intent.ActionId);
    var passIntent = intent;
    Assert.Equal(5, passIntent.UnitId);
  }

  [TestCase(TestName = "EndFactionTurn intent captures expected metadata")]
  public void EndFactionTurnIntentCapturesExpectedMetadata()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var intent = BattleActionIntent.EndFactionTurn(faction);

    Assert.True(intent is EndFactionTurnBattleActionIntent);
    Assert.Equal(BattleActionIntent.EndFactionTurnActionId, intent.ActionId);
    var endIntent = intent;
    Assert.Equal(faction, endIntent.IssuingSide);
  }

  [TestCase(TestName = "ThrowItem intent captures throwable item and target cell")]
  public void ThrowItemIntentCapturesThrowableItemAndTargetCell()
  {
    var grenade = BattleTestFactory.MakeGrenade("Practice");
    var intent = BattleActionIntent.ThrowItem(3, grenade, new Vector3I(4, 0, 2));

    Assert.True(intent is ThrowItemBattleActionIntent);
    Assert.Equal(BattleActionIntent.ThrowItemActionId, intent.ActionId);
    var throwIntent = intent;
    Assert.Equal(3, throwIntent.UnitId);
    Assert.Equal(new Vector3I(4, 0, 2), throwIntent.TargetCell);
    Assert.Equal(grenade, throwIntent.Item);
  }

  [TestCase(TestName = "Named intent preserves unknown action metadata")]
  public void NamedIntentPreservesUnknownActionMetadata()
  {
    var intent = BattleActionIntent.Named("swap_test", 3);

    Assert.True(intent is NamedBattleActionIntent);
    Assert.Equal("swap_test", intent.ActionId);
    var namedIntent = intent;
    Assert.Equal(3, namedIntent.UnitId);
  }

  [TestCase(TestName = "Custom action uses the provided resolver when executed directly")]
  public void CustomActionUsesTheProvidedResolverWhenExecutedDirectly()
  {
    bool invoked = false;
    var session = BattleTestFactory.MakeSession(new Vector3I(2, 1, 2));
    var intent = BattleActionIntent.Custom("custom_ping", (runtimeSession, self) =>
    {
      invoked = true;
      Assert.Equal("custom_ping", self.ActionId);
      Assert.Equal(11, self.UnitId);
      Assert.Equal(7, (int)self.Payload!.Value);
      return BattleSessionMutation.StartBattle().Execute(runtimeSession).FailureReason == BattleMutationFailureReason.Rejected;
    }, unitId: 11, payload: Variant.From(7));

    var result = intent.Resolve(session);

    Assert.True(invoked);
    Assert.True(result);
  }
}
