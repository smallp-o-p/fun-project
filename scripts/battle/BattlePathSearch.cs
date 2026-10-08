using System;
using System.Collections.Generic;
using Point = FunProject.Battle.BattleBoardState.ValidatedPoint;

namespace FunProject.Battle;

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

  internal static Point[] FindPath(Vector3I dimensions, Point source, Point destination, Func<Point, TraversalKnowledge> knowledge)
  {
    if (source == destination) return [source];
    if (knowledge(destination) == TraversalKnowledge.Blocked) return [];
    Dictionary<Point, Point> parents = [];
    Queue<Point> frontier = new();
    parents[source] = source;
    frontier.Enqueue(source);
    while (frontier.Count > 0)
    {
      var current = frontier.Dequeue();
      if (current == destination)
      {
        List<Point> reversed = [];
        for (var point = destination; point != source; point = parents[point]) reversed.Add(point);
        reversed.Add(source);
        return reversed.AsValueEnumerable().Reverse().ToArray();
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
        parents[next] = current;
        frontier.Enqueue(next);
      }
    }
    return [];
  }

  internal static IReadOnlyList<Point> GetReachableTiles(Vector3I dimensions, Point origin, int maxSteps,
    Func<Point, TraversalKnowledge> knowledge)
  {
    List<Point> reachable = [];
    SysColGeneric.HashSet<Point> visited = [origin];
    Queue<(Point Point, int Steps)> frontier = new();
    frontier.Enqueue((origin, 0));
    while (frontier.Count > 0)
    {
      var (current, steps) = frontier.Dequeue();
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
        reachable.Add(next);
        frontier.Enqueue((next, steps + 1));
      }
    }
    return reachable.AsReadOnly();
  }
}
