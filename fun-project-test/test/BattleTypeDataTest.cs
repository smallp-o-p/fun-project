using FunProject.Battle;
using FunProject.Combatants;
using GdUnit4;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public partial class BattleTypeDataTest
{
  // The data class's whole job is registration through the generic door; assert the delegation
  // on a live runtime: with Priority = -50 the expiry fires before a 0-priority observer,
  // so the observer must already see the bomb expired.
  [TestCase(TestName = "ObjectExpirySystemData registers its hook with the authored priority")]
  public void ObjectExpiryRegistration()
  {
    var player = BattleTestFactory.MakeFaction("P");
    var enemy = BattleTestFactory.MakeFaction("E");
    var bombData = new BattleSpecialObjectData { Name = "Bomb" };
    bombData.Capabilities.Add(new TimedEffectCapabilityData { FireAfterTurns = 1 });

    var setup = new BattleSetup(
      new BattleBoardState(new Vector3I(4, 1, 4)),
      [player, enemy],
      [
        new UnitPlacement(new UnitLoadout(BattleTestFactory.MakeCombatant("A", player)), new Vector3I(0, 0, 0)),
        new UnitPlacement(new UnitLoadout(BattleTestFactory.MakeCombatant("B", enemy)), new Vector3I(3, 0, 3)),
      ],
      new System.Collections.Generic.Dictionary<Faction, System.Collections.Generic.IReadOnlyList<ObjectiveData>>
      {
        [player] = [new FakeObjectiveData()],
        [enemy] = [new FakeObjectiveData()],
      },
      Objects: [new ObjectPlacement(bombData, new Vector3I(1, 0, 0))]);
    BattleRuntime runtime = BattleFactory.Start(setup).Match(
      Right: r => r,
      Left: failure => throw new System.InvalidOperationException($"Start failed: {failure.Message}"));
    BattleObjectState bomb = runtime.Query(new GetBattleSpecialObjectsQuery())[0];

    var observed = new List<Option<ObjectStatus>>();
    runtime.RegisterHook<TurnEndedBattleEvent>(
      new StatusObserverHook(() => observed.Add(bomb.Status)), 0);
    new ObjectExpirySystemData { Priority = -50 }.Register(runtime);

    runtime.ExecuteAction(BattleAction.EndFactionTurn(runtime.Query(new GetActiveSideQuery())));

    Assert.Equal(1, observed.Count);
    Assert.Equal(Some(ObjectStatus.Expired), observed[0]);
  }

  private sealed class StatusObserverHook(System.Action onFire) : BattleHook
  {
    public override IReadOnlyList<BattleAction> OnEvent(HookContext context, BattleEvent battleEvent)
    {
      onFire();
      return [];
    }
  }
}
