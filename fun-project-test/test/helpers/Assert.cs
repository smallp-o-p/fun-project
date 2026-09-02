namespace FunProject.Tests;

public static class Assert
{
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

  public static void Contains(string expectedSubstring, string actual, string msg = "")
  {
    if (!actual.Contains(expectedSubstring, StringComparison.Ordinal))
      throw new Exception(string.IsNullOrEmpty(msg)
        ? $"Expected '{actual}' to contain '{expectedSubstring}'."
        : $"Expected '{actual}' to contain '{expectedSubstring}'. {msg}");
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
}
