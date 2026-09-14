using System;
using FunProject.Battle;
using FunProject.Buffs;
using FunProject.Stats;
using FunProject.Strategic;
using FunProject.Weapons;
using GdUnit4;

// Tactical condition integration: effective max health at spawn, the health-damage
// bookkeeping that feeds mission returns, and the summary reports the campaign consumes.
// Campaign roster instances participate directly (never copies) so condition penalties
// written by one battle are visible to the next spawn; campaign-aware spawns pass the
// condition registry's contributions explicitly as spawn stat mods.
[TestSuite]
[RequireGodotRuntime]
public class ConditionBattleTest
{
  [TestCase(TestName = "Spawn max health uses the loadout weapon and sees persistent conditions")]
  public void SpawnMaxHealthUsesLoadoutWeaponAndConditions()
  {
    var faction = TestData.MakeFaction("Player");
    var alpha = TestData.MakeCombatant("Alpha", faction, health: 100);
    // A pre-existing injury cuts effective max health to 75%.
    var conditions = new CombatantConditionSystem();
    conditions.ApplyMissionReturn(alpha, 25, 100, 0);
    // The combatant's own weapon slot must NOT feed the unit: only the explicit loadout does.
    alpha.EquipWeapon(WeaponWithHealthBonus(10));

    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [faction]);
    var unit = battle.Spawn(alpha, Vector3I.Zero,
      weapon: WeaponWithHealthBonus(30), statMods: conditions.StatContributions(alpha));

    // MaxHealth carries the loadout weapon and the injury: (100 + 30) x 0.75 = 97.5 -> 98.
    Assert.Equal(98, unit.MaxHealth);
  }

  [TestCase(TestName = "The effective max health is floored at one")]
  public void EffectiveMaxHealthIsFlooredAtOne()
  {
    var faction = TestData.MakeFaction("Player");
    var alpha = TestData.MakeCombatant("Alpha", faction, health: 100);
    // The active injury contributes the CapMin(1) health floor, keeping the spawned unit
    // alive at one hit point while the loadout zeroes the stat out.
    var conditions = new CombatantConditionSystem();
    conditions.ApplyMissionReturn(alpha, 1, 100, 0);
    var zeroing = new FirearmWeapon(TestData.MakeFirearmWeaponData(name: "Nullifier", modSlots: 1));
    zeroing.GetModSlots()[0].Equip(new MultiStatMod
    {
      StatMods = [new HealthStatMod { Modifiers = [StatModifier.Multiply(0f)] }],
    });

    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [faction]);
    var unit = battle.Spawn(alpha, Vector3I.Zero,
      weapon: zeroing, statMods: conditions.StatContributions(alpha));

    Assert.True(unit.IsAlive);
    Assert.Equal(1, unit.MaxHealth);
  }

  [TestCase(TestName = "Overkill counts only the health actually lost")]
  public void OverkillCountsOnlyHealthActuallyLost()
  {
    var faction = TestData.MakeFaction("Player");
    var alpha = TestData.MakeCombatant("Alpha", faction, health: 20);
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [faction]);
    var unit = battle.Spawn(alpha, Vector3I.Zero);

    battle.ApplyDamage(unit, 999);

    Assert.True(unit.IsDead);
    Assert.Equal(20L, unit.TotalHealthDamageTaken);
    battle.Session.EndBattle(BattleOutcome.Victory);
    var summary = battle.Query(new GetFactionEndOfBattleSummary(faction)).RequireRight();
    Assert.False(summary.HealthByCombatant.ContainsKey(alpha),
      "Dead participants are not part of the health report.");
  }

  [TestCase(TestName = "Stun-only damage never counts toward health metrics")]
  public void StunOnlyDamageNeverCountsTowardHealthMetrics()
  {
    var faction = TestData.MakeFaction("Player");
    var alpha = TestData.MakeCombatant("Alpha", faction, health: 20);
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [faction]);
    var unit = battle.Spawn(alpha, Vector3I.Zero);

    battle.ApplyDamage(unit, 15, DamageKind.Stun);

    Assert.Equal(20, unit.CurrentHealth);
    Assert.Equal(0L, unit.TotalHealthDamageTaken);
    battle.Session.EndBattle(BattleOutcome.Victory);
    var summary = battle.Query(new GetFactionEndOfBattleSummary(faction)).RequireRight();
    Assert.Equal(0L, summary.HealthByCombatant[alpha].HealthDamageTaken);
  }

  [TestCase(TestName = "Armor-absorbed damage never counts toward health metrics")]
  public void ArmorAbsorbedDamageNeverCountsTowardHealthMetrics()
  {
    var faction = TestData.MakeFaction("Player");
    var alpha = TestData.MakeCombatant("Alpha", faction, health: 100);
    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [faction]);
    var unit = battle.Spawn(alpha, Vector3I.Zero, armor: TestData.MakeArmor("Vest", armor: 10));

    battle.ApplyDamage(unit, 5);

    Assert.Equal(100, unit.CurrentHealth);
    Assert.Equal(0L, unit.TotalHealthDamageTaken);
  }

  [TestCase(TestName = "Max-health clamping from stat changes is not damage taken")]
  public void MaxHealthClampingIsNotDamageTaken()
  {
    var faction = TestData.MakeFaction("Player");
    var enemy = TestData.MakeFaction("Enemy");
    // A negative max-health buff exercising the real clamp path: it stays inactive at
    // spawn and activates on the turn-start hook once an enemy stands adjacent.
    var intimidation = TestData.MakeBuff("Intimidated", new AdjacentEnemyCondition(),
      statMods: [new HealthStatMod { Modifiers = [StatModifier.Multiply(0.4f)] }]);
    var alpha = TestData.MakeCombatant("Alpha", faction, health: 100, buffs: [intimidation]);

    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [faction, enemy]);
    var unit = battle.Spawn(alpha, Vector3I.Zero);
    Assert.Equal(100, unit.CurrentHealth); // no adjacent enemy yet
    battle.Spawn(TestData.MakeCombatant("Enemy", enemy), new Vector3I(1, 0, 0));
    battle.Start(); // turn-start hook evaluates Alpha's now-satisfied condition

    Assert.Equal(40, unit.CurrentHealth);
    Assert.Equal(0L, unit.TotalHealthDamageTaken);
    battle.Session.EndBattle(BattleOutcome.Victory);
    var summary = battle.Query(new GetFactionEndOfBattleSummary(faction)).RequireRight();
    Assert.Equal(40, summary.HealthByCombatant[alpha].MaxHealth,
      "The report reads the current effective max health, not a spawn snapshot.");
    Assert.Equal(0L, summary.HealthByCombatant[alpha].HealthDamageTaken,
      "Health clamps are not damage.");
  }

  [TestCase(TestName = "Injured roster spawns enter the next battle with combined penalties")]
  public void InjuredRosterSpawnsEnterReducedWithCombinedPenalties()
  {
    using var campaign = new GeoscapeFixture(TestData.MakeStart(
      roster: [TestData.MakeEntry("Alpha", TestData.MakeCombatantData(health: 100, aim: 60))]));
    var alpha = campaign.State.Roster[0];

    using var firstBattle = new BattleFixture(new Vector3I(5, 1, 5), [campaign.State.PlayerFaction]);
    var wounded = firstBattle.Spawn(alpha, Vector3I.Zero);
    firstBattle.ApplyDamage(wounded, 25);
    firstBattle.Session.EndBattle(BattleOutcome.Victory);
    campaign.Session.ApplyMissionReturn(
      firstBattle.Query(new GetFactionEndOfBattleSummary(campaign.State.PlayerFaction)).RequireRight());
    Assert.Equal(2, campaign.State.Conditions.GetInjury(alpha).RequireSome().Tier); // Injured
    Assert.Equal(1, campaign.State.Conditions.GetFatigue(alpha).RequireSome().Tier); // Tired

    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [campaign.State.PlayerFaction]);
    var unit = battle.Spawn(alpha, Vector3I.Zero,
      statMods: campaign.State.Conditions.StatContributions(alpha));

    Assert.Equal(75, unit.MaxHealth);
    Assert.Equal(75, unit.CurrentHealth);
    // Injured cuts aim and movement by 10%; Tired cuts aim another 10%: 60 -> 48.6 -> 49,
    // 12 -> 10.8 -> 11.
    Assert.Equal(48.6f, unit.EffectiveStat<AimStat>());
    Assert.Equal(10.8f, unit.EffectiveStat<MovementStat>());
  }

  [TestCase(TestName = "Successive mission returns measure damage against the effective max health")]
  public void SuccessiveReturnsUseEffectiveMaxHealth()
  {
    using var campaign = new GeoscapeFixture(TestData.MakeStart(
      roster: [TestData.MakeEntry("Alpha", TestData.MakeCombatantData(health: 100))]));
    var alpha = campaign.State.Roster[0];

    // First battle: 25 damage on 100 health earns two tiers.
    campaign.ReturnFromMission((alpha, 25, 0));
    Assert.Equal(2, campaign.State.Conditions.GetInjury(alpha).RequireSome().Tier); // Injured

    // Second battle: the same combatant now spawns at an effective 75 max health, so the
    // report's denominator is 75 — not the healthy 100.
    var summary = campaign.PlayMission(squad: [(alpha, 20, 0)]);
    Assert.Equal(75, summary.HealthByCombatant[alpha].MaxHealth);
    Assert.Equal(20L, summary.HealthByCombatant[alpha].HealthDamageTaken);

    campaign.Session.ApplyMissionReturn(summary);
    // 20/75 >= 25% earns two more tiers on top of Injured -> CriticalCondition; measuring
    // against a healthy 100 would wrongly stop at GravelyInjured.
    Assert.Equal(4, campaign.State.Conditions.GetInjury(alpha).RequireSome().Tier); // Critically Injured
  }

  [TestCase(TestName = "Successive pipeline hits on one unit accumulate into the mission return")]
  public void SuccessivePipelineHitsAccumulateIntoTheMissionReturn()
  {
    using var campaign = new GeoscapeFixture(TestData.MakeStart(
      roster: [TestData.MakeEntry("Alpha", TestData.MakeCombatantData(health: 100))]));
    var alpha = campaign.State.Roster[0];

    using var battle = new BattleFixture(new Vector3I(5, 1, 5), [campaign.State.PlayerFaction]);
    var unit = battle.Spawn(alpha, Vector3I.Zero);
    // Two separate normal-pipeline hits on the same unit must sum to 30 — a last-hit
    // overwrite would report 10 and under-award the return's injury tiers.
    battle.ApplyDamage(unit, 20);
    battle.ApplyDamage(unit, 10);
    battle.Session.EndBattle(BattleOutcome.Victory);

    var summary = battle.Query(new GetFactionEndOfBattleSummary(campaign.State.PlayerFaction)).RequireRight();
    Assert.Equal(100, summary.HealthByCombatant[alpha].MaxHealth);
    Assert.Equal(30L, summary.HealthByCombatant[alpha].HealthDamageTaken);

    campaign.Session.ApplyMissionReturn(summary);
    // 30% of max health earns two tiers: Healthy -> LightlyInjured -> Injured.
    Assert.Equal(2, campaign.State.Conditions.GetInjury(alpha).RequireSome().Tier);
    Assert.Equal(1, campaign.State.Conditions.GetFatigue(alpha).RequireSome().Tier); // Tired
  }

  private static FirearmWeapon WeaponWithHealthBonus(int bonus)
  {
    var weapon = new FirearmWeapon(TestData.MakeFirearmWeaponData(name: $"Rifle +{bonus}", modSlots: 1));
    weapon.GetModSlots()[0].Equip(new MultiStatMod
    {
      StatMods = [new HealthStatMod { Modifiers = [StatModifier.Add(bonus)] }],
    });
    return weapon;
  }
}
