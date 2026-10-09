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
  private static readonly int[] TargetIds = [1, 2, 3, 4, 5, 6, 7, 8, 9];
  [TestCase]
  public static void DefaultScoreAndQuantizationFollowApprovedFormula()
  {
    var features = EnemyScoreFeatures.FromNormalized(.25, 0, .10, 0, 0, 0, 0);
    Assert.Equal(950L, new EnemyPolicy().Score(features));
    Assert.Equal(14533L, new EnemyPolicy(new(200, 25, 150)).Score(
      EnemyScoreFeatures.FromNormalized(.25, .50, .10, -.20, .30, .40, 1)));
    Assert.Equal(25, EnemyScoreFeatures.FromNormalized(.245, 0, 0, 0, -.245, 0, 0).Damage);
    Assert.Equal(-25, EnemyScoreFeatures.FromNormalized(0, 0, 0, 0, -.245, 0, 0).Progress);
    Assert.Throws<ArgumentOutOfRangeException>(() => _ = new EnemyPolicySettings(aggressionPercent: 201));
    Assert.Throws<ArgumentOutOfRangeException>(() => _ = new EnemyPolicySettings(preferredRangePercent: -1));
  }

  [TestCase]
  public static void DemoWeightsPermitUsefulLowDamageAttacksAndTiesUseTargetId()
  {
    var unit = new EnemyOwnUnit(7, Vector3I.Zero, 20, 0, 4, true, true, false, 0, 2, 0, 0);
    var frame = new EnemyDecisionFrame(1, false, [unit],
      [new(9, new(1, 0, 0), 20, 0), new(3, new(-1, 0, 0), 20, 0)],
      [], [], [new(7, 9, 100), new(7, 3, 100)]);
    Assert.Equal(EnemyIntentKind.Pass, new EnemyPolicy().Decide(frame, new EnemyTurnMemory()).Intent.Kind);
    var policy = new EnemyPolicy(new EnemyPolicySettings(200, 25));
    var decision = policy.Decide(frame, new EnemyTurnMemory());
    Assert.Equal(EnemyIntentKind.Attack, decision.Intent.Kind);
    Assert.Equal(3, decision.Intent.TargetId);
    Assert.True(decision.ScoreCentipoints > 0);
  }

  [TestCase]
  public static void LowestActableActorAndEightNearestTargetsBoundDecisions()
  {
    var actor = new EnemyOwnUnit(2, Vector3I.Zero, 20, 0, 4, true, true, false, 0, 1, 0, 0);
    var targets = System.Array.AsReadOnly(TargetIds
      .AsValueEnumerable().Select(id => new EnemyHostile(id, new(id, 0, 0), id == 9 ? 1 : 1000, 0)).ToArray());
    var previews = targets.AsValueEnumerable().Select(target => new EnemyAttackPreview(2, target.UnitId, 100)).ToArray();
    var frame = new EnemyDecisionFrame(1, false, [actor], targets, [], [], previews);
    var decision = new EnemyPolicy(new EnemyPolicySettings(cautionPercent: 0)).Decide(frame, new EnemyTurnMemory());
    Assert.Equal(EnemyIntentKind.Pass, decision.Intent.Kind);
    Assert.Equal(8, decision.Candidates.Count);
    var second = actor with { UnitId = 1, CanAct = true, CanAttack = false };
    Assert.Equal(1, new EnemyPolicy().Decide(frame with { OwnUnits = [actor, second] }, new EnemyTurnMemory()).Intent.ActorId);

    using var battle = BattleFixture.Duel(configure: setup => setup with
    {
      Sides = [setup.Sides[0] with
      {
        Units = [.. setup.Sides[0].Units, new(new(TestData.MakeCombatant("Armed ally", setup.Sides[0].Faction),
          TestData.MakeWeapon("Pistol", damage: 20)), Vector3I.Zero)],
      }, setup.Sides[1]],
    });
    var observed = battle.Query(new GetEnemyDecisionFrame(battle.PlayerFaction));
    Assert.Equal(2, observed.OwnUnits.Count);
    Assert.Equal(0, observed.AttackPreviews.Count); // The lowest actable actor is unarmed.
    Assert.Equal(battle.PlayerUnit.Id, new EnemyPolicy().Decide(observed, new EnemyTurnMemory()).Intent.ActorId);
    battle.Pass(battle.PlayerUnit);
    observed = battle.Query(new GetEnemyDecisionFrame(battle.PlayerFaction));
    int nextActor = observed.OwnUnits.AsValueEnumerable().Single(unit => unit.CanAct).UnitId;
    Assert.Equal(1, observed.AttackPreviews.Count);
    Assert.Equal(nextActor, observed.AttackPreviews[0].ActorId);
    Assert.Equal(EnemyIntentKind.Attack, new EnemyPolicy().Decide(observed, new EnemyTurnMemory()).Intent.Kind);
  }

  [TestCase]
  public static void HiddenWorldChangesCannotAlterFrameDecisionOrTrace()
  {
    var own = TestData.MakeFaction("Observer");
    var hostile = TestData.MakeFaction("Hostile");
    BattleSetup Setup() => new(TestData.MakeOpenBattleMap(new Vector3I(8, 1, 3)),
      [new(own, [new FakeObjectiveData()], [new(new(TestData.MakeCombatant("Observer", own, vision: 1)), new(5, 0, 0))]),
       new(hostile, [new FakeObjectiveData()], [new(new(TestData.MakeCombatant("Hidden", hostile)), new(6, 0, 0))])], Seed: 7);
    using var open = new BattleFixture(Setup());
    using var hidden = new BattleFixture(Setup());
    Assert.Equal(1, open.Query(new GetEnemyDecisionFrame(own)).Hostiles.Count);
    open.Move(open.PlayerUnit, [new(4, 0, 0)]);
    hidden.Move(hidden.PlayerUnit, [new(4, 0, 0)]);
    var retained = open.Query(new GetEnemyDecisionFrame(own));
    Assert.Equal(1, retained.Contacts.Count);
    Assert.Equal(0, retained.Hostiles.Count);
    hidden.Board.SetTileWalkable(hidden.At(7, 0, 0), false);
    hidden.Move(hidden.EnemyUnit, [new(6, 0, 1)]);
    hidden.Spawn(TestData.MakeCombatant("Hidden equipped", hostile), new(7, 0, 2), TestData.MakeWeapon("Secret", damage: 999));
    hidden.ApplyDamage(hidden.EnemyUnit, 999);
    var other = hidden.Query(new GetEnemyDecisionFrame(own));
    Assert.True(retained.OwnUnits.AsValueEnumerable().SequenceEqual(other.OwnUnits));
    Assert.True(retained.Hostiles.AsValueEnumerable().SequenceEqual(other.Hostiles));
    Assert.True(retained.Tiles.AsValueEnumerable().SequenceEqual(other.Tiles));
    Assert.True(retained.Contacts.AsValueEnumerable().SequenceEqual(other.Contacts));
    Assert.True(retained.AttackPreviews.AsValueEnumerable().SequenceEqual(other.AttackPreviews));
    Assert.True(retained.OwnUnits.Count > 0);
    var a = new EnemyPolicy().Decide(retained, new EnemyTurnMemory());
    var b = new EnemyPolicy().Decide(other, new EnemyTurnMemory());
    Assert.Equal(a.Intent, b.Intent);
    Assert.Equal(a.FallbackReason, b.FallbackReason);
    Assert.True(a.Candidates.AsValueEnumerable().SequenceEqual(b.Candidates));
    open.Move(open.PlayerUnit, [new(3, 0, 0)]);
    Assert.Equal(new Vector3I(4, 0, 0), retained.OwnUnits[0].Position);
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
      var frame = queried.Query(new GetEnemyDecisionFrame(queried.PlayerFaction));
      Assert.Equal(1, frame.AttackPreviews.Count);
      Assert.Equal(65, frame.AttackPreviews[0].HitChancePercent);
    }
    Assert.Equal(ammo, weapon.CurrentAmmo);
    queried.ClearEvents(); control.ClearEvents();
    queried.Attack(queried.PlayerUnit, queried.EnemyUnit);
    control.Attack(control.PlayerUnit, control.EnemyUnit);
    Assert.Equal(control.Events.SingleEvent<UnitAttackedBattleEvent>().Roll,
      queried.Events.SingleEvent<UnitAttackedBattleEvent>().Roll);
  }

  [TestCase]
  public static void ReloadRequiresEmptyMagazineAndObservedClearMovesNeverProbeUnknown()
  {
    var actor = new EnemyOwnUnit(1, Vector3I.Zero, 20, 0, 4, true, false, true, 0, 0, 0, 0);
    var frame = new EnemyDecisionFrame(1, false, [actor], [], [],
      [new(Vector3I.Zero, TraversalKnowledge.Blocked, true, true, TileCover.None),
       new(new(1, 0, 0), TraversalKnowledge.Unknown, false, false, TileCover.None)], []);
    Assert.Equal(EnemyIntentKind.Reload, new EnemyPolicy().Decide(frame, new EnemyTurnMemory()).Intent.Kind);
    var loaded = actor with { CanReload = false, Ammo = 1 };
    Assert.Equal(EnemyIntentKind.Pass, new EnemyPolicy().Decide(frame with { OwnUnits = [loaded] }, new EnemyTurnMemory()).Intent.Kind);
  }

  [TestCase]
  public static void MemoryAvoidsRevisitsAndDismissesObservedEmptyContacts()
  {
    var actor = new EnemyOwnUnit(1, Vector3I.Zero, 20, 0, 4, true, false, false, 0, 0, 0, 0);
    var frame = new EnemyDecisionFrame(1, false, [actor], [], [new(9, new(2, 0, 0), 5)],
      [new(Vector3I.Zero, TraversalKnowledge.Blocked, true, true, TileCover.None),
       new(new(1, 0, 0), TraversalKnowledge.Clear, true, true, TileCover.None),
       new(new(2, 0, 0), TraversalKnowledge.Unknown, true, false, TileCover.None)], []);
    var memory = new EnemyTurnMemory();
    var policy = new EnemyPolicy();
    Assert.Equal(new Vector3I(1, 0, 0), policy.Decide(frame, memory).Intent.Destination);
    memory.Visit(1, new(1, 0, 0));
    Assert.Equal(EnemyIntentKind.Pass, policy.Decide(frame, memory).Intent.Kind);
    var observedEmpty = frame with { Tiles = [new(new(2, 0, 0), TraversalKnowledge.Clear, true, true, TileCover.None)] };
    memory.Observe(observedEmpty);
    Assert.True(memory.IsDismissed(frame.Contacts[0]));
    var newSighting = frame with
    {
      Contacts = [new(9, new(0, 0, 2), 6)],
      Tiles = [new(Vector3I.Zero, TraversalKnowledge.Blocked, true, true, TileCover.None),
        new(new(0, 0, 1), TraversalKnowledge.Clear, true, true, TileCover.None),
        new(new(0, 0, 2), TraversalKnowledge.Unknown, true, false, TileCover.None)],
    };
    memory.Observe(newSighting);
    Assert.Equal(new Vector3I(0, 0, 1), policy.Decide(newSighting, memory).Intent.Destination);
  }
}
