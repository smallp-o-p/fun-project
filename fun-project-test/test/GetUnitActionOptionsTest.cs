using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Tests;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

[TestSuite]
[RequireGodotRuntime]
public class GetUnitActionOptionsTest
{
  private static T QueryRight<T>(Either<BattleQueryFailure, T> result) =>
    result.Match(Right: v => v, Left: f => throw new Exception($"Query failed: {f.Message}"));

  // 5x1x5 open board. Player "Hero" at (0,0,0); a second player slot lets us test an unarmed unit.
  // Enemy "Goon" at (3,0,0) (visible, within default weapon range 10).
  private static (BattleRuntime Runtime, Faction Player, Faction Enemy) MakeBattle(
    Option<Weapon> heroWeapon)
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var board = new BattleBoardState(new Vector3I(5, 1, 5));
    var placements = new List<UnitPlacement>
    {
      new(new UnitLoadout(BattleTestFactory.MakeCombatant("Hero", player), heroWeapon), new Vector3I(0, 0, 0)),
      new(new UnitLoadout(BattleTestFactory.MakeCombatant("Goon", enemy)), new Vector3I(3, 0, 0)),
    };
    var objectives = new Dictionary<Faction, IReadOnlyList<Objective>>
    {
      [player] = new Objective[] { new FakeObjective() },
      [enemy] = new Objective[] { new FakeObjective() },
    };
    var runtime = BattleFactory.Start(new BattleSetup(board, new[] { player, enemy }, placements, objectives))
      .Match(Right: r => r, Left: f => throw new Exception($"Setup failed: {f.Message}"));
    return (runtime, player, enemy);
  }

  private static BattleUnitState UnitOf(BattleRuntime runtime, Faction faction) =>
    QueryRight(runtime.Query(new GetFactionAliveUnits(faction))).Single();

  [TestCase(TestName = "Armed unit with an enemy in range: Move, Attack, Pass, EndTurn all available")]
  public void ArmedUnitHasAllVerbsAvailable()
  {
    var (runtime, player, _) = MakeBattle(BattleTestFactory.MakeWeapon("Rifle"));
    BattleUnitState hero = UnitOf(runtime, player);

    UnitActionAvailability availability = QueryRight(runtime.Query(new GetUnitActionOptions(hero)));

    Assert.True(availability.CanMove);
    Assert.True(availability.CanAttack);
    Assert.True(availability.CanPass);
    Assert.True(availability.CanEndTurn);
  }

  [TestCase(TestName = "Unarmed unit: CanAttack is false (no weapon), Move still available")]
  public void UnarmedUnitCannotAttack()
  {
    var (runtime, player, _) = MakeBattle(None);
    BattleUnitState hero = UnitOf(runtime, player);

    UnitActionAvailability availability = QueryRight(runtime.Query(new GetUnitActionOptions(hero)));

    Assert.False(availability.CanAttack);
    Assert.True(availability.CanMove);
  }

  [TestCase(TestName = "A not-active-side unit: no verb is available")]
  public void NotActiveSideAllUnavailable()
  {
    var (runtime, _, enemy) = MakeBattle(BattleTestFactory.MakeWeapon("Rifle"));
    BattleUnitState goon = UnitOf(runtime, enemy); // enemy is not the active side at turn 1

    UnitActionAvailability availability = QueryRight(runtime.Query(new GetUnitActionOptions(goon)));

    Assert.False(availability.CanMove);
    Assert.False(availability.CanAttack);
    Assert.False(availability.CanPass);
    Assert.False(availability.CanEndTurn);
  }

  [TestCase(TestName = "Boxed-in unit: CanMove is false")]
  public void BoxedInUnitMoveUnavailable()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var board = new BattleBoardState(new Vector3I(5, 1, 5));
    // Wall off the corner unit's only two in-bounds neighbours so it cannot move.
    board.SetTileWalkable(board.ValidatePoint(new Vector3I(1, 0, 0)).RequireSome(), false);
    board.SetTileWalkable(board.ValidatePoint(new Vector3I(0, 0, 1)).RequireSome(), false);
    var placements = new List<UnitPlacement>
    {
      new(new UnitLoadout(BattleTestFactory.MakeCombatant("Hero", player)), new Vector3I(0, 0, 0)),
      new(new UnitLoadout(BattleTestFactory.MakeCombatant("Goon", enemy)), new Vector3I(3, 0, 3)),
    };
    var objectives = new Dictionary<Faction, IReadOnlyList<Objective>>
    {
      [player] = new Objective[] { new FakeObjective() },
      [enemy] = new Objective[] { new FakeObjective() },
    };
    var runtime = BattleFactory.Start(new BattleSetup(board, new[] { player, enemy }, placements, objectives))
      .Match(Right: r => r, Left: f => throw new Exception($"Setup failed: {f.Message}"));
    BattleUnitState hero = QueryRight(runtime.Query(new GetFactionAliveUnits(player))).Single();

    UnitActionAvailability availability = QueryRight(runtime.Query(new GetUnitActionOptions(hero)));
    Assert.False(availability.CanMove);
  }

  [TestCase(TestName = "Out-of-AP unit: only EndTurn is available")]
  public void OutOfApOnlyEndTurnAvailable()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var board = new BattleBoardState(new Vector3I(5, 1, 5));
    var placements = new List<UnitPlacement>
    {
      new(new UnitLoadout(BattleTestFactory.MakeCombatant("Hero", player, actionPoints: 0), BattleTestFactory.MakeWeapon("Rifle")), new Vector3I(0, 0, 0)),
      new(new UnitLoadout(BattleTestFactory.MakeCombatant("Goon", enemy)), new Vector3I(3, 0, 0)),
    };
    var objectives = new Dictionary<Faction, IReadOnlyList<Objective>>
    {
      [player] = new Objective[] { new FakeObjective() },
      [enemy] = new Objective[] { new FakeObjective() },
    };
    var runtime = BattleFactory.Start(new BattleSetup(board, new[] { player, enemy }, placements, objectives))
      .Match(Right: r => r, Left: f => throw new Exception($"Setup failed: {f.Message}"));
    BattleUnitState hero = QueryRight(runtime.Query(new GetFactionAliveUnits(player))).Single();

    UnitActionAvailability availability = QueryRight(runtime.Query(new GetUnitActionOptions(hero)));
    Assert.False(availability.CanMove);
    Assert.False(availability.CanAttack);
    Assert.False(availability.CanPass);
    Assert.True(availability.CanEndTurn);
  }
}
