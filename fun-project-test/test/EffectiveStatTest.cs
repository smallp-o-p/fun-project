using FunProject.Battle;
using FunProject.Stats;
using FunProject.Weapons;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public class EffectiveStatTest
{
  [TestCase(TestName = "Weapon EffectiveStat with no mods returns the base range")]
  public void WeaponEffectiveStatBase()
  {
    var weapon = new FirearmWeapon(BattleTestFactory.MakeFirearmWeaponData(range: 10, modSlots: 1, ammo: new Ammunition()));
    Assert.Equal(10f, weapon.EffectiveStat<RangeStat>());
  }

  [TestCase(TestName = "Equipped MultiStatMod raises the weapon's effective range")]
  public void WeaponEffectiveStatWithSlotMod()
  {
    var weapon = new FirearmWeapon(BattleTestFactory.MakeFirearmWeaponData(range: 10, modSlots: 1, ammo: new Ammunition()));
    weapon.GetModSlots()[0].Equip(new MultiStatMod { StatMods = [new RangeStatMod { Modifiers = [StatModifier.Add(6)] }] });
    Assert.Equal(16f, weapon.EffectiveStat<RangeStat>());
  }

  [TestCase(TestName = "Ammunition modifiers contribute to the weapon's effective stat")]
  public void AmmunitionModsContribute()
  {
    var data = BattleTestFactory.MakeFirearmWeaponData(range: 10, modSlots: 1, ammo: new Ammunition());
    data.DefaultAmmoData = new Ammunition { Modifiers = [new RangeStatMod { Modifiers = [StatModifier.Add(3)] }] };
    var weapon = new FirearmWeapon(data);
    Assert.Equal(13f, weapon.EffectiveStat<RangeStat>());
  }

  [TestCase(TestName = "A combatant-slot Health mod raises MaxHealth")]
  public void CombatantSlotModRaisesMaxHealth()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var combatant = BattleTestFactory.MakeCombatant("Alpha", faction, health: 20, modSlotCount: 1);
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
    var combatant = BattleTestFactory.MakeCombatant("Alpha", faction, health: 20, modSlotCount: 1);
    combatant.GetModSlots()[0].Equip(new MultiStatMod { StatMods = [new HealthStatMod { Modifiers = [StatModifier.Add(10)] }] });
    var unit = new FunProject.Battle.BattleUnitState(1, combatant, None, None);
    Assert.Equal(35, unit.MaxHealth);
  }

  [TestCase(TestName = "BattleCombatQueries: a range mod brings a distant target into range")]
  public void RangeModExtendsAttackRange()
  {
    var playerFaction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var board = new BattleBoardState(new Vector3I(12, 1, 12));
    var session = BattleTestFactory.MakeSession(board, [playerFaction, enemyFaction]);

    // Base range 3 is too short to reach tile (0,0,8); the +10 slot mod lifts effective range to 13.
    var weaponData = BattleTestFactory.MakeFirearmWeaponData(range: 3, critChance: 0, modSlots: 1, ammo: new Ammunition());
    var weapon = new FirearmWeapon(weaponData);
    weapon.GetModSlots()[0].Equip(new MultiStatMod { StatMods = [new RangeStatMod { Modifiers = [StatModifier.Add(10)] }] });

    // distance 8 > base range 3, but effective range 3+10=13 => in range after edits
    var attacker = SpawnUnit(session, BattleTestFactory.MakeCombatant("Alpha", playerFaction), new Vector3I(0, 0, 0), weapon);
    var target = SpawnUnit(session, BattleTestFactory.MakeCombatant("Hostile", enemyFaction), new Vector3I(0, 0, 8));
    StartBattle(session);

    var result = BattleQueryTestHelper.Query(session, new GetHitChanceForAttack(attacker.AliveIn(session), target.AliveIn(session)));
    Assert.True(result.IsRight); // would be Left (out of range) without the four production edits
  }

  [TestCase(TestName = "An equipped Aim mod raises the hit-chance base")]
  public void AimModRaisesHitChance()
  {
    var faction = BattleTestFactory.MakeFaction("Player");
    var enemyFaction = BattleTestFactory.MakeFaction("Enemy");
    var combatant = BattleTestFactory.MakeCombatant("Alpha", faction, aim: 65, modSlotCount: 1);
    combatant.GetModSlots()[0].Equip(new MultiStatMod { StatMods = [new AimStatMod { Modifiers = [StatModifier.Add(15)] }] });

    var board = new FunProject.Battle.BattleBoardState(new Vector3I(8, 1, 8));
    var attackerPoint = board.ValidatePoint(new Vector3I(4, 0, 1)).RequireSome();
    var defenderPoint = board.ValidatePoint(new Vector3I(4, 0, 4)).RequireSome();
    var weapon = BattleTestFactory.MakeWeapon("Rifle");
    var attacker = new FunProject.Battle.BattleUnitState(1, combatant, Some(weapon), None);
    var context = new FunProject.Battle.AttackContext(attacker, weapon, attackerPoint, defenderPoint, board);

    var breakdown = new FunProject.Battle.StandardHitChanceCalculator().Calculate(context);
    Assert.Equal(80, breakdown.BaseChance);
  }
}
