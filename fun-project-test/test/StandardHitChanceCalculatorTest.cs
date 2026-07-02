using FunProject.Battle;
using GdUnit4;
using Godot;
using System.Linq;

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
    var attackerFaction = BattleTestFactory.MakeFaction("Player");
    var board = new BattleBoardState(new Vector3I(8, 1, 8));
    var attackerPoint = board.At(attackerPosition);
    var defenderPoint = board.At(defenderPosition);
    board.GetTile(defenderPoint).Cover = defenderCover;

    var weapon = BattleTestFactory.MakeWeapon("Rifle");
    var attacker = new BattleUnitState(1, BattleTestFactory.MakeCombatant("Alpha", attackerFaction, aim: aim), Some(weapon), None);

    return new AttackContext(attacker, attackerPoint, defenderPoint, board);
  }

  [TestCase(TestName = "Base chance is the attacker's aim when no cover applies")]
  public void BaseChanceIsAttackerAimWhenNoCoverApplies()
  {
    var breakdown = new StandardHitChanceCalculator().Calculate(
      MakeContext(65, TileCover.None, new Vector3I(4, 0, 1), new Vector3I(4, 0, 4)));

    Assert.Equal(65, breakdown.BaseChance);
    Assert.Equal(0, breakdown.Modifiers.Count);
    Assert.Equal(65, breakdown.FinalChance);
  }

  [TestCase(TestName = "Applicable cover subtracts its amount as a named modifier")]
  public void ApplicableCoverSubtractsItsAmountAsNamedModifier()
  {
    var breakdown = new StandardHitChanceCalculator().Calculate(
      MakeContext(65, new TileCover(CoverDirections.North, 40), new Vector3I(4, 0, 1), new Vector3I(4, 0, 4)));

    Assert.Equal(65, breakdown.BaseChance);
    Assert.Equal(1, breakdown.Modifiers.Count);
    Assert.Equal(StandardHitChanceCalculator.CoverModifierLabel, breakdown.Modifiers[0].Label);
    Assert.Equal(-40, breakdown.Modifiers[0].Amount);
    Assert.Equal(25, breakdown.FinalChance);
  }

  [TestCase(TestName = "Diagonal attacker is blocked when either component is covered")]
  public void DiagonalAttackerIsBlockedWhenEitherComponentIsCovered()
  {
    var breakdown = new StandardHitChanceCalculator().Calculate(
      MakeContext(65, new TileCover(CoverDirections.North, 40), new Vector3I(6, 0, 1), new Vector3I(4, 0, 4)));

    Assert.Equal(25, breakdown.FinalChance);
  }

  [TestCase(TestName = "Flanking attacker ignores cover")]
  public void FlankingAttackerIgnoresCover()
  {
    var breakdown = new StandardHitChanceCalculator().Calculate(
      MakeContext(65, new TileCover(CoverDirections.North, 40), new Vector3I(4, 0, 7), new Vector3I(4, 0, 4)));

    Assert.Equal(0, breakdown.Modifiers.Count);
    Assert.Equal(65, breakdown.FinalChance);
  }

  [TestCase(TestName = "Final chance clamps to zero")]
  public void FinalChanceClampsToZero()
  {
    var breakdown = new StandardHitChanceCalculator().Calculate(
      MakeContext(30, new TileCover(CoverDirections.North, 50), new Vector3I(4, 0, 1), new Vector3I(4, 0, 4)));

    Assert.Equal(0, breakdown.FinalChance);
  }

  [TestCase(TestName = "Final chance clamps to one hundred")]
  public void FinalChanceClampsToOneHundred()
  {
    var breakdown = new StandardHitChanceCalculator().Calculate(
      MakeContext(120, TileCover.None, new Vector3I(4, 0, 1), new Vector3I(4, 0, 4)));

    Assert.Equal(100, breakdown.FinalChance);
  }
}
