using System.Collections.Generic;
using FunProject.Battle;
using FunProject.Combatants;
using FunProject.Core;
using FunProject.Weapons;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public sealed class BattleObservationHistoryTest
{
  private static BattleFixture Contacts(int targetX = 2, params BattleTypeSystemData[] systems)
  {
    var player = TestData.MakeFaction("Player"); var enemy = TestData.MakeFaction("Enemy");
    return new BattleFixture(new BattleSetup(TestData.MakeOpenBattleMap(new Vector3I(8, 1, 1)),
      [new BattleSideSetup(player, [new FakeObjectiveData()],
        [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Observer", player, vision: 2)), new Vector3I(0, 0, 0))]),
       new BattleSideSetup(enemy, [new FakeObjectiveData()],
        [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Target", enemy, vision: 1), TestData.MakeAmmoWeapon("Rifle")), new Vector3I(targetX, 0, 0))])], Seed: 7)
    { Systems = systems });
  }

  [TestCase]
  public void BootstrapRecordsHostileContactBeforeBroadcast()
  {
    bool recorded = false;
    using var battle = Contacts(2, new SetupSystemData
    {
      OnRegister = runtime =>
      runtime.RegisterHook<UnitSpottedBattleEvent>(new VerifyBootstrapHistory(() => recorded = true))
    });
    Assert.True(recorded);
    Assert.Equal(1, battle.Query(new GetFactionLastSeenContacts(battle.PlayerFaction)).Count);
    Assert.True(battle.Query(new GetHasSeenHostile(battle.Alive(battle.PlayerUnit))));
  }

  private sealed class VerifyBootstrapHistory(System.Action found) : BattleHook<UnitSpottedBattleEvent>
  {
    protected override IReadOnlyList<BattleAction> OnEvent(HookContext context, UnitSpottedBattleEvent evt)
    {
      if (evt.Unit.Side == evt.Target.Side) return [];
      var contact = new GetFactionLastSeenContacts(evt.Unit.Side).Execute(context.Session).AsValueEnumerable()
        .Single(value => value.UnitId == evt.Target.Id);
      Assert.Equal(context.Session.GetUnitPosition(evt.Target).RequireSome().Raw, contact.Position);
      found(); return [];
    }
  }

  [TestCase]
  public void RepeatedSightUpdatesRevisionAndPosition()
  {
    using var battle = Contacts();
    var first = battle.Query(new GetFactionLastSeenContacts(battle.PlayerFaction))[0];
    battle.EndFactionTurn(battle.PlayerFaction);
    battle.Move(battle.EnemyUnit, [new Vector3I(3, 0, 0)]);
    battle.Move(battle.EnemyUnit, [new Vector3I(2, 0, 0)]);
    var repeated = battle.Query(new GetFactionLastSeenContacts(battle.PlayerFaction))[0];
    Assert.True(repeated.VisibilityRevision > first.VisibilityRevision);
    Assert.Equal(new Vector3I(2, 0, 0), repeated.Position);
    Assert.Equal(1, battle.Events.EventsOf<UnitSpottedBattleEvent>().AsValueEnumerable()
      .Count(evt => evt.Unit == battle.PlayerUnit && evt.Target == battle.EnemyUnit));
    battle.Move(battle.EnemyUnit, [new Vector3I(3, 0, 0)]);
    Assert.Equal(repeated, battle.Query(new GetFactionUnitKnowledge(battle.PlayerFaction)).Remembered[0]);
  }

  [TestCase]
  public void BriefMidActionContactSurvivesLoss()
  {
    using var battle = Contacts(4);
    Assert.False(battle.Query(new GetHasSeenHostile(battle.Alive(battle.PlayerUnit))));
    battle.EndFactionTurn(battle.PlayerFaction);
    battle.Move(battle.EnemyUnit, [new Vector3I(3, 0, 0), new Vector3I(2, 0, 0), new Vector3I(3, 0, 0)]);
    var knowledge = battle.Query(new GetFactionUnitKnowledge(battle.PlayerFaction));
    Assert.Equal(1, knowledge.Current.Count);
    Assert.Equal(new Vector3I(2, 0, 0), knowledge.Remembered[0].Position);
    Assert.True(battle.Query(new GetHasSeenHostile(battle.Alive(battle.PlayerUnit))));
  }

  [TestCase]
  public void SeenHostileIsMonotonicAndIndependentOfActivation()
  {
    using var battle = Contacts();
    battle.EndFactionTurn(battle.PlayerFaction);
    battle.Move(battle.EnemyUnit, [new Vector3I(3, 0, 0)]);
    battle.EndFactionTurn(battle.EnemyFaction);
    Assert.True(battle.Query(new GetHasSeenHostile(battle.Alive(battle.PlayerUnit))));
    var faction = TestData.MakeFaction("Ally");
    using var allies = BattleFixture.Started(new Vector3I(4, 1, 1),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("A", faction)), new Vector3I(0, 0, 0)),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("B", faction)), new Vector3I(1, 0, 0)));
    Assert.False(allies.Query(new GetHasSeenHostile(allies.Alive(allies.PlayerUnit))));
  }

  [TestCase]
  public void HiddenDeathAndEquipmentCannotChangeKnowledge()
  {
    using var alive = Contacts(); using var hiddenDeath = Contacts();
    foreach (var battle in new[] { alive, hiddenDeath })
    {
      battle.EndFactionTurn(battle.PlayerFaction);
      battle.Move(battle.EnemyUnit, [new Vector3I(3, 0, 0)]);
    }
    hiddenDeath.ApplyDamage(hiddenDeath.EnemyUnit, 999);
    var hiddenWeapon = (AmmunitionedWeapon)hiddenDeath.EnemyUnit.EquippedWeapon.RequireSome();
    Assert.True(hiddenWeapon.TrySpendShot().IsSome);
    Assert.True(hiddenWeapon.CanReload());
    var a = alive.Query(new GetFactionUnitKnowledge(alive.PlayerFaction));
    var b = hiddenDeath.Query(new GetFactionUnitKnowledge(hiddenDeath.PlayerFaction));
    Assert.True(a.Current.AsValueEnumerable().SequenceEqual(b.Current));
    Assert.True(a.Remembered.AsValueEnumerable().SequenceEqual(b.Remembered));
    alive.ApplyDamage(alive.PlayerUnit, 999);
    Assert.Equal(1, alive.Query(new GetFactionLastSeenContacts(alive.PlayerFaction)).Count);
  }

  [TestCase]
  public void ExploredObjectDoesNotRevealUnseenStatus()
  {
    var faction = TestData.MakeFaction("Player");
    using var battle = new BattleFixture(new BattleSetup(TestData.MakeOpenBattleMap(new Vector3I(8, 1, 1)),
      [new BattleSideSetup(faction, [new FakeObjectiveData()],
        [new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Observer", faction, vision: 1)), new Vector3I(2, 0, 0))])], Seed: 7)
    { Objects = [new ObjectPlacement(TestData.MakeObject(health: 5), new Vector3I(3, 0, 0))] });
    var obj = battle.Query(new GetBattleSpecialObjectsQuery())[0];
    Assert.Equal(1, battle.Query(new GetFactionVisibleObjectsQuery(faction)).Count);
    battle.Move(battle.PlayerUnit, [new Vector3I(1, 0, 0)]);
    Assert.True(battle.Query(new HasFactionExploredTile(faction, battle.At(3, 0, 0))));
    battle.Session.ApplyDamageTo(obj, [new Damage(10, Element.Kinetic)], None);
    Assert.Equal(0, battle.Query(new GetFactionVisibleObjectsQuery(faction)).Count);
  }
}
