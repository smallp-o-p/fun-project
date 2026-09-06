using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Items.Effects;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;

[TestSuite]
[RequireGodotRuntime]
public class BattleObjectSessionTest
{
  private static BattleRuntime UnwrapStart(Either<BattleSetupFailure, BattleRuntime> result) =>
    result.Match(
      Right: runtime => runtime,
      Left: failure => throw new Exception($"Expected Start to succeed but got {failure.Reason}: {failure.Message}"));

  private static BattleSetupFailure ExpectFailure(Either<BattleSetupFailure, BattleRuntime> result) =>
    result.Match(
      Right: _ => throw new Exception("Expected a setup failure but Start succeeded."),
      Left: failure => failure);

  private static BattleSpecialObjectData MakeBombData()
  {
    var data = new BattleSpecialObjectData { Name = "Bomb" };
    data.Capabilities.Add(new InteractiveCapabilityData { ActionPointCost = 1 });
    return data;
  }

  [TestCase(TestName = "PlaceObject during setup occupies the tile and mints a live proof")]
  public void PlacementBlocksOccupancy()
  {
    var player = TestData.MakeFaction("P");
    var enemy = TestData.MakeFaction("E");
    using var battle = new BattleFixture(new Vector3I(4, 1, 4), [player, enemy]);
    var session = battle.Session;
    battle.Spawn(TestData.MakeCombatant("A", player), new Vector3I(0, 0, 0));
    battle.Spawn(TestData.MakeCombatant("B", enemy), new Vector3I(3, 0, 3));

    BattleBoardState.ValidatedPoint point = battle.At(new Vector3I(2, 0, 2));
    battle.Submit(BattleAction.PlaceObject(MakeBombData(), point));

    BattleObjectState[] objects = [.. session.Objects];
    Assert.Equal(1, objects.Length);
    BattleObjectState bomb = objects[0];
    Assert.True(bomb.Status.IsNone);
    Assert.Equal(point, battle.Live(bomb).Position);
    Assert.Equal(point.Raw, bomb.Position);

    // Occupancy: a unit cannot spawn onto the object tile (a rejected SpawnUnit surfaces
    // from Submit as an InvalidOperationException — trusted parameters).
    var spawn = new SpawnUnit(TestData.MakeCombatant("X", player), point);
    Assert.Throws<System.InvalidOperationException>(() => battle.Submit(spawn));

    // A second object cannot take the same tile.
    Assert.Throws<System.InvalidOperationException>(() => battle.Submit(
      BattleAction.PlaceObject(MakeBombData(), point)));
  }

  [TestCase(TestName = "PlaceObject is rejected once the battle is in progress")]
  public void MidBattlePlacementRejected()
  {
    var player = TestData.MakeFaction("P");
    var enemy = TestData.MakeFaction("E");
    using var battle = BattleFixture.Started(new Vector3I(4, 1, 4),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("A", player)), new Vector3I(0, 0, 0)),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("B", enemy)), new Vector3I(3, 0, 3)));

    BattleBoardState.ValidatedPoint point = battle.At(new Vector3I(2, 0, 2));
    Assert.Throws<System.InvalidOperationException>(() => battle.Submit(
      BattleAction.PlaceObject(MakeBombData(), point)));
    Assert.Equal(0, battle.Query(new GetBattleSpecialObjectsQuery()).Count);
  }

  [TestCase(TestName = "Player faction query returns the started faction")]
  public void PlayerFactionQueryReturnsStartedFaction()
  {
    var player = TestData.MakeFaction("P");
    var enemy = TestData.MakeFaction("E");

    using BattleRuntime runtime = UnwrapStart(BattleFactory.Start(new BattleSetup(
      new BattleBoardState(new Vector3I(4, 1, 4)),
      [player, enemy],
      [
        new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("A", player)), new Vector3I(0, 0, 0)),
        new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("B", enemy)), new Vector3I(3, 0, 3)),
      ],
      new Dictionary<Faction, IReadOnlyList<ObjectiveData>>
      {
        [player] = [new FakeObjectiveData()],
        [enemy] = [new FakeObjectiveData()],
      },
      PlayerFaction: Some(player))));

    Assert.Equal(player, runtime.Query(new GetPlayerFactionQuery()).RequireSome());
  }

  [TestCase(TestName = "Start(setup) places objects and reports typed failures")]
  public void StartPlacesObjectsAndValidates()
  {
    var player = TestData.MakeFaction("P");
    var enemy = TestData.MakeFaction("E");

    // Start mutates the board it is given, so every case builds a FRESH setup + board —
    // never reuse a setup across Start calls.
    BattleSetup SetupWith(IReadOnlyList<ObjectPlacement> objects) => new(
      new BattleBoardState(new Vector3I(4, 1, 4)),
      [player, enemy],
      [
        new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("A", player)), new Vector3I(0, 0, 0)),
        new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("B", enemy)), new Vector3I(3, 0, 3)),
      ],
      new Dictionary<Faction, IReadOnlyList<ObjectiveData>>
      {
        [player] = [new FakeObjectiveData()],
        [enemy] = [new FakeObjectiveData()],
      },
      Objects: objects);

    // Happy path: two objects placed.
    using BattleRuntime runtime = UnwrapStart(BattleFactory.Start(SetupWith(
    [
      new ObjectPlacement(MakeBombData(), new Vector3I(1, 0, 1)),
      new ObjectPlacement(MakeBombData(), new Vector3I(2, 0, 2)),
    ])));
    Assert.Equal(2, runtime.Query(new GetBattleSpecialObjectsQuery()).Count);

    BattleSetupFailure FailureOf(IReadOnlyList<ObjectPlacement> objects) =>
      ExpectFailure(BattleFactory.Start(SetupWith(objects)));

    // Duplicate cell → DuplicateObjectCell.
    Assert.Equal(BattleSetupFailureReason.DuplicateObjectCell,
      FailureOf([new ObjectPlacement(MakeBombData(), new Vector3I(1, 0, 1)),
                 new ObjectPlacement(MakeBombData(), new Vector3I(1, 0, 1))]).Reason);

    // Out of bounds → ObjectCellUnavailable.
    Assert.Equal(BattleSetupFailureReason.ObjectCellUnavailable,
      FailureOf([new ObjectPlacement(MakeBombData(), new Vector3I(9, 0, 9))]).Reason);

    // Onto a unit's spawn cell → ObjectCellUnavailable (not occupiable).
    Assert.Equal(BattleSetupFailureReason.ObjectCellUnavailable,
      FailureOf([new ObjectPlacement(MakeBombData(), new Vector3I(0, 0, 0))]).Reason);
  }
}
