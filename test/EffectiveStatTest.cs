using System;
using FunProject.Battle;
using FunProject.Items.Capabilities;
using FunProject.Stats;
using FunProject.Weapons;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class EffectiveStatTest
{
  private static FirearmWeaponData FirearmData(int range, int slots, Ammunition? ammo = null) =>
    TestData.MakeFirearmWeaponData(range: range, modSlots: slots, ammo: ammo ?? new Ammunition());

  private static FirearmWeapon Firearm(int range, int slots, Ammunition? ammo = null) =>
    new(FirearmData(range, slots, ammo));

  private static FirearmWeapon WithSlotMod(FirearmWeapon weapon, MultiStatMod mod)
  {
    weapon.GetModSlots()[0].Equip(mod);
    return weapon;
  }

  private static MultiStatMod SlotMod(string name = "", params StatMod[] statMods) =>
    new() { Name = name, StatMods = [.. statMods] };

  [TestCase(TestName = "Slot and ammunition contributions compute the weapon's effective range")]
  public void WeaponRangeContributionTable()
  {
    (string Name, Func<FirearmWeapon> Make, float Expected)[] rows =
    [
      ("base range with empty slots and ammunition", () => Firearm(10, 1), 10f),
      ("a slot Range Add6 raises it to 16",
        () => WithSlotMod(Firearm(10, 1), SlotMod(statMods: [new RangeStatMod { Modifiers = [StatModifier.Add(6)] }])), 16f),
      ("ammunition Range Add3 raises it to 13",
        () => Firearm(10, 1, new Ammunition { Modifiers = [new RangeStatMod { Modifiers = [StatModifier.Add(3)] }] }), 13f),
      // Separate mods: (10 + 5) * 1.5 = 22.5.
      ("separate Add and PercentAdd mods stack to 22.5",
        () =>
        {
          var data = FirearmData(1, 1);
          data.RangeStat.BaseValue = 10;
          return WithSlotMod(new FirearmWeapon(data), SlotMod(statMods:
            [new RangeStatMod { Modifiers = [StatModifier.Add(5)] },
              new RangeStatMod { Modifiers = [StatModifier.PercentAdd(0.5f)] }]));
        }, 22.5f),
      // One mod's own ops are bucketed: (10 + 5) * 1.2 = 18.
      ("one mod's PercentAdd then Add bucket to 18",
        () =>
        {
          var data = FirearmData(1, 1);
          data.RangeStat.BaseValue = 10;
          return WithSlotMod(new FirearmWeapon(data),
            SlotMod("+20% Range", [new RangeStatMod { Modifiers = [StatModifier.PercentAdd(0.2f), StatModifier.Add(5)] }]));
        }, 18f),
    ];

    foreach (var row in rows)
      Assert.Equal(row.Expected, row.Make().EffectiveStat<RangeStat>(), row.Name);
  }

  [TestCase(TestName = "Multi-stat mods and separate slots each apply their own stat targets")]
  public void WeaponMultiStatContributionTable()
  {
    var tacticalData = FirearmData(1, 1);
    var tactical = WithSlotMod(new FirearmWeapon(tacticalData), SlotMod("Tactical Overhaul",
      [new CriticalChanceStatMod { Modifiers = [StatModifier.Add(2)] },
        new RangeStatMod { Modifiers = [StatModifier.Add(6)] }]));
    Assert.Equal(tacticalData.CriticalChanceStat.BaseValue + 2, tactical.EffectiveStat<CriticalChanceStat>());
    Assert.Equal(tacticalData.RangeStat.BaseValue + 6, tactical.EffectiveStat<RangeStat>());

    var splitData = FirearmData(1, 1);
    splitData.Capabilities = [new ModSlotsCapabilityData { SlotCount = 2 }];
    var split = new FirearmWeapon(splitData);
    split.GetModSlots()[0].Equip(SlotMod(statMods: [new RangeStatMod { Modifiers = [StatModifier.Add(10)] }]));
    split.GetModSlots()[1].Equip(SlotMod(statMods: [new CriticalChanceStatMod { Modifiers = [StatModifier.PercentAdd(0.5f)] }]));
    Assert.Equal(splitData.RangeStat.BaseValue + 10, split.EffectiveStat<RangeStat>());
    Assert.Equal(splitData.CriticalChanceStat.BaseValue * 1.5f, split.EffectiveStat<CriticalChanceStat>());
  }

  [TestCase(TestName = "Combatant slot and faction Health contributions raise MaxHealth")]
  public void CombatantHealthContributionTable()
  {
    var faction = TestData.MakeFaction("Player");
    var slotted = TestData.MakeCombatant("Alpha", faction, health: 20, modSlotCount: 1);
    slotted.GetModSlots()[0].Equip(SlotMod(statMods: [new HealthStatMod { Modifiers = [StatModifier.Add(10)] }]));
    var unit = new FunProject.Battle.BattleUnitState(1, slotted, None, None);
    Assert.Equal(30, unit.MaxHealth);

    var bonusFaction = new FunProject.Combatants.Faction(new FunProject.Combatants.FactionData
    {
      Name = "Player",
      FactionBonuses = [new HealthStatMod { Modifiers = [StatModifier.Add(5)] }],
    });
    var stacked = TestData.MakeCombatant("Alpha", bonusFaction, health: 20, modSlotCount: 1);
    stacked.GetModSlots()[0].Equip(SlotMod(statMods: [new HealthStatMod { Modifiers = [StatModifier.Add(10)] }]));
    var stackedUnit = new FunProject.Battle.BattleUnitState(1, stacked, None, None);
    Assert.Equal(35, stackedUnit.MaxHealth);
  }

  [TestCase(TestName = "BattleCombatQueries: a range mod brings a distant target into range")]
  public void RangeModExtendsAttackRange()
  {
    var playerFaction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    using var battle = new BattleFixture(new Vector3I(12, 1, 12), [playerFaction, enemyFaction]);

    // Base range 3 is too short to reach tile (0,0,8); the +10 slot mod lifts effective range to 13.
    var weaponData = TestData.MakeFirearmWeaponData(range: 3, critChance: 0, modSlots: 1, ammo: new Ammunition());
    var weapon = new FirearmWeapon(weaponData);
    weapon.GetModSlots()[0].Equip(new MultiStatMod { StatMods = [new RangeStatMod { Modifiers = [StatModifier.Add(10)] }] });

    // distance 8 > base range 3, but effective range 3+10=13 => in range after edits
    var attacker = battle.Spawn(TestData.MakeCombatant("Alpha", playerFaction), new Vector3I(0, 0, 0), weapon);
    var target = battle.Spawn(TestData.MakeCombatant("Hostile", enemyFaction), new Vector3I(0, 0, 8));
    battle.Start();

    var result = battle.Query(new GetHitChanceForAttack(battle.Alive(attacker), battle.Target(target)));
    Assert.True(result.IsRight); // would be Left (out of range) without the four production edits
  }

  [TestCase(TestName = "An equipped Aim mod raises the hit-chance base")]
  public void AimModRaisesHitChance()
  {
    var faction = TestData.MakeFaction("Player");
    var enemyFaction = TestData.MakeFaction("Enemy");
    var combatant = TestData.MakeCombatant("Alpha", faction, aim: 65, modSlotCount: 1);
    combatant.GetModSlots()[0].Equip(new MultiStatMod { StatMods = [new AimStatMod { Modifiers = [StatModifier.Add(15)] }] });

    var board = new FunProject.Battle.BattleBoardState(new Vector3I(8, 1, 8));
    var attackerPoint = board.ValidatePoint(new Vector3I(4, 0, 1)).RequireSome();
    var defenderPoint = board.ValidatePoint(new Vector3I(4, 0, 4)).RequireSome();
    var weapon = TestData.MakeWeapon("Rifle");
    var attacker = new FunProject.Battle.BattleUnitState(1, combatant, Some(weapon), None);
    var context = new FunProject.Battle.AttackContext(attacker, weapon, attackerPoint, defenderPoint, board);

    var breakdown = new FunProject.Battle.StandardHitChanceCalculator().Calculate(context);
    Assert.Equal(80, breakdown.BaseChance);
  }
}
