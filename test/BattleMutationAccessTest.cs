using System;
using FunProject.Battle;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Items.Effects;
using FunProject.Stats;
using FunProject.Weapons;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public sealed class BattleMutationAccessTest
{
  [TestCase(0)]
  [TestCase(1)]
  [TestCase(2)]
  [TestCase(3)]
  [TestCase(4)]
  [TestCase(5)]
  [TestCase(6)]
  [TestCase(7)]
  [TestCase(8)]
  [TestCase(9)]
  [TestCase(10)]
  [TestCase(11)]
  [TestCase(12)]
  [TestCase(13)]
  [TestCase(14)]
  [TestCase(15)]
  [TestCase(16)]
  [TestCase(17)]
  [TestCase(18)]
  [TestCase(19)]
  [TestCase(20)]
  [TestCase(21)]
  [TestCase(22)]
  [TestCase(23)]
  [TestCase(24)]
  [TestCase(25)]
  [TestCase(26)]
  [TestCase(27)]
  [TestCase(28)]
  [TestCase(29)]
  [TestCase(30)]
  [TestCase(31)]
  [TestCase(32)]
  [TestCase(33)]
  [TestCase(34)]
  [TestCase(35)]
  [TestCase(36)]
  [TestCase(37)]
  public void EveryBoundMutationRequiresExecution(int operation)
  {
    var weapon = new FirearmWeapon(TestData.MakeFirearmWeaponData(modSlots: 1));
    var armor = TestData.MakeArmor("Armor", 10);
    var usable = TestData.MakeUsableItem("Medkit");
    using var battle = BattleFixture.Duel(player: new("Alpha", Weapon: weapon, Armor: armor),
      configure: setup =>
      {
        setup.Sides[0].Units[0].Loadout.Combatant.EquipItem(usable.Item, 0);
        return setup with { Objects = [new ObjectPlacement(TestData.MakeObject(health: 5), new Vector3I(0, 0, 0))] };
      });
    var unit = battle.PlayerUnit;
    var actor = battle.Alive(unit);
    var tile = battle.At(1, 0, 0);
    var origin = battle.At(4, 0, 1);
    var obj = battle.Query(new GetBattleSpecialObjectsQuery())[0];
    var health = obj.FindCapability<ObjectHealthCapability>().RequireSome();
    var slot = battle.Read(() => weapon.GetModSlots()[0]);
    var spec = new ImmobilizeStatusSpecData { DurationTurns = 2 };
    battle.Mutate(() => battle.Session.ApplyStatusEffectTo(unit, spec));
    var status = battle.Read(() => unit.ActiveStatusEffects.AsValueEnumerable().Single());
    Action mutate = operation switch
    {
      0 => () => unit.RefreshForNewTurn(),
      1 => () => unit.TrySpendActionPoints(0),
      2 => () => unit.TrySpendActionPoints(int.MaxValue),
      3 => () => unit.ReceiveDamage(0),
      4 => () => unit.AddInventoryItem(usable.Item),
      5 => () => unit.RemoveInventoryItem(usable.Item),
      6 => () => battle.Board.SetTileWalkable(tile, true),
      7 => () => battle.Board.TryPlaceOccupant(tile, 50),
      8 => () => battle.Board.TryMoveOccupant(origin, tile, unit.Id),
      9 => () => battle.Board.TryClearOccupant(tile, 50),
      10 => () => battle.Board.TryPlaceObjectOccupant(tile, -50),
      11 => () => battle.Board.TryClearObjectOccupant(tile, -50),
      12 => () => weapon.TrySpendShot(),
      13 => () => weapon.Reload(),
      14 => () => weapon.Archetype = weapon.Archetype,
      15 => () => weapon.AmmoType = None,
      16 => () => armor.Capability.Reduce(0),
      17 => () => armor.Capability.RearmRegenDelay(),
      18 => () => armor.Capability.TickRegen(),
      19 => () => usable.Capability.TrySpend(0),
      20 => () => usable.Capability.Restore(),
      21 => () => obj.Status = None,
      22 => () => health.Reduce(0),
      23 => () => status.Refresh(),
      24 => () => status.TickDown(),
      25 => () => battle.Session.RollPercent(),
      26 => () => battle.Session.RaiseEvents(new SessionStartedBattleEvent()),
      27 => () => battle.Session.InvalidateVisibility(),
      28 => () => battle.Session.AddObjective(battle.PlayerFaction, new FakeObjectiveData().Instantiate()),
      29 => () => unit.Combatant.EquipWeapon(weapon),
      30 => () => unit.Combatant.UnequipWeapon(),
      31 => () => unit.Combatant.EquipArmor(armor),
      32 => () => unit.Combatant.EquipItem(usable.Item, 0),
      33 => () => unit.Combatant.Progression.AwardPoints(1),
      34 => () => unit.Combatant.Rank.Gain(1),
      35 => () => slot.EquippedMod = None,
      36 => () => slot.Unequip(),
      37 => () => unit.ClearVisibility(),
      _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };
    Assert.Throws<InvalidOperationException>(mutate, $"Mutation operation {operation} must require execution.");
    battle.Read(() => Assert.Throws<InvalidOperationException>(mutate));
  }

  [TestCase]
  public void QueryCannotWriteThroughEscapedSession()
  {
    using var battle = BattleFixture.Duel();
    Assert.Throws<InvalidOperationException>(() => battle.Query(new MutatingQuery()));
  }

  [TestCase]
  public void TileReadCannotRetainWritableAlias()
  {
    Assert.False(typeof(BattleBoardState).GetMethod(nameof(BattleBoardState.GetTile))!.ReturnType.IsByRef);
    using var battle = BattleFixture.Duel();
    battle.Read(() =>
    {
      var point = battle.At(0, 0, 0);
      var original = battle.Board.GetTile(point);
      var local = original;
      local.IsWalkable = !original.IsWalkable;
      Assert.Equal(original, battle.Board.GetTile(point));
    });
  }

  [TestCase]
  public void BorrowedAvailabilityRequiresOwnedAccess()
  {
    using var battle = BattleFixture.Duel();
    var action = battle.Query(new GetAvailableActionsForUnit(battle.Alive(battle.PlayerUnit)))[0];
    Assert.Throws<InvalidOperationException>(() => _ = action.IsAvailable);
    battle.Read(() => _ = action.IsAvailable);
  }

  [TestCase]
  public void EventSpanGetterRequiresOwnedAccess()
  {
    using var battle = BattleFixture.Duel();
    var result = battle.AdvanceTurn();
    Assert.Throws<InvalidOperationException>(() => _ = result.EventsThatOccurred.Length);
    battle.Read(() => Assert.True(result.EventsThatOccurred.Length > 0));
  }

  [TestCase]
  public void SharedEquipmentStaysDeployedUntilFinalDrain()
  {
    var weapon = TestData.MakeAmmoWeapon("Shared");
    using var battle = BattleFixture.Duel(player: new("Alpha", Weapon: weapon));
    using var worker = BattleConcurrencyProbe.HoldQuery(battle.Runtime);
    battle.Runtime.RequestClose();
    Assert.False(battle.Runtime.TryFinishDisposal());
    Assert.True(battle.PlayerUnit.Combatant.IsDeployedInBattle);
    Assert.Throws<InvalidOperationException>(() => weapon.Reload());
    worker.Release();
    Assert.True(battle.Runtime.TryFinishDisposal());
    Assert.False(battle.PlayerUnit.Combatant.IsDeployedInBattle);
    weapon.Reload();
    Assert.Equal(weapon.MagazineSize, weapon.CurrentAmmo);
    Assert.Throws<ObjectDisposedException>(() => battle.PlayerUnit.ReceiveDamage(0));
  }

  [TestCase]
  public void ForeignBattleCannotBindDeployedMutableEquipment()
  {
    var weapon = TestData.MakeAmmoWeapon("Shared");
    using var battle = BattleFixture.Duel(player: new("Alpha", Weapon: weapon));
    Assert.Throws<InvalidOperationException>(() =>
    {
      using var foreign = BattleFixture.Duel(player: new("Beta", Weapon: weapon));
    });
    battle.Read(() => Assert.Equal(weapon.MagazineSize, weapon.CurrentAmmo));
  }

  private sealed class MutatingQuery : IBattleSessionQuery<int>
  {
    public int Execute(BattleSession session) { session.RaiseEvents(new SessionStartedBattleEvent()); return 1; }
  }
}
