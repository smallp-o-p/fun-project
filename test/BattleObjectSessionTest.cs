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

  [TestCase(TestName = "Preparation placement occupies the tile and mints a live proof once started")]
  public void PlacementBlocksOccupancy()
  {
    var player = TestData.MakeFaction("P");
    var enemy = TestData.MakeFaction("E");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [player, enemy]);
    battle.Spawn(TestData.MakeCombatant("A", player), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B", enemy), new Vector3I(3, 0, 3));

    BattleBoardState.ValidatedPoint point = battle.At(new Vector3I(2, 0, 2));
    BattleObjectState bomb = battle.PlaceObject(MakeBombData(), point.Raw);
    battle.Start();

    Assert.Equal(1, battle.Query(new GetBattleSpecialObjectsQuery()).Count);
    Assert.True(bomb.Status.IsNone);
    Assert.Equal(point, battle.Live(bomb).Position);
    Assert.Equal(point.Raw, bomb.Position);

    // Occupancy: a reinforcement cannot spawn onto the object tile (a rejected SpawnUnit
    // surfaces from Submit as an InvalidOperationException — trusted parameters).
    var spawn = new SpawnUnit(TestData.MakeCombatant("X", player), point);
    Assert.Throws<System.InvalidOperationException>(() => battle.Submit(spawn));

    // Initial placement is preparation-only: a post-Start placement has no submission path.
    Assert.Throws<System.InvalidOperationException>(
      () => battle.PlaceObject(MakeBombData(), point.Raw));
  }

  [TestCase(TestName = "Post-Start placement is refused: initial placement is preparation-only")]
  public void MidBattlePlacementRejected()
  {
    var player = TestData.MakeFaction("P");
    var enemy = TestData.MakeFaction("E");
    using var battle = BattleFixture.Started(new Vector3I(4, 1, 4),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("A", player)), new Vector3I(0, 0, 0)),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("B", enemy)), new Vector3I(3, 0, 3)));

    BattleBoardState.ValidatedPoint point = battle.At(new Vector3I(2, 0, 2));
    Assert.Throws<System.InvalidOperationException>(
      () => battle.PlaceObject(MakeBombData(), point.Raw));
    Assert.Equal(0, battle.Query(new GetBattleSpecialObjectsQuery()).Count);
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
