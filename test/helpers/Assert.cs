#nullable disable warnings
using System;
using System.Collections.Generic;
using FunProject.Battle;
using Godot;

namespace FunProject.Tests;

public static class Assert
{
  public static T RequireSome<T>(this Option<T> option, string message = "Expected option to contain a value.")
  {
    return option.Match(
      value => value,
      () => throw new InvalidOperationException(message));
  }

  // Sugar for `board.ValidatePoint(...).RequireSome()`, the pervasive test idiom for turning a raw
  // coordinate into a ValidatedPoint.
  public static BattleBoardState.ValidatedPoint At(this BattleBoardState board, int x, int y, int z) =>
    board.ValidatePoint(new Vector3I(x, y, z)).RequireSome();

  public static BattleBoardState.ValidatedPoint At(this BattleBoardState board, Vector3I position) =>
    board.ValidatePoint(position).RequireSome();

  public static R RequireRight<L, R>(this Either<L, R> result) => result.Match(
    Left: failure => throw new InvalidOperationException($"Expected Right, got Left: {failure}"),
    Right: value => value);

  public static L RequireLeft<L, R>(this Either<L, R> result) => result.Match(
    Left: failure => failure,
    Right: value => throw new InvalidOperationException($"Expected Left, got Right: {value}"));

  public static T[] EventsOf<T>(this IEnumerable<object> events) =>
    events.AsValueEnumerable().OfType<T>().ToArray();

  public static T SingleEvent<T>(this IEnumerable<object> events)
  {
    T[] matches = events.EventsOf<T>();
    if (matches.Length != 1)
      throw new InvalidOperationException($"Expected exactly one {typeof(T).Name}, found {matches.Length}.");
    return matches[0];
  }

  public static int EventIndex<T>(this IEnumerable<object> events, Func<T, bool> predicate = null)
  {
    int index = 0;
    foreach (object value in events)
    {
      if (value is T typed && (predicate is null || predicate(typed)))
        return index;
      index++;
    }
    return -1;
  }

  public static void EventBefore<TFirst, TSecond>(this IEnumerable<object> events)
  {
    object[] snapshot = events.AsValueEnumerable().ToArray();
    int first = snapshot.EventIndex<TFirst>();
    int second = snapshot.EventIndex<TSecond>();
    True(first >= 0, $"Expected {typeof(TFirst).Name}.");
    True(second > first, $"Expected {typeof(TSecond).Name} after {typeof(TFirst).Name}.");
  }

  public static void That(bool condition, string msg = "")
  {
    if (!condition)
      throw new Exception(string.IsNullOrEmpty(msg) ? "Expected true but got false." : $"Expected true but got false. {msg}");
  }

  public static void True(bool condition, string msg = "") => That(condition, msg);

  public static void False(bool condition, string msg = "")
  {
    if (condition)
      throw new Exception(string.IsNullOrEmpty(msg) ? "Expected false but got true." : $"Expected false but got true. {msg}");
  }

  public static void Equal<T>(T expect, T real, string msg = "")
  {
    if (!EqualityComparer<T>.Default.Equals(expect, real))
      throw new Exception(string.IsNullOrEmpty(msg) ? $"Expected {expect} but got {real}." : $"Expected {expect} but got {real}. {msg}");
  }

  public static void Throws<T>(Action body, string msg = "") where T : Exception
  {
    try
    {
      body();
    }
    catch (Exception exception) when (exception is T)
    {
      return;
    }

    throw new Exception(string.IsNullOrEmpty(msg) ? $"Expected {typeof(T).Name} but no exception was thrown." : $"Expected {typeof(T).Name} but no exception was thrown. {msg}");
  }

  // Asserts the exact enabled region set of a model's mask — the readable
  // replacement of the removed numeric bit diagnostics.
  public static void MaskRegions(FunProject.Models.CharacterModel model, NodePath meshPath,
    params string[] enabled)
  {
    var expected = new SysColGeneric.HashSet<string>(enabled);
    var actual = new SysColGeneric.HashSet<string>();
    foreach (FunProject.Models.CharacterModel.MaskRuntime.Region region in model.ResolveMask(meshPath).Regions)
    {
      if (region.Enabled)
        actual.Add(region.Name.ToString());
    }

    if (!expected.SetEquals(actual))
      throw new Exception(
        $"Expected the mask '{meshPath}' regions [{string.Join(", ", expected)}] enabled, "
        + $"but found [{string.Join(", ", actual)}].");
  }
}
