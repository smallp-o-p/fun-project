using System;
using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Weapons;
using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public sealed class EnemyPolicyTest
{
  private static readonly int[] AdditionalTargetPositions = [2, 3, 4, 5, 6, 7, 8, 9];
  [TestCase]
  public static void SettingsPreserveScoringPrecisionAndClampPercentages()
  {
    Assert.Equal(950L, new EnemyPolicySettings().Score(damage: .25, exposure: .10));
    Assert.Equal(14533L, new EnemyPolicySettings(200, 25, 150).Score(
      damage: .25, kill: .50, exposure: .10, rangeImprovement: -.20, progress: .30, frontier: .40, reload: true));
    Assert.Equal(1300L, new EnemyPolicySettings().Score(damage: .245));
    Assert.Equal(-700L, new EnemyPolicySettings().Score(progress: -.245));
    var clamped = new EnemyPolicySettings(201, -1, 300, 101);
    Assert.Equal(200f, clamped.AggressionPercent);
    Assert.Equal(0f, clamped.CautionPercent);
    Assert.Equal(200f, clamped.RangeWeightPercent);
    Assert.Equal(100f, clamped.PreferredRangePercent);
    var fractional = new EnemyPolicySettings(100.25f, 25.5f, 150.75f, 75.5f);
    Assert.Equal(100.25f, fractional.AggressionPercent);
    Assert.Equal(25.5f, fractional.CautionPercent);
    Assert.Equal(150.75f, fractional.RangeWeightPercent);
    Assert.Equal(75.5f, fractional.PreferredRangePercent);
    Assert.Equal(954L, new EnemyPolicySettings(100.25f).Score(damage: .25, exposure: .10));
    Assert.Equal(15801L, new EnemyPolicySettings(100.004f).Score(damage: 1, kill: 1));
    Assert.Equal(-605L, new EnemyPolicySettings(cautionPercent: 115.75f).Score(exposure: .10));
  }

  [TestCase]
  public static void DemoWeightsPermitUsefulLowDamageAttacksAndTiesUseTargetId()
  {
    using var battle = BattleFixture.Duel(dimensions: new(4, 1, 1),
      player: new("Actor", Position: new(2, 0, 0), Weapon: TestData.MakeWeapon("Pistol", damage: 2)),
      enemy: new("First target", Position: Vector3I.Zero), hitChanceCalculator: new AlwaysHitCalculator(),
      configure: setup => setup with
      {
        Sides = [setup.Sides[0], setup.Sides[1] with
        {
          Units = [.. setup.Sides[1].Units,
            new(new(TestData.MakeCombatant("Second target", setup.Sides[1].Faction)), new(3, 0, 0))],
        }],
      });
    Assert.Equal(EnemyIntentKind.Pass, new EnemyPolicy().DecideAction(battle.Runtime, battle.PlayerFaction, new(), battle.Alive(battle.PlayerUnit)).Intent.Kind);
    var decision = new EnemyPolicy(new(200, 25)).DecideAction(battle.Runtime, battle.PlayerFaction, new(), battle.Alive(battle.PlayerUnit)).Intent;
    Assert.Equal(EnemyIntentKind.Attack, decision.Kind);
    Assert.Equal(battle.EnemyUnit.Id, decision.TargetId);
  }

  [TestCase]
  public static void AllActorVisibleTargetsCanBeChosen()
  {
    const int vulnerableTarget = 9;
    using var crowded = BattleFixture.Duel(dimensions: new(10, 1, 1),
      player: new("Actor", Position: Vector3I.Zero, Weapon: TestData.MakeWeapon("Pistol", damage: 1)),
      enemy: new("Nearest", Position: new(1, 0, 0), Health: 1000), hitChanceCalculator: new AlwaysHitCalculator(),
      configure: setup => setup with
      {
        Sides = [setup.Sides[0], setup.Sides[1] with
        {
          Units = [.. setup.Sides[1].Units, .. AdditionalTargetPositions.AsValueEnumerable().Select(x =>
            new UnitPlacement(new(TestData.MakeCombatant($"Target {x}", setup.Sides[1].Faction,
              health: x == vulnerableTarget ? 1 : 1000)), new(x, 0, 0)))],
        }],
      });
    var decision = new EnemyPolicy(new(cautionPercent: 0)).DecideAction(crowded.Runtime, crowded.PlayerFaction, new(), crowded.Alive(crowded.PlayerUnit)).Intent;
    Assert.Equal(EnemyIntentKind.Attack, decision.Kind);
    Assert.Equal(crowded.UnitAt(new(vulnerableTarget, 0, 0)).Id, decision.TargetId);
  }

  [TestCase]
  public static void HiddenWorldChangesCannotAlterDecision()
  {
    var own = TestData.MakeFaction("Observer");
    var hostile = TestData.MakeFaction("Hostile");
    BattleSetup Setup() => new(TestData.MakeOpenBattleMap(new Vector3I(8, 1, 3)),
      [new(own, [new FakeObjectiveData()], [new(new(TestData.MakeCombatant("Observer", own, vision: 1)), new(5, 0, 0))]),
       new(hostile, [new FakeObjectiveData()], [new(new(TestData.MakeCombatant("Hidden", hostile)), new(6, 0, 0))])], Seed: 7);
    using var open = new BattleFixture(Setup());
    using var hidden = new BattleFixture(Setup());
    Assert.Equal(1, open.Query(new GetVisibleHostileUnitsForFaction(own)).Count);
    open.Move(open.PlayerUnit, [new(4, 0, 0)]);
    hidden.Move(hidden.PlayerUnit, [new(4, 0, 0)]);
    Assert.Equal(1, open.Query(new GetFactionUnitKnowledge(own)).Remembered.Count);
    Assert.Equal(0, open.Query(new GetVisibleHostileUnitsForFaction(own)).Count);
    hidden.Board.SetTileWalkable(hidden.At(7, 0, 0), false);
    hidden.Move(hidden.EnemyUnit, [new(6, 0, 1)]);
    hidden.Spawn(TestData.MakeCombatant("Hidden equipped", hostile), new(4, 0, 2), TestData.MakeWeapon("Secret", damage: 999));
    hidden.ApplyDamage(hidden.EnemyUnit, 999);
    var policy = new EnemyPolicy(new(cautionPercent: 0));
    var a = policy.DecideAction(open.Runtime, own, new(), open.Alive(open.PlayerUnit)).Intent;
    var b = policy.DecideAction(hidden.Runtime, own, new(), hidden.Alive(hidden.PlayerUnit)).Intent;
    Assert.Equal(EnemyIntentKind.Move, a.Kind);
    Assert.Equal(new Vector3I(5, 0, 0), a.Destination);
    Assert.Equal(a, b);
  }

  [TestCase(0)]
  [TestCase(200)]
  public static void LoadedWeaponMovementRespectsPreferredRange(int rangeWeight)
  {
    using var battle = BattleFixture.Duel(playerControlled: true, dimensions: new(3, 1, 1),
      player: new("Actor", Position: new(2, 0, 0), Weapon: TestData.MakeWeapon("Weak pistol", damage: 1, range: 2)),
      enemy: new("Target", Position: Vector3I.Zero, Health: 1000), hitChanceCalculator: new AlwaysHitCalculator());
    var policy = new EnemyPolicy(new(cautionPercent: 0, rangeWeightPercent: rangeWeight, preferredRangePercent: 100));
    var decision = policy.DecideAction(battle.Runtime, battle.PlayerFaction, new(), battle.Alive(battle.PlayerUnit)).Intent;
    Assert.Equal(rangeWeight == 0 ? EnemyIntentKind.Move : EnemyIntentKind.Pass, decision.Kind);
    if (rangeWeight == 0) Assert.Equal(new Vector3I(1, 0, 0), decision.Destination);
  }

  [TestCase]
  public static void QueryPreviewSpendsNeitherAmmoNorRandomRolls()
  {
    using var queried = BattleFixture.Duel(player: new("Actor", Weapon: TestData.MakeAmmoWeapon("Pistol")), randomSeed: 19);
    using var control = BattleFixture.Duel(player: new("Actor", Weapon: TestData.MakeAmmoWeapon("Pistol")), randomSeed: 19);
    var weapon = (AmmunitionedWeapon)queried.PlayerUnit.EquippedWeapon.RequireSome();
    int ammo = weapon.CurrentAmmo;
    for (int i = 0; i < 5; i++)
    {
      var decision = new EnemyPolicy(new(cautionPercent: 0)).DecideAction(queried.Runtime, queried.PlayerFaction, new(), queried.Alive(queried.PlayerUnit)).Intent;
      Assert.Equal(EnemyIntentKind.Attack, decision.Kind);
    }
    Assert.Equal(ammo, weapon.CurrentAmmo);
    queried.ClearEvents(); control.ClearEvents();
    queried.Attack(queried.PlayerUnit, queried.EnemyUnit);
    control.Attack(control.PlayerUnit, control.EnemyUnit);
    Assert.Equal(control.Events.SingleEvent<UnitAttackedBattleEvent>().Roll,
      queried.Events.SingleEvent<UnitAttackedBattleEvent>().Roll);
  }

  [TestCase(false)]
  [TestCase(true)]
  public static void ReloadRequiresEmptyMagazineAndObservedClearMovesNeverProbeUnknown(bool visibleTarget)
  {
    var weapon = TestData.MakeAmmoWeapon("Pistol", magazine: 1);
    using var battle = BattleFixture.Duel(playerControlled: true, dimensions: new(4, 1, 1),
      player: new("Actor", Position: Vector3I.Zero, Weapon: weapon),
      enemy: new("Target", Position: new(3, 0, 0), Health: 1),
      configure: setup => setup with
      {
        Sides = [setup.Sides[0] with
        {
          Units = [new(new(TestData.MakeCombatant("Actor", setup.Sides[0].Faction, vision: visibleTarget ? 4 : 0), weapon), Vector3I.Zero)],
        }, setup.Sides[1]],
      });
    weapon.TrySpendShot().RequireSome();
    var policy = new EnemyPolicy();
    var empty = policy.DecideAction(battle.Runtime, battle.PlayerFaction, new(), battle.Alive(battle.PlayerUnit)).Intent;
    Assert.Equal(EnemyIntentKind.Reload, empty.Kind);
    battle.Reload(battle.PlayerUnit, weapon);
    var loaded = policy.DecideAction(battle.Runtime, battle.PlayerFaction, new(), battle.Alive(battle.PlayerUnit)).Intent;
    Assert.Equal(visibleTarget ? EnemyIntentKind.Attack : EnemyIntentKind.Pass, loaded.Kind);
  }

  [TestCase]
  public static void MemoryAvoidsRevisitsAndDismissesObservedEmptyContacts()
  {
    using var battle = BattleFixture.Duel(dimensions: new(4, 1, 1),
      player: new("Observer", Position: new(1, 0, 0)), enemy: new("Contact", Position: new(2, 0, 0)),
      configure: setup => setup with
      {
        Sides = [setup.Sides[0] with
        {
          Units = [new(new(TestData.MakeCombatant("Observer", setup.Sides[0].Faction, vision: 1, actionPoints: 20)), new(1, 0, 0))],
        }, setup.Sides[1]],
      });
    battle.Move(battle.PlayerUnit, [Vector3I.Zero]);
    var contact = battle.Query(new GetFactionLastSeenContacts(battle.PlayerFaction)).AsValueEnumerable().Single();
    var memory = new EnemyTurnMemory();
    var policy = new EnemyPolicy();
    Assert.Equal(new Vector3I(1, 0, 0), policy.DecideAction(battle.Runtime, battle.PlayerFaction, memory, battle.Alive(battle.PlayerUnit)).Intent.Destination);
    memory.Visit(battle.PlayerUnit.Id, new(1, 0, 0));
    Assert.Equal(EnemyIntentKind.Pass, policy.DecideAction(battle.Runtime, battle.PlayerFaction, memory, battle.Alive(battle.PlayerUnit)).Intent.Kind);
    battle.Move(battle.EnemyUnit, [new(3, 0, 0)]);
    battle.Move(battle.PlayerUnit, [new(1, 0, 0)]);
    policy.DecideAction(battle.Runtime, battle.PlayerFaction, memory, battle.Alive(battle.PlayerUnit));
    Assert.True(memory.IsDismissed(contact));
    battle.Move(battle.PlayerUnit, [new(2, 0, 0)]);
    battle.Move(battle.PlayerUnit, [new(1, 0, 0)]);
    var newContact = battle.Query(new GetFactionLastSeenContacts(battle.PlayerFaction)).AsValueEnumerable().Single();
    Assert.True(newContact.VisibilityRevision > contact.VisibilityRevision);
    Assert.False(memory.IsDismissed(newContact));
    Assert.Equal(new Vector3I(2, 0, 0), policy.DecideAction(battle.Runtime, battle.PlayerFaction, memory, battle.Alive(battle.PlayerUnit)).Intent.Destination);
  }
}
