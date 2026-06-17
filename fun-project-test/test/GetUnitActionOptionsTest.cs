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

    IReadOnlyList<UnitActionOption> options = QueryRight(runtime.Query(new GetUnitActionOptions(hero)));

    Assert.True(options.OfType<MoveActionOption>().Single().IsAvailable);
    Assert.True(options.OfType<AttackActionOption>().Single().IsAvailable);
    Assert.True(options.OfType<PassActionOption>().Single().IsAvailable);
    Assert.True(options.OfType<EndTurnActionOption>().Single().IsAvailable);
  }

  [TestCase(TestName = "Unarmed unit has no AttackActionOption in the possible set")]
  public void UnarmedUnitHasNoAttackOption()
  {
    var (runtime, player, _) = MakeBattle(None);
    BattleUnitState hero = UnitOf(runtime, player);

    IReadOnlyList<UnitActionOption> options = QueryRight(runtime.Query(new GetUnitActionOptions(hero)));

    Assert.False(options.OfType<AttackActionOption>().Any());
    Assert.True(options.OfType<MoveActionOption>().Single().IsAvailable);
  }

  [TestCase(TestName = "A not-active-side unit returns the full possible set, all unavailable")]
  public void NotActiveSideAllUnavailable()
  {
    var (runtime, _, enemy) = MakeBattle(BattleTestFactory.MakeWeapon("Rifle"));
    BattleUnitState goon = UnitOf(runtime, enemy); // enemy is not the active side at turn 1

    IReadOnlyList<UnitActionOption> options = QueryRight(runtime.Query(new GetUnitActionOptions(goon)));

    Assert.True(options.Count >= 3);
    Assert.False(options.Any(o => o.IsAvailable));
  }

  [TestCase(TestName = "Boxed-in unit: MoveActionOption present but unavailable")]
  public void BoxedInUnitMoveUnavailable()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var board = new BattleBoardState(new Vector3I(5, 1, 5));
    // Wall off the corner unit's only two in-bounds neighbours so it cannot move.
    board.GetTile(board.ValidatePoint(new Vector3I(1, 0, 0)).RequireSome()).IsWalkable = false;
    board.GetTile(board.ValidatePoint(new Vector3I(0, 0, 1)).RequireSome()).IsWalkable = false;
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

    MoveActionOption move = QueryRight(runtime.Query(new GetUnitActionOptions(hero)))
      .OfType<MoveActionOption>().Single();
    Assert.False(move.IsAvailable);
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

    IReadOnlyList<UnitActionOption> options = QueryRight(runtime.Query(new GetUnitActionOptions(hero)));
    Assert.False(options.OfType<MoveActionOption>().Single().IsAvailable);
    Assert.False(options.OfType<AttackActionOption>().Single().IsAvailable);
    Assert.False(options.OfType<PassActionOption>().Single().IsAvailable);
    Assert.True(options.OfType<EndTurnActionOption>().Single().IsAvailable);
  }
}
