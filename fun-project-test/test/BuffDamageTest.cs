using FunProject.Battle;
using FunProject.Buffs;
using FunProject.Stats;
using FunProject.Weapons;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public partial class BuffDamageTest
{
  private static DamageBundleMod AddThreeToAllPackets() =>
    new()
    {
      PacketModifiers = [new PacketModifier { AffectAllElements = true, Ops = [StatModifier.Add(3)] }],
    };

  [TestCase(TestName = "EmitDamage folds external mods after the weapon's own")]
  public void EmitDamageFoldsExternalMods()
  {
    var weapon = MakeWeapon("Blade", damage: 5);

    Assert.Equal(5, weapon.EmitDamage().AsValueEnumerable().Sum(damage => damage.Amount));
    Assert.Equal(8, weapon.EmitDamage([AddThreeToAllPackets()]).AsValueEnumerable().Sum(damage => damage.Amount));
  }

  [TestCase(TestName = "TrySpendShot threads external mods through the ammunitioned path")]
  public void TrySpendShotThreadsExternalMods()
  {
    var weapon = MakeAmmoWeapon("Rifle", magazine: 6, damage: 5);

    var bundle = weapon.TrySpendShot([AddThreeToAllPackets()]).RequireSome();
    Assert.Equal(8, bundle.AsValueEnumerable().Sum(damage => damage.Amount));
    Assert.Equal(5, weapon.CurrentAmmo); // the shot was still spent
  }

  [TestCase(TestName = "An attack applies the attacker's active damage buff; inactive applies none")]
  public void AttackAppliesActiveDamageBuff()
  {
    var damageBuff = MakeBuff(
      "Rampage",
      new HealthBelowPercentCondition { Percent = 50f },
      damageMods: [AddThreeToAllPackets()]);
    var battle = new BattleDuelBuilder
    {
      HitChanceCalculator = new AlwaysHitCalculator(),
      Player = new DuelSide("Alpha", Weapon: MakeWeapon("Saber", damage: 5), Buffs: [damageBuff]),
      Enemy = new DuelSide("Hostile", Health: 20),
    }.Start();
    var enemy = battle.EnemyUnit.State;

    Attack(battle.Session, battle.Executor, battle.PlayerUnit, battle.EnemyUnit); // buff inactive: base 5
    Assert.Equal(15, enemy.CurrentHealth);

    ApplyDamage(battle.Session, battle.PlayerUnit.State, 11);      // player 9/20: condition holds
    EndFactionTurn(battle.Executor, battle.PlayerFaction);         // enemy turn (buff activates)
    EndFactionTurn(battle.Executor, battle.EnemyFaction);          // player turn again

    Attack(battle.Session, battle.Executor, battle.PlayerUnit, battle.EnemyUnit); // buff active: 5 + 3
    Assert.Equal(7, enemy.CurrentHealth);
  }
}
