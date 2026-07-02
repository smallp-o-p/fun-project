using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Weapons;
using GdUnit4;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

[TestSuite]
[RequireGodotRuntime]
public class GetAvailableActionsForUnitTest
{
  // 5x1x5 open board. Player "Hero" at (0,0,0); enemy "Goon" at (3,0,0) (visible, in range 10).
  private static (BattleRuntime Runtime, Faction Player, Faction Enemy) MakeBattle(Option<Weapon> heroWeapon)
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var runtime = StartRuntime(
      new Vector3I(5, 1, 5),
      new StartPlacement(player, BattleTestFactory.MakeCombatant("Hero", player), new Vector3I(0, 0, 0), heroWeapon),
      new StartPlacement(enemy, BattleTestFactory.MakeCombatant("Goon", enemy), new Vector3I(3, 0, 0)));
    return (runtime, player, enemy);
  }

  private static IReadOnlyList<AvailableUnitAction> ActionsOf(BattleRuntime runtime, BattleUnitState unit) =>
    GetValue(runtime.Query(new GetAvailableActionsForUnit(unit)));

  private static AvailableUnitAction Row<TDefinition>(IReadOnlyList<AvailableUnitAction> actions)
    where TDefinition : UnitActionDefinition =>
    actions.Single(action => action.Action is TDefinition);

  [TestCase(TestName = "Armed melee unit: Move, Attack, Pass, EndTurn rows, all available, no Reload row")]
  public void ArmedMeleeUnitAllVerbsAvailable()
  {
    var (runtime, player, _) = MakeBattle(BattleTestFactory.MakeWeapon("Rifle"));
    var actions = ActionsOf(runtime, SingleAliveUnit(runtime, player));

    Assert.Equal(4, actions.Count);
    Assert.True(actions.All(action => action.IsAvailable));
    Assert.False(actions.Any(action => action.Action is ReloadActionDefinition));
  }

  [TestCase(TestName = "Magazine weapon adds a Reload row in catalog order; full mag makes it unavailable with CanReloadCondition")]
  public void MagazineWeaponAddsReloadRow()
  {
    var (runtime, player, _) = MakeBattle(BattleTestFactory.MakeAmmoWeapon("SMG"));
    var actions = ActionsOf(runtime, SingleAliveUnit(runtime, player));

    Assert.Equal(5, actions.Count);
    Assert.True(actions[0].Action is MoveActionDefinition);
    Assert.True(actions[1].Action is AttackActionDefinition);
    Assert.True(actions[2].Action is ReloadActionDefinition);
    Assert.True(actions[3].Action is PassActionDefinition);
    Assert.True(actions[4].Action is EndTurnActionDefinition);

    Assert.True(Row<AttackActionDefinition>(actions).IsAvailable);
    var reload = Row<ReloadActionDefinition>(actions);
    Assert.False(reload.IsAvailable);
    Assert.True(reload.FailedCondition.Match(c => c is CanReloadCondition, () => false));
  }

  [TestCase(TestName = "Empty magazine: Attack unavailable with WeaponIsLoadedCondition, Reload available")]
  public void EmptyMagazineFlipsAttackAndReload()
  {
    var (runtime, player, _) = MakeBattle(BattleTestFactory.MakeAmmoWeapon("Pistol", magazine: 1));
    BattleUnitState hero = SingleAliveUnit(runtime, player);
    var weapon = hero.EquippedWeapon.Match(
      Some: w => (AmmunitionedWeapon)w,
      None: () => throw new Exception("Hero should be armed."));
    Assert.True(weapon.TrySpendShot().IsSome); // drain the single round

    var actions = ActionsOf(runtime, hero);

    var attack = Row<AttackActionDefinition>(actions);
    Assert.False(attack.IsAvailable);
    Assert.True(attack.FailedCondition.Match(c => c is WeaponIsLoadedCondition, () => false));
    Assert.True(Row<ReloadActionDefinition>(actions).IsAvailable);
  }

  [TestCase(TestName = "Unarmed unit: no Attack or Reload rows, Move available")]
  public void UnarmedUnitOmitsAttackAndReload()
  {
    var (runtime, player, _) = MakeBattle(None);
    var actions = ActionsOf(runtime, SingleAliveUnit(runtime, player));

    Assert.False(actions.Any(action => action.Action is AttackActionDefinition));
    Assert.False(actions.Any(action => action.Action is ReloadActionDefinition));
    Assert.True(Row<MoveActionDefinition>(actions).IsAvailable);
  }

  [TestCase(TestName = "Not-active-side unit: every row unavailable, EndTurn failing IsActiveSideCondition")]
  public void NotActiveSideAllUnavailable()
  {
    var (runtime, _, enemy) = MakeBattle(BattleTestFactory.MakeWeapon("Rifle"));
    var actions = ActionsOf(runtime, SingleAliveUnit(runtime, enemy)); // enemy is not active at turn 1

    Assert.True(actions.All(action => !action.IsAvailable));
    Assert.True(Row<MoveActionDefinition>(actions).FailedCondition.Match(c => c is UnitCanActNowCondition, () => false));
    Assert.True(Row<EndTurnActionDefinition>(actions).FailedCondition.Match(c => c is IsActiveSideCondition, () => false));
  }

  [TestCase(TestName = "Boxed-in unit: Move unavailable with HasOpenAdjacentTileCondition")]
  public void BoxedInMoveUnavailable()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var board = new BattleBoardState(new Vector3I(5, 1, 5));
    board.SetTileWalkable(board.ValidatePoint(new Vector3I(1, 0, 0)).RequireSome(), false);
    board.SetTileWalkable(board.ValidatePoint(new Vector3I(0, 0, 1)).RequireSome(), false);
    var runtime = StartRuntime(
      board,
      new StartPlacement(player, BattleTestFactory.MakeCombatant("Hero", player), new Vector3I(0, 0, 0)),
      new StartPlacement(enemy, BattleTestFactory.MakeCombatant("Goon", enemy), new Vector3I(3, 0, 3)));
    BattleUnitState hero = SingleAliveUnit(runtime, player);

    var move = Row<MoveActionDefinition>(ActionsOf(runtime, hero));
    Assert.False(move.IsAvailable);
    Assert.True(move.FailedCondition.Match(c => c is HasOpenAdjacentTileCondition, () => false));
  }

  [TestCase(TestName = "Out-of-AP unit: only EndTurn is available")]
  public void OutOfApOnlyEndTurnAvailable()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    var runtime = StartRuntime(
      new Vector3I(5, 1, 5),
      new StartPlacement(player, BattleTestFactory.MakeCombatant("Hero", player, actionPoints: 0), new Vector3I(0, 0, 0), BattleTestFactory.MakeWeapon("Rifle")),
      new StartPlacement(enemy, BattleTestFactory.MakeCombatant("Goon", enemy), new Vector3I(3, 0, 0)));
    BattleUnitState hero = SingleAliveUnit(runtime, player);

    var actions = ActionsOf(runtime, hero);
    Assert.False(Row<MoveActionDefinition>(actions).IsAvailable);
    Assert.False(Row<AttackActionDefinition>(actions).IsAvailable);
    Assert.False(Row<PassActionDefinition>(actions).IsAvailable);
    Assert.True(Row<EndTurnActionDefinition>(actions).IsAvailable);
  }

  [TestCase(TestName = "A walkable tile directly above counts as an open neighbor for Move")]
  public void VerticalNeighborKeepsMoveAvailable()
  {
    var player = BattleTestFactory.MakeFaction("Player");
    var enemy = BattleTestFactory.MakeFaction("Enemy");
    // 2x2x1 board. Hero's only horizontal neighbor is walled off; the tile directly above is
    // open — the same vertical adjacency the pathfinder connects, so Move must stay available.
    var board = new BattleBoardState(new Vector3I(2, 2, 1));
    board.SetTileWalkable(board.ValidatePoint(new Vector3I(1, 0, 0)).RequireSome(), false);
    var runtime = StartRuntime(
      board,
      new StartPlacement(player, BattleTestFactory.MakeCombatant("Hero", player), new Vector3I(0, 0, 0)),
      new StartPlacement(enemy, BattleTestFactory.MakeCombatant("Goon", enemy), new Vector3I(1, 1, 0)));
    BattleUnitState hero = SingleAliveUnit(runtime, player);

    Assert.True(Row<MoveActionDefinition>(ActionsOf(runtime, hero)).IsAvailable);
  }

  [TestCase(TestName = "A dead unit fails the query")]
  public void DeadUnitQueryFails()
  {
    var (runtime, player, _) = MakeBattle(BattleTestFactory.MakeWeapon("Rifle"));
    BattleUnitState hero = SingleAliveUnit(runtime, player);
    runtime.ExecuteAction(BattleAction.ApplyDamage(hero, 999));

    var result = runtime.Query(new GetAvailableActionsForUnit(hero));

    Assert.True(result.IsLeft);
  }
}
