using FunProject.Weapons;
using GdUnit4;

namespace FunProject.Tests;

[TestSuite]
[RequireGodotRuntime]
public sealed class BattleUnitReadoutTest
{
  [TestCase]
  public static void ReadoutShowsCurrentStunAndUnconsciousness()
  {
    using var battle = BattleFixture.Duel();

    battle.ApplyDamage(battle.PlayerUnit, 5, DamageKind.Stun);
    string conscious = BattleHud.FormatUnitReadout(battle.PlayerUnit);
    Assert.True(conscious.Contains("STUN 5", System.StringComparison.Ordinal));
    Assert.False(conscious.Contains("Unconscious", System.StringComparison.Ordinal));

    battle.ApplyDamage(battle.PlayerUnit, 15, DamageKind.Stun);
    string unconscious = BattleHud.FormatUnitReadout(battle.PlayerUnit);
    Assert.True(unconscious.Contains("STUN 20", System.StringComparison.Ordinal));
    Assert.True(unconscious.Contains("Unconscious", System.StringComparison.Ordinal));
  }
}
