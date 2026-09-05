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

  [TestCase(false)]
  [TestCase(true)]
  public void FirstDeliveryCreatesShelfUsingCapturedStockPolicy(bool unlimited)
  {
    var item = MakeItemData(unlimited: !unlimited);
    var armory = new Armory([], []);
    Assert.True(armory.TryGetItemStock(item).IsNone);
    Assert.True(armory.TryWithdrawItem(item).IsNone);

    armory.AddItem(item, unlimited);

    Assert.Equal(1, armory.ItemStock().Count);
    Assert.Equal(unlimited, armory.TryGetItemStock(item).RequireSome().Unlimited);
    Assert.True(armory.HasAvailableItem(item));
    Assert.True(armory.TryWithdrawItem(item).IsSome);
    Assert.Equal(unlimited, armory.HasAvailableItem(item));
    Assert.Equal(unlimited, armory.TryGetItemStock(item).RequireSome().Available);
  }

  [TestCase]
  public void DeliveryAddsToExistingScarceShelf()
  {
    var item = MakeItemData();
    var armory = new Armory([item], []);
    armory.AddItem(item, false);
    Assert.Equal(1, armory.ItemStock().Count);
    Assert.Equal(2, armory.TryGetItemStock(item).RequireSome().Remaining);
  }

  [TestCase]
  public void DeliveryRejectsNullAndExistingUnlimitedSupplyWithoutChangingStock()
  {
    var item = MakeItemData(unlimited: true);
    var armory = new Armory([], []);
    Assert.Throws<ArgumentNullException>(() => armory.AddItem(null!, true));
    Assert.Equal(0, armory.ItemStock().Count);

    armory.AddItem(item, true);
    var stock = armory.TryGetItemStock(item).RequireSome();
    Assert.Throws<InvalidOperationException>(() => armory.AddItem(item, true));
    Assert.Equal(stock, armory.TryGetItemStock(item).RequireSome());
    Assert.Equal(1, armory.ItemStock().Count);
  }

  [TestCase(TestName = "Unknown lookups return None")]
  public void UnknownLookupReturnsNone()
  {
    var unknown = MakeItemData("Unknown");
    var armory = new Armory([MakeItemData("Known")], []);

    Assert.False(armory.TryGetItemStock(unknown).IsSome);
    Assert.False(armory.HasAvailableItem(unknown));
  }

  [TestCase(TestName = "Depositing without a shelf throws before touching mods")]
  public void DepositRejectsMissingShelf()
  {
    EquippableItemData data = new()
    {
      Name = "Supply",
      UnlimitedStock = true,
      Capabilities = [new ModSlotsCapabilityData { SlotCount = 1 }],
    };
    MultiStatMod mod = MakeMod("Scope");
    var armory = new Armory([], [mod]);
    EquippableItem item = ItemRuntimeFactory.Create(data);
    item.GetModSlots()[0].Equip(armory.TryWithdrawMod(mod).ValueUnsafe());

    Assert.Throws<InvalidOperationException>(() => armory.DepositItem(item));
    Assert.True(item.GetModSlots()[0].HasMod); // the guard fired before any mod was pulled off
    Assert.Equal(0, armory.ModStock()[0].Remaining); // and nothing returned to the mod shelf
  }

  [TestCase(TestName = "Counted stock growth uses checked arithmetic")]
  public void CountedGrowthUsesCheckedArithmetic()
  {
    EquippableItemData item = MakeItemData("Pistol");
    MultiStatMod mod = MakeMod("Chip");
    var armory = new Armory([item], [mod]);

    armory.TryWithdrawItem(item); // consume the seeded count; growth starts from zero
    armory.TryWithdrawMod(mod);
    armory.AddStock(item, int.MaxValue);
    Assert.Throws<OverflowException>(() => armory.AddStock(item, 1));
    Assert.Throws<OverflowException>(() => armory.AddItem(item, false));
    Assert.Throws<OverflowException>(() => armory.DepositItem(ItemRuntimeFactory.Create(item)));
    Assert.Equal(int.MaxValue, armory.TryGetItemStock(item).RequireSome().Remaining);

    armory.AddModStock(mod, int.MaxValue);
    Assert.Throws<OverflowException>(() => armory.AddModStock(mod, 1));
    Assert.Throws<OverflowException>(() => armory.DepositMod(mod));
  }
}
