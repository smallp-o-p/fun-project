using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Items.Effects;
using GdUnit4;
using Godot;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public class BattleObjectSessionTest
{
  private static BattleSpecialObjectData MakeBombData()
  {
    var data = new BattleSpecialObjectData { Name = "Bomb" };
    data.Capabilities.Add(new InteractiveCapabilityData { ActionPointCost = 1 });
    return data;
  }

  [TestCase(TestName = "Startup object placement occupies the tile and mints a live proof")]
  public void PlacementBlocksOccupancy()
  {
    var player = TestData.MakeFaction("P");
    var enemy = TestData.MakeFaction("E");
    using var battle = new BattleFixture(new BattleSetup(
      TestData.MakeOpenBattleMap(new Vector3I(4, 1, 4)),
      [
        new BattleSideSetup(player, [new FakeObjectiveData()],
          [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("A", player)), new Vector3I(0, 0, 0))]),
        new BattleSideSetup(enemy, [new FakeObjectiveData()],
          [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("B", enemy)), new Vector3I(3, 0, 3))]),
      ],
      Seed: 7) with
    { Objects = [new ObjectPlacement(MakeBombData(), new Vector3I(2, 0, 2))] });
    var session = battle.Session;

    BattleBoardState.ValidatedPoint point = battle.At(new Vector3I(2, 0, 2));
    BattleObjectState[] objects = [.. session.Objects];
    Assert.Equal(1, objects.Length);
    BattleObjectState bomb = objects[0];
    Assert.True(bomb.Status.IsNone);
    Assert.Equal(point, battle.Live(bomb).Position);
    Assert.Equal(point.Raw, bomb.Position);

    // Occupancy: a unit cannot spawn onto the object tile (a rejected SpawnUnit surfaces
    // from Submit as an InvalidOperationException — trusted parameters). The duplicate-
    // placement validation matrix lives at the factory boundary (BattleFactoryTest).
    var spawn = new SpawnUnit(TestData.MakeCombatant("X", player), point);
    Assert.Throws<System.InvalidOperationException>(() => battle.Submit(spawn));
  }

  [TestCase(TestName = "Start(setup) places objects")]
  public void StartPlacesObjects()
  {
    var player = TestData.MakeFaction("P");
    var enemy = TestData.MakeFaction("E");

    // Fresh A/B combatants and object resources per Start; the two-object success case and
    // its typed-failure matrix live at the factory boundary (BattleFactoryTest).
    using BattleRuntime runtime = BattleFactory.Start(TestData.MakeBattleSetup(player, enemy,
      new UnitLoadout(TestData.MakeCombatant("A", player)), new UnitLoadout(TestData.MakeCombatant("B", enemy)),
      [new FakeObjectiveData()], [new FakeObjectiveData()]) with
    {
      Objects =
      [
        new ObjectPlacement(MakeBombData(), new Vector3I(1, 0, 1)),
        new ObjectPlacement(MakeBombData(), new Vector3I(2, 0, 2)),
      ],
    }).RequireRight();
    Assert.Equal(2, runtime.Query(new GetBattleSpecialObjectsQuery()).Count);
  }
}
