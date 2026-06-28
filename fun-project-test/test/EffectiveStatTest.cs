using FunProject.Battle;
using FunProject.Stats;
using FunProject.Tests;
using FunProject.Weapons;
using GdUnit4;
using static BattleActionTestHelper;

[TestSuite]
[RequireGodotRuntime]
public class EffectiveStatTest
{
  private FirearmWeaponData MakeFirearmWithSlot(int range = 10) => new()
  {
    Frame = BattleTestFactory.MakeFrame(),
    DamageStat = new DamageStat { BaseValue = 10 },
    CriticalChanceStat = new CriticalChanceStat { BaseValue = 5 },
    RangeStat = new RangeStat { BaseValue = range },
    AmmunitionStat = new AmmunitionStat { BaseValue = 12 },
    DefaultAmmoData = new Ammunition(),
    Capabilities = [new FunProject.Items.Capabilities.ModSlotsCapabilityData { SlotCount = 1 }],
  };

  [TestCase(TestName = "MultiStatMod exposes its stat mods as contributions")]
  public void MultiStatModExposesContributions()
  {
    var mod = new MultiStatMod { StatMods = [new RangeStatMod { Modifiers = [StatModifier.Add(6)] }] };
    int count = 0;
    foreach (var _ in mod.StatContributions) count++;
    Assert.Equal(1, count);
  }

  [TestCase(TestName = "Weapon EffectiveStat with no mods returns the base range")]
  public void WeaponEffectiveStatBase()
  {
    var weapon = new FirearmWeapon(MakeFirearmWithSlot(range: 10));
    Assert.Equal(10f, weapon.EffectiveStat<RangeStat>());
  }

  [TestCase(TestName = "Equipped MultiStatMod raises the weapon's effective range")]
  public void WeaponEffectiveStatWithSlotMod()
  {
    var weapon = new FirearmWeapon(MakeFirearmWithSlot(range: 10));
    weapon.GetModSlots()[0].Equip(new MultiStatMod { StatMods = [new RangeStatMod { Modifiers = [StatModifier.Add(6)] }] });
    Assert.Equal(16f, weapon.EffectiveStat<RangeStat>());
  }

  [TestCase(TestName = "Ammunition modifiers contribute to the weapon's effective stat")]
  public void AmmunitionModsContribute()
  {
    var data = MakeFirearmWithSlot(range: 10);
    data.DefaultAmmoData = new Ammunition { Modifiers = [new RangeStatMod { Modifiers = [StatModifier.Add(3)] }] };
    var weapon = new FirearmWeapon(data);
    Assert.Equal(13f, weapon.EffectiveStat<RangeStat>());
  }

  [TestCase(TestName = "Runtime Faction surfaces authored FactionBonuses")]
  public void FactionSurfacesBonuses()
  {
    var faction = new FunProject.Combatants.Faction(new FunProject.Combatants.FactionData
    {
      Name = "City Guard",
      FactionBonuses = [new HealthStatMod { Modifiers = [StatModifier.Add(5)] }],
    });
    Assert.Equal(1, faction.StatBonuses.Count);
  }

  private static FunProject.Combatants.Combatant CombatantWithSlot(
    FunProject.Combatants.Faction faction, int health = 20, int aim = 65)
    => new(new FunProject.Combatants.CombatantData
    {
      Name = "Alpha",
      HealthStat = new HealthStat { BaseValue = health },
      ActionPointsStat = new ActionPointsStat { BaseValue = 4 },
      WillStat = new WillStat { BaseValue = 50 },
      MovementStat = new MovementStat { BaseValue = 12 },
      VisionStat = new VisionStat { BaseValue = 20 },
      AimStat = new AimStat { BaseValue = aim },
      ModSlotCount = 1,
    }, faction);

  [TestCase(TestName = "A combatant-slot Health mod raises MaxHealth")]
  public void CombatantSlotModRaisesMaxHealth()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var combatant = CombatantWithSlot(faction, health: 20);
    combatant.GetModSlots()[0].Equip(new MultiStatMod { StatMods = [new HealthStatMod { Modifiers = [StatModifier.Add(10)] }] });
    var unit = new FunProject.Battle.BattleUnitState(1, combatant, None, None);
    Assert.Equal(30, unit.MaxHealth);
  }

  [TestCase(TestName = "Faction bonus and combatant slot stack on the same stat")]
  public void FactionAndSlotStack()
  {
    var faction = new FunProject.Combatants.Faction(new FunProject.Combatants.FactionData
    {
      Name = "Player",
      FactionBonuses = [new HealthStatMod { Modifiers = [StatModifier.Add(5)] }],
    });
    var combatant = CombatantWithSlot(faction, health: 20);
    combatant.GetModSlots()[0].Equip(new MultiStatMod { StatMods = [new HealthStatMod { Modifiers = [StatModifier.Add(10)] }] });
    var unit = new FunProject.Battle.BattleUnitState(1, combatant, None, None);
    Assert.Equal(35, unit.MaxHealth);
  }

  [TestCase(TestName = "BattleCombatQueries: a range mod brings a distant target into range")]
  public void RangeModExtendsAttackRange()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var board = new BattleBoardState(new Godot.Vector3I(12, 1, 12));
    var session = BattleTestFactory.MakeSession(board, [playerFaction, enemyFaction]);

    // Base range 3 is too short to reach tile (0,0,8); the +10 slot mod lifts effective range to 13.
    var weaponData = new FirearmWeaponData
    {
      Frame = BattleTestFactory.MakeFrame(),
      DamageStat = new DamageStat { BaseValue = 10 },
      CriticalChanceStat = new CriticalChanceStat { BaseValue = 0 },
      RangeStat = new RangeStat { BaseValue = 3 },
      AmmunitionStat = new AmmunitionStat { BaseValue = 12 },
      DefaultAmmoData = new Ammunition(),
      Capabilities = [new FunProject.Items.Capabilities.ModSlotsCapabilityData { SlotCount = 1 }],
    };
    var weapon = new FirearmWeapon(weaponData);
    weapon.GetModSlots()[0].Equip(new MultiStatMod { StatMods = [new RangeStatMod { Modifiers = [StatModifier.Add(10)] }] });

    // distance 8 > base range 3, but effective range 3+10=13 => in range after edits
    var attacker = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", playerFaction), new Godot.Vector3I(0, 0, 0), weapon);
    var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Hostile", enemyFaction), new Godot.Vector3I(0, 0, 8));
    StartBattle(session);

    var result = BattleQueryTestHelper.Query(session, new GetHitChanceForAttack(attacker.State, target.State));
    Assert.True(result.IsRight); // would be Left (out of range) without the four production edits
  }

  [TestCase(TestName = "An equipped Aim mod raises the hit-chance base")]
  public void AimModRaisesHitChance()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var combatant = CombatantWithSlot(faction, aim: 65);
    combatant.GetModSlots()[0].Equip(new MultiStatMod { StatMods = [new AimStatMod { Modifiers = [StatModifier.Add(15)] }] });

    var board = new FunProject.Battle.BattleBoardState(new Godot.Vector3I(8, 1, 8));
    var attackerPoint = board.ValidatePoint(new Godot.Vector3I(4, 0, 1)).RequireSome();
    var defenderPoint = board.ValidatePoint(new Godot.Vector3I(4, 0, 4)).RequireSome();
    var weapon = BattleTestFactory.MakeWeapon("Rifle");
    var attacker = new FunProject.Battle.BattleUnitState(1, combatant, Some(weapon), None);
    var context = new FunProject.Battle.AttackContext(attacker, attackerPoint, defenderPoint, board);

    var breakdown = new FunProject.Battle.StandardHitChanceCalculator().Calculate(context);
    Assert.Equal(80, breakdown.BaseChance);
  }
}
