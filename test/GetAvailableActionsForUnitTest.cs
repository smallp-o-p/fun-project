using FunProject.Battle;
using FunProject.Weapons;
using GdUnit4;
using System;
using System.Collections.Generic;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public class GetAvailableActionsForUnitTest
{
  // 5x1x5 open board. Player "Hero" at (0,0,0); enemy "Goon" at (3,0,0) (visible, in range 10).
  private static BattleFixture MakeBattle(Option<Weapon> heroWeapon)
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    return BattleFixture.Started(new Vector3I(5, 1, 5),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Hero", player), heroWeapon), new Vector3I(0, 0, 0)),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Goon", enemy)), new Vector3I(3, 0, 0)));
  }

  private static UnitAction Row<TDefinition>(IReadOnlyList<UnitAction> actions)
    where TDefinition : UnitActionDefinition =>
    actions.AsValueEnumerable().Single(action => action.Action is TDefinition);

  [TestCase(TestName = "Armed melee unit: Move, Attack, Pass, EndTurn rows, all available, no Reload row")]
  public static void ArmedMeleeUnitAllVerbsAvailable()
  {
    using var battle = MakeBattle(TestData.MakeWeapon("Rifle"));
    var actions = battle.Query(new GetAvailableActionsForUnit(battle.SingleAliveUnit(battle.PlayerFaction)));

    Assert.Equal(4, actions.Count);
    Assert.True(actions.AsValueEnumerable().All(action => action.IsAvailable));
    Assert.False(actions.AsValueEnumerable().Any(action => action.Action is ReloadActionDefinition));
  }

  [TestCase(TestName = "Magazine weapon adds a Reload row in catalog order; full mag makes it unavailable")]
  public static void MagazineWeaponAddsReloadRow()
  {
    using var battle = MakeBattle(TestData.MakeAmmoWeapon("SMG"));
    var actions = battle.Query(new GetAvailableActionsForUnit(battle.SingleAliveUnit(battle.PlayerFaction)));

    Assert.Equal(5, actions.Count);
    Assert.True(actions[0].Action is MoveActionDefinition);
    Assert.True(actions[1].Action is AttackActionDefinition);
    Assert.True(actions[2].Action is ReloadActionDefinition);
    Assert.True(actions[3].Action is PassActionDefinition);
    Assert.True(actions[4].Action is EndTurnActionDefinition);

    Assert.True(Row<AttackActionDefinition>(actions).IsAvailable);
    var reload = Row<ReloadActionDefinition>(actions);
    Assert.False(reload.IsAvailable);
  }

  [TestCase(TestName = "Empty magazine: Attack unavailable, Reload available")]
  public static void EmptyMagazineFlipsAttackAndReload()
  {
    using var battle = MakeBattle(TestData.MakeAmmoWeapon("Pistol", magazine: 1));
    AliveUnit hero = battle.SingleAliveUnit(battle.PlayerFaction);
    var weapon = (AmmunitionedWeapon)hero.State.EquippedWeapon.RequireSome("Hero should be armed.");
    Assert.True(weapon.TrySpendShot().IsSome); // drain the single round

    var actions = battle.Query(new GetAvailableActionsForUnit(hero));

    var attack = Row<AttackActionDefinition>(actions);
    Assert.False(attack.IsAvailable);
    Assert.True(Row<ReloadActionDefinition>(actions).IsAvailable);
  }

  [TestCase(TestName = "Unarmed unit: no Attack or Reload rows, Move available")]
  public static void UnarmedUnitOmitsAttackAndReload()
  {
    using var battle = MakeBattle(None);
    var actions = battle.Query(new GetAvailableActionsForUnit(battle.SingleAliveUnit(battle.PlayerFaction)));

    Assert.False(actions.AsValueEnumerable().Any(action => action.Action is AttackActionDefinition));
    Assert.False(actions.AsValueEnumerable().Any(action => action.Action is ReloadActionDefinition));
    Assert.True(Row<MoveActionDefinition>(actions).IsAvailable);
  }

  [TestCase(TestName = "Not-active-side unit: every row unavailable, including EndTurn")]
  public static void NotActiveSideAllUnavailable()
  {
    using var battle = MakeBattle(TestData.MakeWeapon("Rifle"));
    var actions = battle.Query(new GetAvailableActionsForUnit(battle.SingleAliveUnit(battle.EnemyFaction))); // enemy is not active at turn 1

    Assert.True(actions.AsValueEnumerable().All(action => !action.IsAvailable));
  }

  [TestCase(TestName = "Boxed-in unit: Move available with no reachable destinations")]
  public static void BoxedInMoveAvailableWithoutDestinations()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    using var battle = BattleFixture.Started(
      TestData.MakeMapWithWalls(new Vector3I(5, 1, 5), new Vector3I(1, 0, 0), new Vector3I(0, 0, 1)),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Hero", player)), new Vector3I(0, 0, 0)),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Goon", enemy)), new Vector3I(3, 0, 3)));
    AliveUnit hero = battle.SingleAliveUnit(battle.PlayerFaction);

    var move = Row<MoveActionDefinition>(battle.Query(new GetAvailableActionsForUnit(hero)));
    Assert.True(move.IsAvailable);
    Assert.Equal(0, battle.Query(new GetPossibleMoveTilesForUnit(hero)).Count);
  }

  [TestCase(TestName = "Out-of-AP unit: only EndTurn is available")]
  public static void OutOfApOnlyEndTurnAvailable()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    using var battle = BattleFixture.Started(new Vector3I(5, 1, 5),
      new UnitPlacement(new UnitLoadout(
        TestData.MakeCombatant("Hero", player, actionPoints: 0), TestData.MakeWeapon("Rifle")), new Vector3I(0, 0, 0)),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Goon", enemy)), new Vector3I(3, 0, 0)));
    AliveUnit hero = battle.SingleAliveUnit(battle.PlayerFaction);

    var actions = battle.Query(new GetAvailableActionsForUnit(hero));
    Assert.False(Row<MoveActionDefinition>(actions).IsAvailable);
    Assert.False(Row<AttackActionDefinition>(actions).IsAvailable);
    Assert.False(Row<PassActionDefinition>(actions).IsAvailable);
    Assert.True(Row<EndTurnActionDefinition>(actions).IsAvailable);
  }

  [TestCase(TestName = "Unconscious unit has no available catalog actions, including EndTurn")]
  public static void UnconsciousUnitHasNoAvailableActions()
  {
    using var battle = MakeBattle(TestData.MakeAmmoWeapon("SMG"));
    AliveUnit hero = battle.SingleAliveUnit(battle.PlayerFaction);
    battle.ApplyDamage(hero.State, 20, DamageKind.Stun);
    hero = battle.SingleAliveUnit(battle.PlayerFaction);

    var actions = battle.Query(new GetAvailableActionsForUnit(hero));

    Assert.True(actions.AsValueEnumerable().All(action => !action.IsAvailable));
    Assert.False(Row<EndTurnActionDefinition>(actions).IsAvailable);
  }

  [TestCase(TestName = "An unconscious unit has no possible move tiles while FindPath stays geometric")]
  public static void UnconsciousUnitSpatialQueries()
  {
    using var battle = MakeBattle(None);
    AliveUnit hero = battle.SingleAliveUnit(battle.PlayerFaction);
    battle.ApplyDamage(hero.State, 20, DamageKind.Stun);
    hero = battle.SingleAliveUnit(battle.PlayerFaction);

    Assert.Equal(0, battle.Query(new GetPossibleMoveTilesForUnit(hero)).Count);
    var path = battle.Query(new FindPathForUnit(hero, battle.At(new Vector3I(1, 0, 0))));
    Vector3I[] expected = [Vector3I.Zero, new Vector3I(1, 0, 0)];
    Assert.True(path.AsValueEnumerable().Select(point => point.Raw).SequenceEqual(expected));
  }

  [TestCase(TestName = "A walkable tile directly above is reachable for Move")]
  public static void VerticalNeighborIsReachable()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    // 2x2x1 board. Hero's only horizontal neighbor is walled off; the tile directly above is
    // open — the same vertical adjacency the pathfinder connects, so it must be reachable.
    using var battle = BattleFixture.Started(
      TestData.MakeMapWithWalls(new Vector3I(2, 2, 1), new Vector3I(1, 0, 0)),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Hero", player)), new Vector3I(0, 0, 0)),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Goon", enemy)), new Vector3I(1, 1, 0)));
    AliveUnit hero = battle.SingleAliveUnit(battle.PlayerFaction);

    var reachable = battle.Query(new GetPossibleMoveTilesForUnit(hero));
    Assert.True(reachable.AsValueEnumerable().Select(point => point.Raw).Contains(new Vector3I(0, 1, 0)));
  }
}
