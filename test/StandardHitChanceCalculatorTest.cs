using FunProject.Battle;
using GdUnit4;
using Godot;

[TestSuite]
[RequireGodotRuntime]
public partial class StandardHitChanceCalculatorTest
{
  private static AttackContext MakeContext(
    int aim,
    TileCover defenderCover,
    Vector3I attackerPosition,
    Vector3I defenderPosition)
  {
    var attackerFaction = TestData.MakeFaction("Player");
    var board = new BattleBoardState(new Vector3I(8, 1, 8));
    var attackerPoint = board.At(attackerPosition);
    var defenderPoint = board.At(defenderPosition);
    board.GetTile(defenderPoint).Cover = defenderCover;

    var weapon = TestData.MakeWeapon("Rifle");
    var attacker = new BattleUnitState(1, TestData.MakeCombatant("Alpha", attackerFaction, aim: aim), Some(weapon), None);

    return new AttackContext(attacker, weapon, attackerPoint, defenderPoint, board);
  }

  [TestCase(65, 0, 4, 1, 65, 0, TestName = "BaseChanceIsAttackerAimWhenNoCoverApplies")]
  [TestCase(65, 40, 4, 1, 25, 1, TestName = "ApplicableCoverSubtractsItsAmountAsNamedModifier")]
  [TestCase(65, 40, 6, 1, 25, 1, TestName = "DiagonalAttackerIsBlockedWhenEitherComponentIsCovered")]
  [TestCase(65, 40, 4, 7, 65, 0, TestName = "FlankingAttackerIgnoresCover")]
  [TestCase(30, 50, 4, 1, 0, 1, TestName = "FinalChanceClampsToZero")]
  [TestCase(120, 0, 4, 1, 100, 0, TestName = "FinalChanceClampsToOneHundred")]
  public void HitChance(
    int aim,
    int coverAmount,
    int attackX,
    int attackZ,
    int expectedFinal,
    int expectedModifiers)
  {
    var breakdown = new StandardHitChanceCalculator().Calculate(
      MakeContext(aim, new TileCover(coverAmount, 0, 0, 0), new Vector3I(attackX, 0, attackZ), new Vector3I(4, 0, 4)));

    Assert.Equal(aim, breakdown.BaseChance);
    Assert.Equal(expectedModifiers, breakdown.Modifiers.Count);
    if (expectedModifiers == 1)
    {
      Assert.Equal(StandardHitChanceCalculator.CoverModifierLabel, breakdown.Modifiers[0].Label);
      Assert.Equal(-coverAmount, breakdown.Modifiers[0].Amount);
    }
    Assert.Equal(expectedFinal, breakdown.FinalChance);
  }

  [TestCase(4, 1, 10)]
  [TestCase(7, 4, 30)]
  [TestCase(4, 7, 50)]
  [TestCase(1, 4, 70)]
  [TestCase(7, 1, 30)]
  [TestCase(1, 1, 70)]
  [TestCase(7, 7, 50)]
  [TestCase(1, 7, 70)]
  public void IndependentSidesSubtractTheStrongestApplicableCover(int attackX, int attackZ, int expectedCover)
  {
    var breakdown = new StandardHitChanceCalculator().Calculate(
      MakeContext(80, new TileCover(10, 30, 50, 70), new Vector3I(attackX, 0, attackZ), new Vector3I(4, 0, 4)));

    Assert.Equal(80, breakdown.BaseChance);
    Assert.Equal(1, breakdown.Modifiers.Count);
    Assert.Equal(StandardHitChanceCalculator.CoverModifierLabel, breakdown.Modifiers[0].Label);
    Assert.Equal(-expectedCover, breakdown.Modifiers[0].Amount);
    Assert.Equal(80 - expectedCover, breakdown.FinalChance);
  }
}
