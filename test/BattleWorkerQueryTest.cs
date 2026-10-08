using System;
using System.Collections.Generic;
using System.Threading;
using FunProject.Battle;
using FunProject.Buffs;
using FunProject.Stats;
using FunProject.Weapons;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public sealed class BattleWorkerQueryTest
{
  [TestCase]
  public void PreparedStatsMatchExistingFold()
  {
    StatModifier[] modifiers = [StatModifier.Add(0.125f), StatModifier.PercentAdd(0.2f),
      StatModifier.Multiply(1.2f), StatModifier.Override(13.333333f),
      StatModifier.CapMin(12f), StatModifier.CapMax(13.2f)];
    var mod = new HealthStatMod { Modifiers = [.. modifiers] };
    var buff = TestData.MakeBuff("Repeated", new HealthBelowPercentCondition { Percent = 101f }, statMods: [mod]);
    var inputs = BattleStatInputs.Capture(new Dictionary<Type, float> { [typeof(HealthStat)] = 10f }, [mod], [buff, buff]);
    Assert.Equal(HasStats.Fold(10f, modifiers.AsValueEnumerable().Concat(modifiers).Concat(modifiers).ToArray()),
      inputs.TryResolve<HealthStat>([buff, buff]).RequireSome());
    Assert.True(inputs.TryResolve<AimStat>([]).IsNone);
    Assert.Equal(HasStats.Fold(10f, modifiers), HasStats.Fold(10f,
      modifiers.AsValueEnumerable().Select(m => new StatModifierValue(m.Operation, m.Value)).ToArray()));
    StatModifier[] arithmetic = [StatModifier.Add(0.125f), StatModifier.PercentAdd(0.2f), StatModifier.Multiply(1.2f)];
    var arithmeticMod = new HealthStatMod { Modifiers = [.. arithmetic] };
    var repeated = TestData.MakeBuff("Stack", new HealthBelowPercentCondition { Percent = 101f }, statMods: [arithmeticMod]);
    var stacked = BattleStatInputs.Capture(new Dictionary<Type, float> { [typeof(HealthStat)] = 10f }, [arithmeticMod], [repeated, repeated]);
    Assert.Equal(HasStats.Fold(10f, arithmetic.AsValueEnumerable().Concat(arithmetic).Concat(arithmetic).ToArray()),
      stacked.TryResolve<HealthStat>([repeated, repeated]).RequireSome());
  }

  [TestCase]
  public void WorkerCanReadBuiltInAvailabilityPathAndPreview()
  {
    using var battle = BattleFixture.Duel(player: new("Alpha", Weapon: TestData.MakeAmmoWeapon("Rifle")));
    var actor = battle.Alive(battle.PlayerUnit);
    var target = battle.Target(battle.EnemyUnit);
    var destination = battle.At(3, 0, 1);
    (int Chance, int PathLength, string Facts, float Aim) Read() => (
      battle.Query(new GetHitChanceForAttack(actor, target)).RequireRight().FinalChance,
      battle.Query(new FindPathForUnit(actor, destination)).Length,
      string.Join(",", battle.Query(new GetUnitActionFacts(actor)).AsValueEnumerable().Select(f => f.IsAvailable).ToArray()),
      battle.PlayerUnit.EffectiveStat<AimStat>());
    var host = battle.Read(Read);
    var worker = OnWorker(() => battle.Read(Read));
    Assert.Equal(host, worker);
  }

  [TestCase]
  public void PreviewDoesNotAdvanceRngOrInvokeHooks()
  {
    using var control = BattleFixture.Duel(player: new("Alpha", Weapon: TestData.MakeWeapon("Rifle")), enemy: new("Durable", Health: 200));
    using var observed = BattleFixture.Duel(player: new("Alpha", Weapon: TestData.MakeWeapon("Rifle")), enemy: new("Durable", Health: 200));
    int broadcasts = 0;
    observed.Runtime.BattleEventCommitted += _ => broadcasts++;
    OnWorker(() => observed.Read(() =>
    {
      for (int i = 0; i < 50; i++)
        observed.Query(new GetHitChanceForAttack(observed.Alive(observed.PlayerUnit), observed.Target(observed.EnemyUnit))).RequireRight();
      return 0;
    }));
    Assert.Equal(0, broadcasts);
    control.ClearEvents(); observed.ClearEvents();
    control.Attack(control.PlayerUnit, control.EnemyUnit);
    observed.Attack(observed.PlayerUnit, observed.EnemyUnit);
    Assert.Equal(control.Events.SingleEvent<UnitAttackedBattleEvent>().Roll,
      observed.Events.SingleEvent<UnitAttackedBattleEvent>().Roll);
  }

  [TestCase]
  public void BuffAndEquipmentMutationRefreshPreparedInputs()
  {
    var weapon = new FirearmWeapon(TestData.MakeFirearmWeaponData(range: 10, modSlots: 1));
    var buff = TestData.MakeBuff("Aim", new HealthBelowPercentCondition { Percent = 101f },
      statMods: [new AimStatMod { Modifiers = [StatModifier.Add(0.25f)] }]);
    var conditional = TestData.MakeBuff("Low health", new HealthBelowPercentCondition { Percent = 50f },
      statMods: [new AimStatMod { Modifiers = [StatModifier.Add(7)] }]);
    using var battle = BattleFixture.Duel(player: new("Alpha", Weapon: weapon, Buffs: [buff, buff, conditional]));
    Assert.Equal(65.5f, OnWorker(() => battle.Read(() => battle.PlayerUnit.EffectiveStat<AimStat>())));
    var slot = battle.Read(() => weapon.GetModSlots()[0]);
    battle.Mutate(() => slot.Equip(new MultiStatMod { StatMods = [new RangeStatMod { Modifiers = [StatModifier.Add(3)] }] }));
    Assert.Equal(13, OnWorker(() => battle.Read(() => weapon.EffectiveRange)));
    battle.Mutate(() => weapon.AmmoType = Some(new Ammunition { Modifiers = [new RangeStatMod { Modifiers = [StatModifier.Add(2)] }] }));
    Assert.Equal(15, OnWorker(() => battle.Read(() => weapon.EffectiveRange)));
    Assert.Equal(battle.Read(() => weapon.EffectiveRange), OnWorker(() => battle.Read(() => weapon.EffectiveRange)));
    battle.ApplyDamage(battle.PlayerUnit, 11);
    battle.AdvanceTurn();
    battle.AdvanceTurn();
    Assert.Equal(72.5f, OnWorker(() => battle.Read(() => battle.PlayerUnit.EffectiveStat<AimStat>())));
  }

  private static T OnWorker<T>(Func<T> read)
  {
    T value = default!; Exception? failure = null;
    var thread = new Thread(() => { try { value = read(); } catch (Exception ex) { failure = ex; } });
    thread.Start();
    if (!thread.Join(TimeSpan.FromSeconds(5))) throw new TimeoutException("Worker read did not finish.");
    if (failure is not null) throw new InvalidOperationException("Worker query failed.", failure);
    return value;
  }
}
