using System;
using System.Collections.Generic;
using FunProject.Battle;
using FunProject.Core;
using FunProject.Items.Effects;
using FunProject.Stats;
using FunProject.Weapons;
using GdUnit4;
using Godot;

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
  public void GuaranteedObjectShotSharesPreviewCostsAndEventIdentity()
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
    battle.Read(() => Assert.True(attackOption.IsAvailable));
    var preview = battle.Query(new GetHitChanceForAttack(
      battle.Alive(battle.PlayerUnit), battle.Target(obj))).RequireRight();
    battle.ClearEvents();
    int beforeAp = battle.Read(() => battle.PlayerUnit.CurrentActionPoints);
    battle.Attack(battle.PlayerUnit, obj);
    battle.Read(() => Assert.Equal(100, preview.FinalChance));
    battle.Read(() => Assert.Equal(0, preview.Modifiers.Count));
    battle.Read(() => Assert.Equal(15, obj.FindCapability<ObjectHealthCapability>().RequireSome().CurrentHealth));
    battle.Read(() => Assert.Equal(beforeAp - 1, battle.PlayerUnit.CurrentActionPoints));
    battle.Read(() => Assert.Equal(0, weapon.CurrentAmmo));
    battle.Read(() => Assert.False(attackOption.IsAvailable));
    var shot = battle.Events.SingleEvent<UnitAttackedBattleEvent>();
    battle.Read(() => Assert.True(shot.IsHit));
    battle.Read(() => Assert.Equal(preview.FinalChance, shot.Breakdown.FinalChance));
    battle.Read(() => Assert.True(shot.Roll >= 0 && shot.Roll < 100));
    battle.Read(() => Assert.True(ReferenceEquals(obj, ((BattleEntity.Object)shot.Target).State)));
    battle.Read(() => Assert.Equal(Some(battle.PlayerUnit), battle.Events.SingleEvent<ObjectDamagedBattleEvent>().MaybeCause));
    battle.Events.EventBefore<UnitAttackedBattleEvent, ObjectDamagedBattleEvent>();
    battle.ClearEvents();
    battle.Attack(battle.PlayerUnit, obj);
    battle.Read(() => Assert.Equal(beforeAp - 1, battle.PlayerUnit.CurrentActionPoints));
    battle.Read(() => Assert.Equal(0, battle.Events.Count));
  }

  [TestCase]
  public void DestructionIsTerminalAndClearsOccupancyBeforeBroadcast()
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
      battle.Read(() => Assert.True(ReferenceEquals(obj, destroyed.Object)));
      battle.Read(() => Assert.Equal(Some(ObjectStatus.Destroyed), obj.Status));
      battle.Read(() => Assert.True(battle.Board.FindObjectPosition(obj.Id).IsNone));
    };
    battle.ClearEvents();
    battle.Attack(battle.PlayerUnit, obj);
    battle.Read(() => Assert.True(observed));
    battle.Read(() => Assert.Equal(0, obj.FindCapability<ObjectHealthCapability>().RequireSome().CurrentHealth));
    battle.Read(() => Assert.True(battle.Runtime.TryGetAttackTarget(new BattleEntity.Object(obj)).IsNone));
    battle.Read(() => Assert.True(battle.Runtime.TryGetAliveObject(obj).IsNone));
    battle.Read(() => Assert.Equal(0, battle.Events.EventsOf<ObjectDamagedBattleEvent>().Length));
    battle.Read(() => Assert.Equal(Some(battle.PlayerUnit), battle.Events.SingleEvent<ObjectDestroyedBattleEvent>().MaybeCause));
    battle.Events.EventBefore<UnitAttackedBattleEvent, ObjectDestroyedBattleEvent>();
    battle.Read(() => Assert.True(battle.Query(new GetBattleSpecialObjectsQuery()).AsValueEnumerable().Contains(obj)));
  }

  [TestCase]
  public void StunAndStatusPayloadsAreIgnoredForObjects()
  {
    using var battle = BattleFixture.Duel(
      player: new("Stunner", Weapon: TestData.MakeStunWeapon()),
      enemy: new("Burner", Weapon: TestData.MakeStatusWeapon(TestData.MakeBurn(applyChancePercent: 50))),
      configure: ObjectsAt(new ObjectPlacement(TestData.MakeObject("Crate", 20), new Vector3I(4, 0, 2))));
    var obj = ObjectAt(battle, new Vector3I(4, 0, 2));
    battle.ClearEvents();
    battle.Attack(battle.PlayerUnit, obj);
    battle.Read(() => Assert.Equal(20, obj.FindCapability<ObjectHealthCapability>().RequireSome().CurrentHealth));
    battle.Read(() => Assert.Equal(1, battle.Events.Count));
    battle.Read(() => Assert.True(battle.Events.SingleEvent<UnitAttackedBattleEvent>().IsHit));
    // Primitives/preview allow the other side; existing availability belongs to the caller.
    battle.Attack(battle.EnemyUnit, obj);
    battle.Read(() => Assert.Equal(17, obj.FindCapability<ObjectHealthCapability>().RequireSome().CurrentHealth));
    battle.Read(() => Assert.Equal(0, battle.Events.EventsOf<UnitStatusEffectAppliedBattleEvent>().Length));
  }

  private sealed class TwoObjectShots(BattleUnitState shooter, BattleObjectState target)
    : BattleHook<UnitMovedBattleEvent>
  {
    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, UnitMovedBattleEvent evt)
    {
      var actor = context.Session.TryGetAlive(shooter).RequireSome();
      var proof = context.Session.TryGetAttackTarget(new BattleEntity.Object(target)).RequireSome();
      return [BattleAction.AttackEntity(actor, proof), BattleAction.AttackEntity(actor, proof)];
    }
  }

  [TestCase]
  public void EarlierReactionDestroysTargetAndLaterReactionSpendsNothing()
  {
    var weapon = TestData.MakeAmmoWeapon("Rifle", magazine: 2, damage: 5);
    using var battle = BattleFixture.Duel(
      enemy: new("Guard", Weapon: weapon),
      configure: ObjectsAt(new ObjectPlacement(TestData.MakeObject("Crate", 5), new Vector3I(3, 0, 3))));
    var obj = ObjectAt(battle, new Vector3I(3, 0, 3));
    battle.RegisterHook<UnitMovedBattleEvent>(new TwoObjectShots(battle.EnemyUnit, obj));
    battle.ClearEvents();
    int beforeAp = battle.Read(() => battle.EnemyUnit.CurrentActionPoints);
    battle.Move(battle.PlayerUnit, [new Vector3I(4, 0, 2)]);
    battle.Read(() => Assert.Equal(1, battle.Events.EventsOf<UnitAttackedBattleEvent>().Length));
    battle.Read(() => Assert.Equal(1, battle.Events.EventsOf<ObjectDestroyedBattleEvent>().Length));
    battle.Read(() => Assert.Equal(1, weapon.CurrentAmmo));
    battle.Read(() => Assert.Equal(beforeAp - 1, battle.EnemyUnit.CurrentActionPoints));
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
  public void ShotHookCanResolveTheTargetBeforeWeaponDamageResumes()
  {
    var weapon = TestData.MakeAmmoWeapon("Rifle", magazine: 2);
    using var battle = BattleFixture.Duel(
      player: new("Shooter", Weapon: weapon),
      configure: ObjectsAt(new ObjectPlacement(TestData.MakeObject("Crate", 20), new Vector3I(4, 0, 2))));
    var obj = ObjectAt(battle, new Vector3I(4, 0, 2));
    battle.RegisterHook<UnitAttackedBattleEvent>(new DestroyDuringShot(obj));
    battle.ClearEvents();
    battle.Attack(battle.PlayerUnit, obj);
    battle.Read(() => Assert.Equal(1, weapon.CurrentAmmo));
    battle.Read(() => Assert.Equal(1, battle.Events.EventsOf<ObjectDestroyedBattleEvent>().Length));
    battle.Read(() => Assert.Equal(0, battle.Events.EventsOf<ObjectDamagedBattleEvent>().Length));
  }

  [TestCase]
  public void ExploredObjectsStillRequireTheAttackerSightAndRange()
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
    battle.Read(() => Assert.True(battle.Query(new GetFactionVisibleObjectsQuery(battle.PlayerFaction)).AsValueEnumerable().Contains(obj)));
    foreach (var actor in new[] { blind, shortRange, unarmed })
    {
      int ap = battle.Read(() => actor.CurrentActionPoints);
      battle.ClearEvents();
      battle.Read(() => Assert.True(battle.Query(new GetHitChanceForAttack(battle.Alive(actor), battle.Target(obj))).IsLeft));
      battle.Attack(actor, obj);
      battle.Read(() => Assert.Equal(ap, actor.CurrentActionPoints));
      battle.Read(() => Assert.Equal(0, battle.Events.Count));
    }
    battle.Read(() => Assert.Equal(6, blindWeapon.CurrentAmmo));
    battle.Read(() => Assert.Equal(6, shortWeapon.CurrentAmmo));
  }

  [TestCase]
  public void ObjectRangeIncludesTheBoundaryAndExcludesBeyondIt()
  {
    using var battle = BattleFixture.Duel(
      player: new("Shooter", Weapon: TestData.MakeWeapon("Rifle", range: 2)),
      configure: ObjectsAt(
        new ObjectPlacement(TestData.MakeObject("Boundary", 20), new Vector3I(2, 0, 1)),
        new ObjectPlacement(TestData.MakeObject("Beyond", 20), new Vector3I(1, 0, 1))));
    var boundary = ObjectAt(battle, new Vector3I(2, 0, 1));
    var beyond = ObjectAt(battle, new Vector3I(1, 0, 1));
    battle.Read(() => Assert.True(battle.Query(new GetHitChanceForAttack(battle.Alive(battle.PlayerUnit), battle.Target(boundary))).IsRight));
    battle.Read(() => Assert.True(battle.Query(new GetHitChanceForAttack(battle.Alive(battle.PlayerUnit), battle.Target(beyond))).IsLeft));
  }

  [TestCase]
  public void IgnoredObjectStatusesDoNotConsumeExtraRandomRolls()
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
  public void ObjectDamageRejectsInvalidTargetsAndSeparatesHealthFromStun()
  {
    using var battle = BattleFixture.Duel(configure: ObjectsAt(
      new ObjectPlacement(TestData.MakeObject("Crate", 10), new Vector3I(3, 0, 2)),
      new ObjectPlacement(TestData.MakeObject(), new Vector3I(2, 0, 2))));
    using var foreign = BattleFixture.Duel();
    var obj = ObjectAt(battle, new Vector3I(3, 0, 2));
    var scenery = ObjectAt(battle, new Vector3I(2, 0, 2));
    Assert.Throws<InvalidOperationException>(() => battle.Mutate(() => battle.Session.ApplyDamageTo(scenery, [], None)));
    Assert.Throws<InvalidOperationException>(() => foreign.Mutate(() => foreign.Session.ApplyDamageTo(obj, [], None)));
    Assert.Throws<ArgumentNullException>(() => battle.Mutate(() => battle.Session.ApplyDamageTo(obj, null!, None)));
    battle.ClearEvents();
    battle.Mutate(() => battle.Session.ApplyDamageTo(obj,
      [new Damage(7, Element.Kinetic, Kind: DamageKind.Stun), new Damage(3, Element.Thermal)], None));
    battle.Read(() => Assert.Equal(7, obj.FindCapability<ObjectHealthCapability>().RequireSome().CurrentHealth));
    battle.Read(() => Assert.Equal(3, battle.Events.SingleEvent<ObjectDamagedBattleEvent>().HealthDamage));
    battle.Mutate(() => battle.Session.ApplyDamageTo(obj, [new Damage(999, Element.Kinetic)], None));
    Assert.Throws<InvalidOperationException>(() =>
      battle.Mutate(() => battle.Session.ApplyDamageTo(obj, [new Damage(1, Element.Kinetic)], None)));
    battle.Read(() => Assert.Equal(1, battle.Events.EventsOf<ObjectDestroyedBattleEvent>().Length));
  }
}
