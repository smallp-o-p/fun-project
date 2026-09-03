using FunProject.GameState;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Stats;
using GdUnit4;
using LanguageExt.UnsafeValueAccess;
using static FunProject.Tests.GeoscapeTestFactory;
using static GdUnit4.Assertions;
using System;

[TestSuite]
[RequireGodotRuntime]
public class ArmoryTest
{
  private static ArmoryEntryData Entry(EquippableItemData item, int count) => new() { Item = item, Count = count };
  private static ModStockEntryData ModEntry(EquippableMod mod, int count) => new() { Mod = mod, Count = count };

  private static MultiStatMod MakeMod(string name) => new() { Name = name };

  [TestCase(TestName = "Limited entry hands out exactly Count instances then runs dry")]
  public void LimitedEntryExhausts()
  {
    EquippableItemData data = new() { Name = "Pistol" };
    var armory = new Armory([Entry(data, 2)], []);

    Option<EquippableItem> first = armory.TryWithdrawItem(data);
    Option<EquippableItem> second = armory.TryWithdrawItem(data);
    Option<EquippableItem> third = armory.TryWithdrawItem(data);

    Assert.True(first.IsSome);
    Assert.True(second.IsSome);
    Assert.False(third.IsSome);
  }

  [TestCase(TestName = "Deposit returns the very instance that was withdrawn")]
  public void DepositReturnsInstance()
  {
    EquippableItemData data = new() { Name = "Pistol" };
    var armory = new Armory([Entry(data, 1)], []);
    EquippableItem withdrawn = armory.TryWithdrawItem(data).ValueUnsafe();

    armory.DepositItem(withdrawn);
    EquippableItem again = armory.TryWithdrawItem(data).ValueUnsafe();

    AssertThat(again).IsSame(withdrawn);
  }

  [TestCase(TestName = "Unlimited entry always yields fresh distinct instances and ignores deposits")]
  public void UnlimitedEntryInstantiates()
  {
    EquippableItemData data = new() { Name = "Grenade" };
    var armory = new Armory([Entry(data, -1)], []);

    EquippableItem a = armory.TryWithdrawItem(data).ValueUnsafe();
    EquippableItem b = armory.TryWithdrawItem(data).ValueUnsafe();
    armory.DepositItem(a);

    AssertThat(a).IsNotSame(b);
    Assert.True(armory.TryWithdrawItem(data).IsSome);
  }

  [TestCase(TestName = "Depositing an unlimited item returns its equipped mods and discards the instance")]
  public void UnlimitedDepositReturnsMods()
  {
    EquippableItemData data = new()
    {
      Name = "Rifle",
      Capabilities = [new ModSlotsCapabilityData { SlotCount = 1 }],
    };
    MultiStatMod mod = MakeMod("Scope");
    var armory = new Armory([Entry(data, -1)], [ModEntry(mod, 1)]);
    EquippableItem item = armory.TryWithdrawItem(data).ValueUnsafe();
    item.GetModSlots()[0].Equip(armory.TryWithdrawMod(mod).ValueUnsafe());

    armory.DepositItem(item);
    EquippableItem fresh = armory.TryWithdrawItem(data).ValueUnsafe();

    Assert.Equal(1, armory.ModStock()[0].Remaining);
    Assert.False(fresh.GetModSlots()[0].HasMod);
    AssertThat(fresh).IsNotSame(item);
  }

  [TestCase(TestName = "Mod shelf counts down and back up")]
  public void ModShelfCounts()
  {
    MultiStatMod mod = MakeMod("Chip");
    var armory = new Armory([], [ModEntry(mod, 1)]);

    AssertThat(armory.TryWithdrawMod(mod).ValueUnsafe()).IsSame(mod);
    Assert.False(armory.TryWithdrawMod(mod).IsSome);
    armory.DepositMod(mod);
    Assert.True(armory.TryWithdrawMod(mod).IsSome);
  }

  [TestCase(TestName = "Unlimited mods never run dry")]
  public void UnlimitedMods()
  {
    MultiStatMod mod = MakeMod("Chip");
    var armory = new Armory([], [ModEntry(mod, -1)]);

    armory.TryWithdrawMod(mod);
    armory.TryWithdrawMod(mod);

    Assert.True(armory.TryWithdrawMod(mod).IsSome);
  }

  [TestCase(TestName = "Stock lines report remaining counts and unlimited flags")]
  public void StockLines()
  {
    EquippableItemData pistol = new() { Name = "Pistol" };
    EquippableItemData grenade = new() { Name = "Grenade" };
    MultiStatMod mod = MakeMod("Chip");
    var armory = new Armory([Entry(pistol, 2), Entry(grenade, -1)], [ModEntry(mod, -1)]);

    armory.TryWithdrawItem(pistol);

    Assert.Equal(2, armory.ItemStock().Count);
    Assert.Equal(1, armory.ItemStock()[0].Remaining);
    Assert.False(armory.ItemStock()[0].Unlimited);
    Assert.True(armory.ItemStock()[1].Unlimited);
    Assert.True(armory.ModStock()[0].Unlimited);
  }

  [TestCase(TestName = "Unknown item data withdraws None and unknown deposits throw")]
  public void UnknownEntries()
  {
    EquippableItemData unknown = new() { Name = "Unknown" };
    EquippableItemData known = new() { Name = "Known" };
    var armory = new Armory([Entry(known, 1)], []);

    Assert.False(armory.TryWithdrawItem(unknown).IsSome);
    Assert.Throws<InvalidOperationException>(() => armory.DepositItem(
      ItemRuntimeFactory.Create(new EquippableItemData { Name = "Also Unknown" })));
  }

  [TestCase(TestName = "Authoring mistakes throw at construction")]
  public void AuthoringValidation()
  {
    Assert.Throws<ArgumentNullException>(() => new Armory(null!, []));
    Assert.Throws<InvalidOperationException>(() => new Armory([new ArmoryEntryData { Item = null! }], []));
    Assert.Throws<InvalidOperationException>(() => new Armory(
      [Entry(new EquippableItemData { Name = "P" }, -2)], []));
    EquippableItemData dup = new() { Name = "Dup" };
    Assert.Throws<InvalidOperationException>(() => new Armory([Entry(dup, 1), Entry(dup, 1)], []));
    Assert.Throws<InvalidOperationException>(() => new Armory([], [new ModStockEntryData { Mod = null! }]));
    MultiStatMod same = MakeMod("A");
    Assert.Throws<InvalidOperationException>(() => new Armory([], [ModEntry(same, -1), ModEntry(same, 1)]));
  }
}
