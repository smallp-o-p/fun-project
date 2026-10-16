using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Items.Effects;
using GdUnit4;
using Godot;
using System.Collections.Generic;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class BattleObjectSessionTest
{
  [TestCase(TestName = "Startup places both objects, blocks their tiles, and mints live proofs")]
  public static void PlacementBlocksOccupancy()
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
    {
      Objects =
      [
        new ObjectPlacement(TestData.MakeObject("Bomb", capabilities: new InteractiveCapabilityData()), new Vector3I(1, 0, 1)),
        new ObjectPlacement(TestData.MakeObject("Bomb", capabilities: new InteractiveCapabilityData()), new Vector3I(2, 0, 2)),
      ],
    });
    var objects = battle.Query(new GetBattleSpecialObjectsQuery());
    Assert.Equal(2, objects.Count);
    foreach (var position in new[] { new Vector3I(1, 0, 1), new Vector3I(2, 0, 2) })
    {
      BattleBoardState.ValidatedPoint point = battle.At(position);
      var bomb = objects.AsValueEnumerable().Single(obj => obj.Position == position);
      Assert.True(bomb.Status.IsNone);
      Assert.Equal(point, battle.Live(bomb).Position);

      // The factory failure matrix lives in BattleFactoryTest; this protects live occupancy.
      var spawn = new SpawnUnit(TestData.MakeCombatant("X", player), point);
      Assert.Throws<System.InvalidOperationException>(() => battle.Submit(spawn));
    }
  }
}
