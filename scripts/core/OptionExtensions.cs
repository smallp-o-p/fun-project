using System;

public static class OptionExtensions
{
  public static T RequireSome<T>(this Option<T> option, string message = "Expected option to contain a value.")
  {
    return option.Match(
      value => value,
      () => throw new InvalidOperationException(message));
  }
}
