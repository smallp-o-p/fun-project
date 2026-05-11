using System;
using System.Collections.Generic;
using FunProject.Battle;

namespace FunProject.Tests;

internal static class OptionTestExtensions
{
  public static T RequireSome<T>(this Option<T> option, string message = "Expected option to contain a value.")
  {
    return option.Match(
      value => value,
      () => throw new InvalidOperationException(message));
  }

  public static BattleActionResult RequireSingleResult(
    this IReadOnlyList<BattleActionResult> results,
    string message = "Expected exactly one action result.")
  {
    if (results.Count != 1)
      throw new InvalidOperationException($"{message} Actual count: {results.Count}.");

    return results[0];
  }
}
