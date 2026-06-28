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
public class PlayerActionControllerTest
{
  private static T QueryRight<T>(Either<BattleQueryFailure, T> result) =>
    result.Match(Right: v => v, Left: f => throw new Exception($"Query failed: {f.Message}"));

  // 5x1x5 open board. Player "Hero" (armed) at (0,0,0), enemy "Goon" at (3,0,0).
  private static (BattleRuntime Runtime, Faction Player, BattleUnitState Hero) MakeArmedBattle() =>
    MakeBattle(BattleTestFactory.MakeWeapon("Rifle"));

  // 5x1x5 open board. Player "Hero" at (0,0,0) with the given weapon (or none), enemy "Goon" at (3,0,0).
  private static (BattleRuntime Runtime, Faction Player, BattleUnitState Hero) MakeBattle(Option<Weapon> heroWeapon)
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
    var hero = QueryRight(runtime.Query(new GetFactionAliveUnits(player))).Single();
    return (runtime, player, hero);
  }

  private static T OptionOf<T>(PlayerActionController c) where T : UnitActionOption =>
    c.ActionOptions.OfType<T>().Single();

  [TestCase(TestName = "Selecting a unit caches its available action options")]
  public void SelectCachesOptions()
  {
    var (runtime, player, _) = MakeArmedBattle();
    var c = new PlayerActionController(runtime, player);

    Assert.True(c.TrySelectUnitAt(new Vector3I(0, 0, 0)));
    Assert.True(OptionOf<MoveActionOption>(c).IsAvailable);
    Assert.True(OptionOf<AttackActionOption>(c).IsAvailable);
  }

  [TestCase(TestName = "Unarmed unit: presentation builds no AttackActionOption")]
  public void UnarmedUnitHasNoAttackOption()
  {
    var (runtime, player, _) = MakeBattle(None);
    var c = new PlayerActionController(runtime, player);

    Assert.True(c.TrySelectUnitAt(new Vector3I(0, 0, 0)));
    Assert.False(c.ActionOptions.OfType<AttackActionOption>().Any());
    Assert.True(OptionOf<MoveActionOption>(c).IsAvailable);
  }

  [TestCase(TestName = "BeginAction(Move) -> targeting -> Confirm relocates the unit")]
  public void MoveFlowRelocates()
  {
    var (runtime, player, hero) = MakeArmedBattle();
    var c = new PlayerActionController(runtime, player);
    c.TrySelectUnitAt(new Vector3I(0, 0, 0));

    c.BeginAction(OptionOf<MoveActionOption>(c));
    Assert.Equal(PlayerActionController.TargetingMode.ActionTargeting, c.Mode);
    Assert.True(c.SetPending(new Vector3I(2, 0, 1)));
    Assert.Equal(PlayerActionController.TargetingMode.ActionPending, c.Mode);
    c.Confirm();

    Assert.Equal(new Vector3I(2, 0, 1), QueryRight(runtime.Query(new GetUnitPosition(hero))).Raw);
    Assert.Equal(PlayerActionController.TargetingMode.None, c.Mode);
    Assert.True(c.SelectedUnit.IsSome);
  }

  [TestCase(TestName = "BeginAction(Attack) -> targeting -> Confirm resolves an attack")]
  public void AttackFlowResolves()
  {
    var (runtime, player, _) = MakeArmedBattle();
    var c = new PlayerActionController(runtime, player);
    c.TrySelectUnitAt(new Vector3I(0, 0, 0));

    c.BeginAction(OptionOf<AttackActionOption>(c));
    Assert.Equal(PlayerActionController.TargetingMode.ActionTargeting, c.Mode);
    ActionPreview preview = QueryRight(c.PreviewAt(new Vector3I(3, 0, 0)));
    Assert.True(preview is AttackPreview);
    Assert.True(c.SetPending(new Vector3I(3, 0, 0)));
    var results = c.Confirm();

    Assert.True(results.Single().Succeeded);
    Assert.Equal(PlayerActionController.TargetingMode.None, c.Mode);
  }

  [TestCase(TestName = "BeginAction(Pass) submits immediately and stays selected")]
  public void PassSubmitsImmediately()
  {
    var (runtime, player, hero) = MakeArmedBattle();
    var c = new PlayerActionController(runtime, player);
    c.TrySelectUnitAt(new Vector3I(0, 0, 0));

    c.BeginAction(OptionOf<PassActionOption>(c));

    Assert.Equal(PlayerActionController.TargetingMode.None, c.Mode);
    // After passing, the unit is no longer available this turn.
    Assert.False(QueryRight(runtime.Query(new CanUnitActNow(hero))));
  }

  [TestCase(TestName = "Cancel steps back one level")]
  public void CancelStepsBack()
  {
    var (runtime, player, _) = MakeArmedBattle();
    var c = new PlayerActionController(runtime, player);
    c.TrySelectUnitAt(new Vector3I(0, 0, 0));
    c.BeginAction(OptionOf<MoveActionOption>(c));
    c.SetPending(new Vector3I(2, 0, 1));

    c.Cancel(); // ActionPending -> ActionTargeting
    Assert.Equal(PlayerActionController.TargetingMode.ActionTargeting, c.Mode);
    c.Cancel(); // ActionTargeting -> None (still selected)
    Assert.Equal(PlayerActionController.TargetingMode.None, c.Mode);
    Assert.True(c.SelectedUnit.IsSome);
  }
}
