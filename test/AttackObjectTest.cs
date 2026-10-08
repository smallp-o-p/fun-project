using System;
using System.Collections.Generic;
using FunProject.Battle;
using FunProject.Core;
using FunProject.Items.Effects;
using FunProject.Stats;
using FunProject.Weapons;
using GdUnit4;
using Godot;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public partial class AttackObjectTest
{
  private sealed class MustNotCalculateObject : IHitChanceCalculator
  {
    public HitChanceBreakdown Calculate(AttackContext context) =>
      throw new InvalidOperationException("An object shot must bypass the unit calculator.");
  }

  private static Func<BattleSetup, BattleSetup> ObjectsAt(params ObjectPlacement[] placements) =>
    setup => setup with { Objects = placements };

  private static BattleObjectState ObjectAt(BattleFixture battle, Vector3I cell) =>
    battle.Query(new GetBattleSpecialObjectsQuery()).AsValueEnumerable().Single(o => o.Position == cell);

  private static BattleMapData MapWithCoveredCell(Vector3I coverCell)
  {
    List<(Vector3I Cell, BattleMapTileData Tile)> tiles = [];
    for (int x = 0; x < 8; x++)
      for (int z = 0; z < 8; z++)
      {
        var cell = new Vector3I(x, 0, z);
        tiles.Add(cell == coverCell
          ? (cell, new BattleMapTileData { Walkable = true, CoverDirections = CoverDirections.North, CoverAmount = 100 })
          : (cell, TestData.FloorTile()));
      }
    return TestData.MakeMapData(new Vector3I(8, 1, 8), [.. tiles]);
  }

  [TestCase]
  public static void GuaranteedObjectShotSharesPreviewCostsAndEventIdentity()
  {
    var weapon = TestData.MakeAmmoWeapon("Rifle", magazine: 1, damage: 5);
    using var battle = BattleFixture.Duel(
      map: MapWithCoveredCell(new Vector3I(4, 0, 2)),
      hitChanceCalculator: new MustNotCalculateObject(),
      player: new("Shooter", Aim: 0, Weapon: weapon),
      configure: ObjectsAt(new ObjectPlacement(TestData.MakeObject("Crate", 20), new Vector3I(4, 0, 2))));
    var obj = ObjectAt(battle, new Vector3I(4, 0, 2));
    var attackOption = battle.Query(new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)))
      .AsValueEnumerable().Single(option => option.Action is AttackActionDefinition);
    Assert.True(attackOption.IsAvailable);
    var preview = battle.Query(new GetHitChanceForAttack(
      battle.Alive(battle.PlayerUnit), battle.Target(obj))).RequireRight();
    battle.ClearEvents();
    int beforeAp = battle.PlayerUnit.CurrentActionPoints;
    battle.Attack(battle.PlayerUnit, obj);
    Assert.Equal(100, preview.FinalChance);
    Assert.Equal(0, preview.Modifiers.Count);
    Assert.Equal(15, obj.FindCapability<ObjectHealthCapability>().RequireSome().CurrentHealth);
    Assert.Equal(beforeAp - 1, battle.PlayerUnit.CurrentActionPoints);
    Assert.Equal(0, weapon.CurrentAmmo);
    Assert.False(attackOption.IsAvailable);
    var shot = battle.Events.SingleEvent<UnitAttackedBattleEvent>();
    Assert.True(shot.IsHit);
    Assert.Equal(preview.FinalChance, shot.Breakdown.FinalChance);
    Assert.True(shot.Roll >= 0 && shot.Roll < 100);
    Assert.True(ReferenceEquals(obj, ((BattleEntity.BoardObject)shot.Target).State));
    Assert.Equal(Some(battle.PlayerUnit), battle.Events.SingleEvent<ObjectDamagedBattleEvent>().MaybeCause);
    battle.Events.EventBefore<UnitAttackedBattleEvent, ObjectDamagedBattleEvent>();
    battle.ClearEvents();
    battle.Attack(battle.PlayerUnit, obj);
    Assert.Equal(beforeAp - 1, battle.PlayerUnit.CurrentActionPoints);
    Assert.Equal(0, battle.Events.Count);
  }

  [TestCase]
  public static void DestructionIsTerminalAndClearsOccupancyBeforeBroadcast()
  {
    using var battle = BattleFixture.Duel(
      player: new("Shooter", Weapon: TestData.MakeWeapon("Rifle", damage: 99)),
      configure: ObjectsAt(new ObjectPlacement(TestData.MakeObject("Crate", 5), new Vector3I(4, 0, 2))));
    var obj = ObjectAt(battle, new Vector3I(4, 0, 2));
    bool observed = false;
    battle.Runtime.BattleEventCommitted += evt =>
    {
      if (evt is not ObjectDestroyedBattleEvent destroyed) return;
      observed = true;
      Assert.True(ReferenceEquals(obj, destroyed.BoardObject));
      Assert.Equal(Some(ObjectStatus.Destroyed), obj.Status);
      Assert.True(battle.Board.FindObjectPosition(obj.Id).IsNone);
    };
    battle.ClearEvents();
    battle.Attack(battle.PlayerUnit, obj);
    Assert.True(observed);
    Assert.Equal(0, obj.FindCapability<ObjectHealthCapability>().RequireSome().CurrentHealth);
    Assert.True(battle.Runtime.TryGetAttackTarget(new BattleEntity.BoardObject(obj)).IsNone);
    Assert.True(battle.Runtime.TryGetAliveObject(obj).IsNone);
    Assert.Equal(0, battle.Events.EventsOf<ObjectDamagedBattleEvent>().Length);
    Assert.Equal(Some(battle.PlayerUnit), battle.Events.SingleEvent<ObjectDestroyedBattleEvent>().MaybeCause);
    battle.Events.EventBefore<UnitAttackedBattleEvent, ObjectDestroyedBattleEvent>();
    Assert.True(battle.Query(new GetBattleSpecialObjectsQuery()).AsValueEnumerable().Contains(obj));
  }

  [TestCase]
  public static void StunAndStatusPayloadsAreIgnoredForObjects()
  {
    using var battle = BattleFixture.Duel(
      player: new("Stunner", Weapon: TestData.MakeStunWeapon()),
      enemy: new("Burner", Weapon: TestData.MakeStatusWeapon(TestData.MakeBurn(applyChancePercent: 50))),
      configure: ObjectsAt(new ObjectPlacement(TestData.MakeObject("Crate", 20), new Vector3I(4, 0, 2))));
    var obj = ObjectAt(battle, new Vector3I(4, 0, 2));
    battle.ClearEvents();
    battle.Attack(battle.PlayerUnit, obj);
    Assert.Equal(20, obj.FindCapability<ObjectHealthCapability>().RequireSome().CurrentHealth);
    Assert.Equal(1, battle.Events.Count);
    Assert.True(battle.Events.SingleEvent<UnitAttackedBattleEvent>().IsHit);
    // Primitives/preview allow the other side; existing availability belongs to the caller.
    battle.Attack(battle.EnemyUnit, obj);
    Assert.Equal(17, obj.FindCapability<ObjectHealthCapability>().RequireSome().CurrentHealth);
    Assert.Equal(0, battle.Events.EventsOf<UnitStatusEffectAppliedBattleEvent>().Length);
  }

  private sealed class TwoObjectShots(BattleUnitState shooter, BattleObjectState target)
    : BattleHook<UnitMovedBattleEvent>
  {
    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, UnitMovedBattleEvent evt)
    {
      var actor = context.Session.TryGetAlive(shooter).RequireSome();
      var proof = context.Session.TryGetAttackTarget(new BattleEntity.BoardObject(target)).RequireSome();
      return [BattleAction.AttackEntity(actor, proof), BattleAction.AttackEntity(actor, proof)];
    }
  }

  [TestCase]
  public static void EarlierReactionDestroysTargetAndLaterReactionSpendsNothing()
  {
    var weapon = TestData.MakeAmmoWeapon("Rifle", magazine: 2, damage: 5);
    using var battle = BattleFixture.Duel(
      enemy: new("Guard", Weapon: weapon),
      configure: ObjectsAt(new ObjectPlacement(TestData.MakeObject("Crate", 5), new Vector3I(3, 0, 3))));
    var obj = ObjectAt(battle, new Vector3I(3, 0, 3));
    battle.RegisterHook<UnitMovedBattleEvent>(new TwoObjectShots(battle.EnemyUnit, obj));
    battle.ClearEvents();
    int beforeAp = battle.EnemyUnit.CurrentActionPoints;
    battle.Move(battle.PlayerUnit, [new Vector3I(4, 0, 2)]);
    Assert.Equal(1, battle.Events.EventsOf<UnitAttackedBattleEvent>().Length);
    Assert.Equal(1, battle.Events.EventsOf<ObjectDestroyedBattleEvent>().Length);
    Assert.Equal(1, weapon.CurrentAmmo);
    Assert.Equal(beforeAp - 1, battle.EnemyUnit.CurrentActionPoints);
  }

  private sealed class DestroyDuringShot(BattleObjectState obj) : BattleHook<UnitAttackedBattleEvent>
  {
    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, UnitAttackedBattleEvent evt)
    {
      context.Session.ApplyDamageTo(obj, [new Damage(999, Element.Kinetic)], None);
      return [];
    }
  }

  [TestCase]
  public static void ShotHookCanResolveTheTargetBeforeWeaponDamageResumes()
  {
    var weapon = TestData.MakeAmmoWeapon("Rifle", magazine: 2);
    using var battle = BattleFixture.Duel(
      player: new("Shooter", Weapon: weapon),
      configure: ObjectsAt(new ObjectPlacement(TestData.MakeObject("Crate", 20), new Vector3I(4, 0, 2))));
    var obj = ObjectAt(battle, new Vector3I(4, 0, 2));
    battle.RegisterHook<UnitAttackedBattleEvent>(new DestroyDuringShot(obj));
    battle.ClearEvents();
    battle.Attack(battle.PlayerUnit, obj);
    Assert.Equal(1, weapon.CurrentAmmo);
    Assert.Equal(1, battle.Events.EventsOf<ObjectDestroyedBattleEvent>().Length);
    Assert.Equal(0, battle.Events.EventsOf<ObjectDamagedBattleEvent>().Length);
  }

  [TestCase]
  public static void ExploredObjectsStillRequireTheAttackerSightAndRange()
  {
    using var battle = BattleFixture.Duel(configure: ObjectsAt(
      new ObjectPlacement(TestData.MakeObject("Crate", 20), new Vector3I(3, 0, 1))));
    var blindWeapon = TestData.MakeAmmoWeapon("Rifle");
    var shortWeapon = TestData.MakeAmmoWeapon("Pistol", range: 1);
    var blind = battle.Spawn(TestData.MakeCombatant("Blind", battle.PlayerFaction, vision: 1),
      new Vector3I(0, 0, 0), blindWeapon);
    var shortRange = battle.Spawn(TestData.MakeCombatant("Short", battle.PlayerFaction),
      new Vector3I(0, 0, 1), shortWeapon);
    var unarmed = battle.Spawn(TestData.MakeCombatant("Unarmed", battle.PlayerFaction),
      new Vector3I(1, 0, 0));
    var obj = ObjectAt(battle, new Vector3I(3, 0, 1));
    Assert.True(battle.Query(new GetFactionVisibleObjectsQuery(battle.PlayerFaction)).AsValueEnumerable().Contains(obj));
    foreach (var actor in new[] { blind, shortRange, unarmed })
    {
      int ap = actor.CurrentActionPoints;
      battle.ClearEvents();
      Assert.True(battle.Query(new GetHitChanceForAttack(battle.Alive(actor), battle.Target(obj))).IsLeft);
      battle.Attack(actor, obj);
      Assert.Equal(ap, actor.CurrentActionPoints);
      Assert.Equal(0, battle.Events.Count);
    }
    Assert.Equal(6, blindWeapon.CurrentAmmo);
    Assert.Equal(6, shortWeapon.CurrentAmmo);
  }

  [TestCase]
  public static void ObjectRangeIncludesTheBoundaryAndExcludesBeyondIt()
  {
    using var battle = BattleFixture.Duel(
      player: new("Shooter", Weapon: TestData.MakeWeapon("Rifle", range: 2)),
      configure: ObjectsAt(
        new ObjectPlacement(TestData.MakeObject("Boundary", 20), new Vector3I(2, 0, 1)),
        new ObjectPlacement(TestData.MakeObject("Beyond", 20), new Vector3I(1, 0, 1))));
    var boundary = ObjectAt(battle, new Vector3I(2, 0, 1));
    var beyond = ObjectAt(battle, new Vector3I(1, 0, 1));
    Assert.True(battle.Query(new GetHitChanceForAttack(battle.Alive(battle.PlayerUnit), battle.Target(boundary))).IsRight);
    Assert.True(battle.Query(new GetHitChanceForAttack(battle.Alive(battle.PlayerUnit), battle.Target(beyond))).IsLeft);
  }

  [TestCase]
  public static void IgnoredObjectStatusesDoNotConsumeExtraRandomRolls()
  {
    List<int> Rolls(Weapon weapon)
    {
      using var battle = BattleFixture.Duel(randomSeed: 19,
        player: new("Shooter", Weapon: weapon),
        configure: ObjectsAt(new ObjectPlacement(TestData.MakeObject("Crate", 20), new Vector3I(3, 0, 2))));
      var obj = ObjectAt(battle, new Vector3I(3, 0, 2));
      battle.ClearEvents();
      battle.Attack(battle.PlayerUnit, obj);
      battle.Attack(battle.PlayerUnit, battle.EnemyUnit);
      return battle.Events.EventsOf<UnitAttackedBattleEvent>().AsValueEnumerable()
        .Select(evt => evt.Roll).ToList();
    }
    var plain = Rolls(TestData.MakeWeapon("Rifle", damage: 3));
    var withStatus = Rolls(TestData.MakeStatusWeapon(TestData.MakeBurn(applyChancePercent: 50)));
    Assert.True(plain.AsValueEnumerable().SequenceEqual(withStatus));
  }

  [TestCase]
  public static void ObjectDamageRejectsInvalidTargetsAndSeparatesHealthFromStun()
  {
    using var battle = BattleFixture.Duel(configure: ObjectsAt(
      new ObjectPlacement(TestData.MakeObject("Crate", 10), new Vector3I(3, 0, 2)),
      new ObjectPlacement(TestData.MakeObject(), new Vector3I(2, 0, 2))));
    using var foreign = BattleFixture.Duel();
    var obj = ObjectAt(battle, new Vector3I(3, 0, 2));
    var scenery = ObjectAt(battle, new Vector3I(2, 0, 2));
    Assert.Throws<InvalidOperationException>(() => battle.Session.ApplyDamageTo(scenery, [], None));
    Assert.Throws<InvalidOperationException>(() => foreign.Session.ApplyDamageTo(obj, [], None));
    Assert.Throws<ArgumentNullException>(() => battle.Session.ApplyDamageTo(obj, null!, None));
    battle.ClearEvents();
    battle.Session.ApplyDamageTo(obj,
      [new Damage(7, Element.Kinetic, Kind: DamageKind.Stun), new Damage(3, Element.Thermal)], None);
    Assert.Equal(7, obj.FindCapability<ObjectHealthCapability>().RequireSome().CurrentHealth);
    Assert.Equal(3, battle.Events.SingleEvent<ObjectDamagedBattleEvent>().HealthDamage);
    battle.Session.ApplyDamageTo(obj, [new Damage(999, Element.Kinetic)], None);
    Assert.Throws<InvalidOperationException>(() =>
      battle.Session.ApplyDamageTo(obj, [new Damage(1, Element.Kinetic)], None));
    Assert.Equal(1, battle.Events.EventsOf<ObjectDestroyedBattleEvent>().Length);
  }
}
