using FunProject.GameState;
using FunProject.Items;
using FunProject.Items.Capabilities;
using FunProject.Stats;
using GdUnit4;
using LanguageExt.UnsafeValueAccess;
using static GdUnit4.Assertions;
using System;

[TestSuite]
[RequireGodotRuntime]
public class ArmoryTest
{
  [TestCase(TestName = "A listed scarce item seeds one; AddStock grows the count at runtime")]
  public void ScarceEntrySeedsOneInstance()
  {
    EquippableItemData data = TestData.MakeItemData("Pistol");
    var armory = new Armory([data], []);

    Option<EquippableItem> first = armory.TryWithdrawItem(data);
    Option<EquippableItem> second = armory.TryWithdrawItem(data);
    Assert.True(first.IsSome);
    Assert.False(second.IsSome);

    armory.AddStock(data, 2);
    Assert.True(armory.TryWithdrawItem(data).IsSome);
    Assert.True(armory.TryWithdrawItem(data).IsSome);
    Assert.False(armory.TryWithdrawItem(data).IsSome);
  }

  [TestCase(TestName = "Unlimited entries always yield fresh distinct instances and ignore deposits")]
  public void UnlimitedEntryInstantiates()
  {
    EquippableItemData data = TestData.MakeItemData("Grenade", unlimited: true);
    var armory = new Armory([data], []);

    EquippableItem a = armory.TryWithdrawItem(data).ValueUnsafe();
    EquippableItem b = armory.TryWithdrawItem(data).ValueUnsafe();
    armory.DepositItem(a);

    AssertThat(a).IsNotSame(b);
    Assert.True(armory.TryWithdrawItem(data).IsSome);
  }

  [TestCase(TestName = "Depositing an item returns its equipped mods and counts it back into stock")]
  public void DepositReturnsModsAndCountsIn()
  {
    EquippableItemData data = new()
    {
      Name = "Rifle",
      Capabilities = [new ModSlotsCapabilityData { SlotCount = 1 }],
    };
    MultiStatMod mod = TestData.MakeMod("Scope");
    var armory = new Armory([data], [mod]);
    EquippableItem item = armory.TryWithdrawItem(data).ValueUnsafe();
    item.GetModSlots()[0].Equip(armory.TryWithdrawMod(mod).ValueUnsafe());

    armory.DepositItem(item);
    EquippableItem fresh = armory.TryWithdrawItem(data).ValueUnsafe();

    Assert.Equal(1, armory.ModStock()[0].Remaining); // mod back on its shelf
    Assert.False(fresh.GetModSlots()[0].HasMod); // stock is anonymous: fresh is factory state
    AssertThat(fresh).IsNotSame(item); // a NEW materialized instance, not the deposited one
  }

  [TestCase(TestName = "Mod shelf counts down and back up, and AddModStock grows it at runtime")]
  public void ModShelfCounts()
  {
    MultiStatMod mod = TestData.MakeMod("Chip");
    var armory = new Armory([], [mod]);

    AssertThat(armory.TryWithdrawMod(mod).ValueUnsafe()).IsSame(mod);
    Assert.False(armory.TryWithdrawMod(mod).IsSome);

    armory.AddModStock(mod, 2);
    Assert.True(armory.TryWithdrawMod(mod).IsSome);
    Assert.True(armory.TryWithdrawMod(mod).IsSome);
    Assert.False(armory.TryWithdrawMod(mod).IsSome);

    armory.DepositMod(mod);
    Assert.True(armory.TryWithdrawMod(mod).IsSome);
  }

  [TestCase(TestName = "Unlimited mods never run dry")]
  public void UnlimitedMods()
  {
    MultiStatMod mod = TestData.MakeMod("Chip", unlimited: true);
    var armory = new Armory([], [mod]);

    armory.TryWithdrawMod(mod);
    armory.TryWithdrawMod(mod);

    Assert.True(armory.TryWithdrawMod(mod).IsSome);
  }

  [TestCase(TestName = "Stock lines report remaining counts and unlimited flags")]
  public void StockLines()
  {
    EquippableItemData pistol = TestData.MakeItemData("Pistol");
    EquippableItemData grenade = TestData.MakeItemData("Grenade", unlimited: true);
    MultiStatMod mod = TestData.MakeMod("Chip", unlimited: true);
    var armory = new Armory([pistol, grenade], [mod]);

    armory.TryWithdrawItem(pistol);

    Assert.Equal(2, armory.ItemStock().Count);
    Assert.Equal(0, armory.ItemStock()[0].Remaining);
    Assert.False(armory.ItemStock()[0].Unlimited);
    Assert.True(armory.ItemStock()[1].Unlimited);
    Assert.True(armory.ModStock()[0].Unlimited);
  }

  [TestCase(TestName = "Unknown item data withdraws None and unknown deposits throw")]
  public void UnknownEntries()
  {
    EquippableItemData unknown = TestData.MakeItemData("Unknown");
    EquippableItemData known = TestData.MakeItemData("Known");
    var armory = new Armory([known], []);

    Assert.False(armory.TryWithdrawItem(unknown).IsSome);
    Assert.Throws<InvalidOperationException>(() => armory.DepositItem(
      ItemRuntimeFactory.Create(new EquippableItemData { Name = "Also Unknown" })));
  }

  [TestCase(TestName = "Construction rejects null lists, null entries, and duplicates")]
  public void ConstructionValidation()
  {
    Assert.Throws<ArgumentNullException>(() => new Armory(null!, []));
    Assert.Throws<ArgumentNullException>(() => new Armory([null!], []));
    Assert.Throws<ArgumentNullException>(() => new Armory([], [null!]));
    EquippableItemData dupItem = TestData.MakeItemData("Dup");
    Assert.Throws<InvalidOperationException>(() => new Armory([dupItem, dupItem], []));
    MultiStatMod dupMod = TestData.MakeMod("Dup");
    Assert.Throws<InvalidOperationException>(() => new Armory([], [dupMod, dupMod]));
  }

  [TestCase(TestName = "Runtime stock growth validates its inputs")]
  public void RuntimeGrowthValidation()
  {
    EquippableItemData scarce = TestData.MakeItemData("Pistol");
    EquippableItemData plentiful = TestData.MakeItemData("Grenade", unlimited: true);
    MultiStatMod scarceMod = TestData.MakeMod("Chip");
    MultiStatMod plentifulMod = TestData.MakeMod("Core", unlimited: true);
    var armory = new Armory([scarce, plentiful], [scarceMod, plentifulMod]);

    Assert.Throws<ArgumentOutOfRangeException>(() => armory.AddStock(scarce, -1));
    Assert.Throws<InvalidOperationException>(() => armory.AddStock(TestData.MakeItemData("Unknown"), 1));
    Assert.Throws<InvalidOperationException>(() => armory.AddStock(plentiful, 1));
    Assert.Throws<ArgumentOutOfRangeException>(() => armory.AddModStock(scarceMod, -1));
    Assert.Throws<InvalidOperationException>(() => armory.AddModStock(TestData.MakeMod("Unknown"), 1));
    Assert.Throws<InvalidOperationException>(() => armory.AddModStock(plentifulMod, 1));
  }
}
