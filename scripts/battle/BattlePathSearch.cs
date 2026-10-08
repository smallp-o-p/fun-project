using System;
using System.Collections.Generic;
using Point = FunProject.Battle.BattleBoardState.ValidatedPoint;

namespace FunProject.Battle;

internal readonly record struct SearchPath(Point[] Points, bool IsEstimated);
internal readonly record struct SearchReachableTile(Point Point, bool IsEstimated);

internal static class BattlePathSearch
{
  // Order is part of exact movement's shortest-path tie break. Y has no diagonals.
  internal static readonly Vector3I[] AdjacentDirections =
  [
    new(-1, 0, 0), new(1, 0, 0), new(0, 0, -1), new(0, 0, 1), new(0, -1, 0), new(0, 1, 0),
    new(1, 0, 1), new(-1, 0, -1), new(1, 0, -1), new(-1, 0, 1)
  ];

  private static bool InBounds(Vector3I dimensions, Vector3I point) => point.X >= 0 && point.Y >= 0 && point.Z >= 0
    && point.X < dimensions.X && point.Y < dimensions.Y && point.Z < dimensions.Z;

  internal static TraversalKnowledge StepKnowledge(Vector3I dimensions, Point from, Vector3I direction,
    Func<Point, TraversalKnowledge> knowledge)
  {
    if (direction.X == 0 || direction.Z == 0) return TraversalKnowledge.Clear;
    TraversalKnowledge Flank(Vector3I point) => InBounds(dimensions, point) ? knowledge(new(point)) : TraversalKnowledge.Blocked;
    var x = Flank(from.Raw + new Vector3I(direction.X, 0, 0));
    var z = Flank(from.Raw + new Vector3I(0, 0, direction.Z));
    if (x == TraversalKnowledge.Clear || z == TraversalKnowledge.Clear) return TraversalKnowledge.Clear;
    return x == TraversalKnowledge.Blocked && z == TraversalKnowledge.Blocked ? TraversalKnowledge.Blocked : TraversalKnowledge.Unknown;
  }

  internal static SearchPath FindPath(Vector3I dimensions, Point source, Point destination, Func<Point, TraversalKnowledge> knowledge)
  {
    if (source == destination) return new([source], false);
    if (knowledge(destination) == TraversalKnowledge.Blocked) return new([], false);
    Dictionary<Point, (Point Parent, bool Estimated)> parents = [];
    Queue<Point> frontier = new();
    parents[source] = (source, false);
    frontier.Enqueue(source);
    while (frontier.Count > 0)
    {
      var current = frontier.Dequeue();
      if (current == destination)
      {
        List<Point> reversed = [];
        for (var point = destination; point != source; point = parents[point].Parent) reversed.Add(point);
        reversed.Add(source);
        return new(reversed.AsValueEnumerable().Reverse().ToArray(), parents[destination].Estimated);
      }
      foreach (var direction in AdjacentDirections)
      {
        var raw = current.Raw + direction;
        if (!InBounds(dimensions, raw)) continue;
        var next = new Point(raw);
        if (parents.ContainsKey(next)) continue;
        var tile = knowledge(next);
        if (tile == TraversalKnowledge.Blocked) continue;
        var step = StepKnowledge(dimensions, current, direction, knowledge);
        if (step == TraversalKnowledge.Blocked) continue;
        parents[next] = (current, parents[current].Estimated || tile == TraversalKnowledge.Unknown || step == TraversalKnowledge.Unknown);
        frontier.Enqueue(next);
      }
    }
    return new([], false);
  }

  internal static IReadOnlyList<SearchReachableTile> GetReachableTiles(Vector3I dimensions, Point origin, int maxSteps,
    Func<Point, TraversalKnowledge> knowledge)
  {
    List<SearchReachableTile> reachable = [];
    SysColGeneric.HashSet<Point> visited = [origin];
    Queue<(Point Point, int Steps, bool Estimated)> frontier = new();
    frontier.Enqueue((origin, 0, false));
    while (frontier.Count > 0)
    {
      var (current, steps, estimated) = frontier.Dequeue();
      if (steps >= maxSteps) continue;
      foreach (var direction in AdjacentDirections)
      {
        var raw = current.Raw + direction;
        if (!InBounds(dimensions, raw)) continue;
        var next = new Point(raw);
        // Preserve exact flood's first-visit order, including rejected corner approaches.
        if (!visited.Add(next)) continue;
        var tile = knowledge(next);
        if (tile == TraversalKnowledge.Blocked) continue;
        var step = StepKnowledge(dimensions, current, direction, knowledge);
        if (step == TraversalKnowledge.Blocked) continue;
        bool nextEstimated = estimated || tile == TraversalKnowledge.Unknown || step == TraversalKnowledge.Unknown;
        reachable.Add(new(next, nextEstimated));
        frontier.Enqueue((next, steps + 1, nextEstimated));
      }
    }
    return reachable.AsReadOnly();
  }
}
