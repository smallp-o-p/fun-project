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
    var player = TestData.MakeFaction("P");
    var enemy = TestData.MakeFaction("E");
    var bombData = new BattleSpecialObjectData { Name = "Bomb" };
    bombData.Capabilities.Add(new TimedEffectCapabilityData { FireAfterTurns = 1 });

    var setup = TestData.MakeBattleSetup(player, enemy,
      new UnitLoadout(TestData.MakeCombatant("A", player)), new UnitLoadout(TestData.MakeCombatant("B", enemy)),
      [new FakeObjectiveData()], [new FakeObjectiveData()])
      with
    { Objects = [new ObjectPlacement(bombData, new Vector3I(1, 0, 0))] };
    using var runtime = BattleFactory.Start(setup).RequireRight();
    BattleObjectState bomb = runtime.Query(new GetBattleSpecialObjectsQuery())[0];

    var observed = new List<Option<ObjectStatus>>();
    runtime.RegisterHook<TurnEndedBattleEvent>(
      new StatusObserverHook(() => observed.Add(bomb.Status)), 0);
    new ObjectExpirySystemData { Priority = -50 }.Register(runtime);

    runtime.ExecuteAction(BattleAction.EndFactionTurn(runtime.Query(new GetCurrentTurnQuery()).RequireSome().ActiveFaction));

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
