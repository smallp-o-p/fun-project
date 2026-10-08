using FunProject.Battle;
using FunProject.Combatants;
using GdUnit4;

[TestSuite]
[RequireGodotRuntime]
public sealed class BattleKnowledgeMovementTest
{
  private static BattleFixture Observer(Vector3I dimensions, int vision, Vector3I? position = null)
  {
    var side = TestData.MakeFaction("Observer");
    return BattleFixture.Started(dimensions, new UnitPlacement(
      new UnitLoadout(TestData.MakeCombatant("Observer", side, vision: vision)), position ?? Vector3I.Zero));
  }

  private static KnownPath Path(BattleFixture battle, Vector3I destination) => battle.Query(
    new FindKnownPathForUnit(battle.PlayerFaction, battle.Alive(battle.PlayerUnit), battle.At(destination)));

  [TestCase]
  public void HiddenOccupancyAndTerrainCannotAlterEstimate()
  {
    using var open = Observer(new Vector3I(5, 1, 1), 1);
    using var blocked = Observer(new Vector3I(5, 1, 1), 1);
    blocked.Board.SetTileWalkable(blocked.At(3, 0, 0), false);
    blocked.Spawn(TestData.MakeCombatant("Hidden", TestData.MakeFaction("Enemy")), new Vector3I(2, 0, 0));
    var a = Path(open, new Vector3I(4, 0, 0)); var b = Path(blocked, new Vector3I(4, 0, 0));
    Assert.True(a.Points.AsValueEnumerable().SequenceEqual(b.Points));
    Assert.True(a.IsEstimated);
    Assert.True(b.IsEstimated);
    var reachA = open.Query(new GetKnownMoveTilesForUnit(open.PlayerFaction, open.Alive(open.PlayerUnit)));
    var reachB = blocked.Query(new GetKnownMoveTilesForUnit(blocked.PlayerFaction, blocked.Alive(blocked.PlayerUnit)));
    Assert.True(reachA.AsValueEnumerable().SequenceEqual(reachB));
  }

  [TestCase]
  public void KnownBlockerConstrainsSearch()
  {
    using var battle = Observer(new Vector3I(4, 1, 3), 4, new Vector3I(0, 0, 1));
    battle.Spawn(TestData.MakeCombatant("Ally", battle.PlayerFaction), new Vector3I(1, 0, 1));
    var path = Path(battle, new Vector3I(2, 0, 1));
    Assert.Equal(3, path.Points.Length);
    Assert.False(path.Points.AsValueEnumerable().Contains(new Vector3I(1, 0, 1)));
    Assert.False(path.IsEstimated);
    Assert.Equal(TraversalKnowledge.Blocked, battle.Query(new GetTileKnowledgeForFaction(battle.PlayerFaction, battle.At(1, 0, 1))).Occupancy);
    Assert.Throws<System.ArgumentException>(() => battle.Query(new FindKnownPathForUnit(
      TestData.MakeFaction("Foreign"), battle.Alive(battle.PlayerUnit), battle.At(2, 0, 1))));
  }

  [TestCase]
  public void UnknownDiagonalFlankMarksEstimate()
  {
    var side = TestData.MakeFaction("Observer");
    using var battle = BattleFixture.Started(new Vector3I(3, 1, 3),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Actor", side, vision: 0)), Vector3I.Zero),
      new UnitPlacement(new UnitLoadout(TestData.MakeCombatant("Scout", side, vision: 2)), new Vector3I(2, 0, 2)));
    var destination = battle.Query(new GetTileKnowledgeForFaction(side, battle.At(1, 0, 1)));
    Assert.True(destination.Terrain.IsSome);
    Assert.Equal(TraversalKnowledge.Clear, destination.Occupancy);
    Assert.Equal(TraversalKnowledge.Unknown, battle.Query(new GetTileKnowledgeForFaction(side, battle.At(1, 0, 0))).Occupancy);
    Assert.Equal(TraversalKnowledge.Unknown, battle.Query(new GetTileKnowledgeForFaction(side, battle.At(0, 0, 1))).Occupancy);
    var path = Path(battle, new Vector3I(1, 0, 1));
    Assert.Equal(2, path.Points.Length);
    Assert.True(path.IsEstimated);
    var diagonal = battle.Query(new GetKnownMoveTilesForUnit(battle.PlayerFaction, battle.Alive(battle.PlayerUnit)))
      .AsValueEnumerable().Single(tile => tile.Position == new Vector3I(1, 0, 1));
    Assert.True(diagonal.IsEstimated);
  }

  [TestCase]
  public void ActualAndKnowledgeSearchShareNeighborhood()
  {
    using var battle = Observer(new Vector3I(3, 2, 3), 10);
    battle.Board.SetTileWalkable(battle.At(1, 0, 0), false);
    battle.Session.InvalidateVisibility();
    battle.EndFactionTurn(battle.PlayerFaction);
    var destination = battle.At(2, 1, 2);
    var actual = battle.Query(new FindPathForUnit(battle.Alive(battle.PlayerUnit), destination));
    var known = Path(battle, destination.Raw);
    Assert.True(actual.AsValueEnumerable().Select(point => point.Raw).SequenceEqual(known.Points));
    Assert.False(known.IsEstimated);
    var actualReach = battle.Query(new GetPossibleMoveTilesForUnit(battle.Alive(battle.PlayerUnit), 0));
    var knownReach = battle.Query(new GetKnownMoveTilesForUnit(battle.PlayerFaction, battle.Alive(battle.PlayerUnit), 0));
    Assert.True(actualReach.AsValueEnumerable().Select(point => point.Raw).SequenceEqual(knownReach.AsValueEnumerable().Select(tile => tile.Position)));
    Assert.False(knownReach.AsValueEnumerable().Any(tile => tile.IsEstimated));
  }

  [TestCase]
  public void ExploredTerrainUsesLastObservation()
  {
    using var battle = Observer(new Vector3I(5, 1, 1), 1, new Vector3I(2, 0, 0));
    var seen = battle.Query(new GetTileKnowledgeForFaction(battle.PlayerFaction, battle.At(3, 0, 0)));
    battle.Move(battle.PlayerUnit, [new Vector3I(1, 0, 0)]);
    battle.Board.SetTileWalkable(battle.At(3, 0, 0), false);
    var unseen = battle.Query(new GetTileKnowledgeForFaction(battle.PlayerFaction, battle.At(3, 0, 0)));
    Assert.Equal(seen.Terrain, unseen.Terrain);
    Assert.Equal(TraversalKnowledge.Unknown, unseen.Occupancy);
    Assert.True(battle.Query(new GetTileKnowledgeForFaction(battle.PlayerFaction, battle.At(4, 0, 0))).Terrain.IsNone);
  }
}
