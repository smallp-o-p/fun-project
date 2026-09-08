using FunProject.Battle;
using FunProject.Weapons;
using GdUnit4;
using System;
using System.Collections.Generic;

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
  public void ArmedMeleeUnitAllVerbsAvailable()
  {
    using var battle = MakeBattle(TestData.MakeWeapon("Rifle"));
    var actions = battle.Query(new GetAvailableActionsForUnit(battle.SingleAliveUnit(battle.PlayerFaction)));

    Assert.Equal(4, actions.Count);
    Assert.True(actions.AsValueEnumerable().All(action => action.IsAvailable));
    Assert.False(actions.AsValueEnumerable().Any(action => action.Action is ReloadActionDefinition));
  }

  [TestCase(TestName = "Magazine weapon adds a Reload row in catalog order; full mag makes it unavailable")]
  public void MagazineWeaponAddsReloadRow()
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
  public void EmptyMagazineFlipsAttackAndReload()
  {
    using var battle = MakeBattle(TestData.MakeAmmoWeapon("Pistol", magazine: 1));
    AliveUnit hero = battle.SingleAliveUnit(battle.PlayerFaction);
    var weapon = hero.State.EquippedWeapon.Match(
      Some: w => (AmmunitionedWeapon)w,
      None: () => throw new Exception("Hero should be armed."));
    Assert.True(weapon.TrySpendShot().IsSome); // drain the single round

    var actions = battle.Query(new GetAvailableActionsForUnit(hero));

    var attack = Row<AttackActionDefinition>(actions);
    Assert.False(attack.IsAvailable);
    Assert.True(Row<ReloadActionDefinition>(actions).IsAvailable);
  }

  [TestCase(TestName = "Unarmed unit: no Attack or Reload rows, Move available")]
  public void UnarmedUnitOmitsAttackAndReload()
  {
    using var battle = MakeBattle(None);
    var actions = battle.Query(new GetAvailableActionsForUnit(battle.SingleAliveUnit(battle.PlayerFaction)));

    Assert.False(actions.AsValueEnumerable().Any(action => action.Action is AttackActionDefinition));
    Assert.False(actions.AsValueEnumerable().Any(action => action.Action is ReloadActionDefinition));
    Assert.True(Row<MoveActionDefinition>(actions).IsAvailable);
  }

  [TestCase(TestName = "Not-active-side unit: every row unavailable, including EndTurn")]
  public void NotActiveSideAllUnavailable()
  {
    using var battle = MakeBattle(TestData.MakeWeapon("Rifle"));
    var actions = battle.Query(new GetAvailableActionsForUnit(battle.SingleAliveUnit(battle.EnemyFaction))); // enemy is not active at turn 1

    Assert.True(actions.AsValueEnumerable().All(action => !action.IsAvailable));
  }

  [TestCase(TestName = "Boxed-in unit: Move unavailable")]
  public void BoxedInMoveUnavailable()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    var board = new BattleBoardState(new Vector3I(5, 1, 5));
    board.SetTileWalkable(board.ValidatePoint(new Vector3I(1, 0, 0)).RequireSome(), false);
    board.SetTileWalkable(board.ValidatePoint(new Vector3I(0, 0, 1)).RequireSome(), false);
    using var battle = BattleFixture.Started(board,
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Hero", player)), new Vector3I(0, 0, 0)),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Goon", enemy)), new Vector3I(3, 0, 3)));
    AliveUnit hero = battle.SingleAliveUnit(battle.PlayerFaction);

    var move = Row<MoveActionDefinition>(battle.Query(new GetAvailableActionsForUnit(hero)));
    Assert.False(move.IsAvailable);
  }

  [TestCase(TestName = "Out-of-AP unit: only EndTurn is available")]
  public void OutOfApOnlyEndTurnAvailable()
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
  public void UnconsciousUnitHasNoAvailableActions()
  {
    using var battle = MakeBattle(TestData.MakeAmmoWeapon("SMG"));
    AliveUnit hero = battle.SingleAliveUnit(battle.PlayerFaction);
    battle.ApplyDamage(hero.State, 20, DamageKind.Stun);

    var actions = battle.Query(new GetAvailableActionsForUnit(hero));

    Assert.True(actions.AsValueEnumerable().All(action => !action.IsAvailable));
    Assert.False(Row<EndTurnActionDefinition>(actions).IsAvailable);
  }

  [TestCase(TestName = "Unconscious unit has no possible move tiles")]
  public void UnconsciousUnitHasNoPossibleMoveTiles()
  {
    using var battle = MakeBattle(None);
    AliveUnit hero = battle.SingleAliveUnit(battle.PlayerFaction);
    battle.ApplyDamage(hero.State, 20, DamageKind.Stun);

    Assert.Equal(0, battle.Query(new GetPossibleMoveTilesForUnit(hero)).Count);
  }

  [TestCase(TestName = "FindPath remains a geometric query for an unconscious unit")]
  public void FindPathRemainsGeometricForUnconsciousUnit()
  {
    using var battle = MakeBattle(None);
    AliveUnit hero = battle.SingleAliveUnit(battle.PlayerFaction);
    battle.ApplyDamage(hero.State, 20, DamageKind.Stun);

    var path = battle.Query(new FindPathForUnit(hero, battle.At(new Vector3I(1, 0, 0))));

    Vector3I[] expected = [Vector3I.Zero, new Vector3I(1, 0, 0)];
    Assert.True(path.AsValueEnumerable().Select(point => point.Raw).SequenceEqual(expected));
  }

  [TestCase(TestName = "A walkable tile directly above counts as an open neighbor for Move")]
  public void VerticalNeighborKeepsMoveAvailable()
  {
    var player = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    // 2x2x1 board. Hero's only horizontal neighbor is walled off; the tile directly above is
    // open — the same vertical adjacency the pathfinder connects, so Move must stay available.
    var board = new BattleBoardState(new Vector3I(2, 2, 1));
    board.SetTileWalkable(board.ValidatePoint(new Vector3I(1, 0, 0)).RequireSome(), false);
    using var battle = BattleFixture.Started(board,
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Hero", player)), new Vector3I(0, 0, 0)),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Goon", enemy)), new Vector3I(1, 1, 0)));
    AliveUnit hero = battle.SingleAliveUnit(battle.PlayerFaction);

    Assert.True(Row<MoveActionDefinition>(battle.Query(new GetAvailableActionsForUnit(hero))).IsAvailable);
  }
}
