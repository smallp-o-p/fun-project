using System;

namespace FunProject.Tests;

internal static class OptionTestExtensions
{
  public static T RequireSome<T>(this Option<T> option, string message = "Expected option to contain a value.")
  {
    return option.Match(
      value => value,
      () => throw new InvalidOperationException(message));
  }
}
